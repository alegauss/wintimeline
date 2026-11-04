using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace WinTimeline.Engine.Jobs;

/// <summary>Who is waiting on a job: what the user asked for now, what is on screen, or neither.</summary>
public enum JobPriority
{
    /// <summary>Nobody is waiting: proxies, analysis of media not in view.</summary>
    Background = 0,

    /// <summary>What is on screen: thumbnails and waveforms in view.</summary>
    Visible = 1,

    /// <summary>What the user just asked for and is watching.</summary>
    Interactive = 2,
}

/// <summary>Where a job is.</summary>
public enum JobState
{
    /// <summary>Waiting for its turn and its resources.</summary>
    Queued,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Stopped at a checkpoint by the user; resumes from it.</summary>
    Paused,

    /// <summary>Finished.</summary>
    Completed,

    /// <summary>Stopped by an error after its last attempt.</summary>
    Failed,

    /// <summary>Stopped by the user, for good.</summary>
    Cancelled,
}

/// <summary>What to run.</summary>
/// <param name="Kind">The kind of work, e.g. "proxy", which the scheduler's registry runs.</param>
/// <param name="Title">What the Jobs panel calls it, e.g. "Proxy for A001.mov".</param>
/// <param name="Arguments">What the work needs, kept so it can be run again after a restart.</param>
/// <param name="Priority">Who is waiting on it.</param>
/// <param name="Needs">The resources it holds while running, e.g. { "gpu-encoder": 1 }.</param>
/// <param name="MaxAttempts">How many times it is tried before it fails.</param>
public sealed record JobRequest(string Kind, string Title, JsonObject Arguments, JobPriority Priority, IReadOnlyDictionary<string, int> Needs, int MaxAttempts = 1);

/// <summary>The work a kind of job does. It reads where it got to from <see cref="JobContext.Checkpoint"/>.</summary>
/// <param name="context">The job's context.</param>
/// <returns>When the work is done.</returns>
public delegate Task JobWork(JobContext context);

/// <summary>Thrown out of a checkpoint when the job must stop there and run again later from it.</summary>
public sealed class JobStoppedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public JobStoppedException()
        : base("the job stopped at its checkpoint")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public JobStoppedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">The cause.</param>
    public JobStoppedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>What a running job sees: its arguments, where it got to, and the way to report and to stop.</summary>
public sealed class JobContext
{
    private readonly Job _job;
    private readonly JobScheduler _scheduler;

    internal JobContext(Job job, JobScheduler scheduler, CancellationToken cancellation)
    {
        _job = job;
        _scheduler = scheduler;
        Cancellation = cancellation;
    }

    /// <summary>The job's arguments.</summary>
    public JsonObject Arguments => _job.Request.Arguments;

    /// <summary>Where the job got to when it last stopped, or an empty object on its first run.</summary>
    public JsonObject Checkpoint => (JsonObject)_job.CheckpointNode.DeepClone();

    /// <summary>Set when the job is cancelled.</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>Reports how far the job is, from 0 to 1; progress never goes back.</summary>
    /// <param name="fraction">How far.</param>
    public void Report(double fraction) => _scheduler.Report(_job, fraction);

    /// <summary>
    /// Records where the job got to and gives the scheduler a place to stop it: a pause, the
    /// editor throttling background work, or a shutdown ends the run here, and the next run
    /// starts from this checkpoint; a cancel ends it for good.
    /// </summary>
    /// <param name="checkpoint">Enough to carry on from here.</param>
    /// <exception cref="JobStoppedException">The job must stop here.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public void Save(JsonObject checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Cancellation.ThrowIfCancellationRequested();
        _job.CheckpointNode = (JsonObject)checkpoint.DeepClone();
        if (_scheduler.MustStop(_job))
        {
            throw new JobStoppedException();
        }
    }
}

/// <summary>A job in the scheduler, as the Jobs panel shows it.</summary>
public sealed class Job
{
    private readonly Stopwatch _sinceStart = new();
    private double _progressAtStart;

    internal Job(string id, JobRequest request, long order)
    {
        Id = id;
        Request = request;
        Order = order;
    }

    /// <summary>Its identity, kept across restarts.</summary>
    public string Id { get; }

    /// <summary>What it runs.</summary>
    public JobRequest Request { get; }

    /// <summary>Where it is.</summary>
    public JobState State { get; internal set; }

    /// <summary>How far it is, from 0 to 1.</summary>
    public double Progress { get; internal set; }

    /// <summary>How many times it has been started.</summary>
    public int Attempts { get; internal set; }

    /// <summary>Why it last failed, or null.</summary>
    public string? Error { get; internal set; }

    /// <summary>How long it should still take at the pace of its current run, or null before there is a pace.</summary>
    public TimeSpan? Eta
    {
        get
        {
            double done = Progress - _progressAtStart;
            return State != JobState.Running || done <= 0
                ? null
                : TimeSpan.FromTicks((long)(_sinceStart.Elapsed.Ticks / done * (1 - Progress)));
        }
    }

    internal long Order { get; }

    internal JsonObject CheckpointNode { get; set; } = [];

    internal bool PauseRequested { get; set; }

    internal CancellationTokenSource? Cancel { get; set; }

    internal void Started()
    {
        _progressAtStart = Progress;
        _sinceStart.Restart();
    }
}

/// <summary>
/// WT40: one queue for long work. Jobs run by priority, then in the order they came, each only
/// while the resources it needs are free; lower priorities fill what a higher one cannot use.
/// A job stops at its checkpoints, never in the middle of a step, so a pause, a throttle and a
/// restart all resume it from where it got to.
/// </summary>
public sealed class JobScheduler : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _capacity;
    private readonly Dictionary<string, int> _inUse = new(StringComparer.Ordinal);
    private readonly Func<string, JobWork?> _kinds;
    private readonly List<Job> _jobs = [];
    private readonly Dictionary<Job, Task> _running = [];
    private long _order;
    private bool _throttled;
    private bool _stopping;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="capacity">How many of each resource there are, e.g. { "gpu-encoder": 2, "cpu": 8 }.</param>
    /// <param name="kinds">The work for each kind of job, or null for a kind this build does not know.</param>
    public JobScheduler(IReadOnlyDictionary<string, int> capacity, Func<string, JobWork?> kinds)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        _capacity = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
        _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    }

    /// <summary>Raised, on a worker thread, whenever a job changes state or progress.</summary>
    public event EventHandler<Job>? Changed;

    /// <summary>Every job, in the order they were queued.</summary>
    public IReadOnlyList<Job> Jobs
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs];
            }
        }
    }

    /// <summary>
    /// Whether background work steps aside, as it does during playback: no background job starts,
    /// and a running one stops at its next checkpoint and queues again.
    /// </summary>
    public bool Throttled
    {
        get => _throttled;
        set
        {
            lock (_gate)
            {
                _throttled = value;
            }

            Pump();
        }
    }

    /// <summary>How far all unfinished work is together, from 0 to 1, as the taskbar shows it; 1 when there is none.</summary>
    public double OverallProgress
    {
        get
        {
            lock (_gate)
            {
                Job[] counted = [.. _jobs.Where(j => j.State is not JobState.Cancelled)];
                return counted.Length == 0 ? 1 : counted.Average(j => j.Progress);
            }
        }
    }

    /// <summary>Queues a job.</summary>
    /// <param name="request">What to run.</param>
    /// <returns>The job.</returns>
    /// <exception cref="ArgumentException">It needs more of a resource than there is, or its kind is unknown.</exception>
    public Job Enqueue(JobRequest request) => Add(request, Guid.NewGuid().ToString("N"), [], 0);

    /// <summary>Stops a job at its next checkpoint, or before it starts.</summary>
    /// <param name="job">The job.</param>
    public void Pause(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            if (job.State == JobState.Queued)
            {
                job.State = JobState.Paused;
            }
            else if (job.State == JobState.Running)
            {
                job.PauseRequested = true;
                return;
            }
            else
            {
                return;
            }
        }

        Changed?.Invoke(this, job);
    }

    /// <summary>Queues a paused job again; it carries on from its checkpoint.</summary>
    /// <param name="job">The job.</param>
    public void Resume(Job job) => Requeue(job, JobState.Paused, resetAttempts: false);

    /// <summary>Queues a failed or cancelled job again, from its last checkpoint and with its attempts reset.</summary>
    /// <param name="job">The job.</param>
    public void Retry(Job job)
    {
        Requeue(job, JobState.Failed, resetAttempts: true);
        Requeue(job, JobState.Cancelled, resetAttempts: true);
    }

    /// <summary>Stops a job for good.</summary>
    /// <param name="job">The job.</param>
    public void Cancel(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            if (job.State == JobState.Running)
            {
                job.Cancel?.Cancel();
                return;
            }

            if (job.State is not (JobState.Queued or JobState.Paused))
            {
                return;
            }

            job.State = JobState.Cancelled;
        }

        Changed?.Invoke(this, job);
    }

    /// <summary>Waits until nothing is running and nothing queued can start.</summary>
    /// <returns>When idle.</returns>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_gate)
            {
                running = [.. _running.Values];
            }

            if (running.Length == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops every running job at its next checkpoint and returns what is unfinished, to be kept
    /// in the project sidecar and given to <see cref="Restore"/> after a restart.
    /// </summary>
    /// <returns>The unfinished jobs.</returns>
    public async Task<JsonArray> ShutdownAsync()
    {
        lock (_gate)
        {
            _stopping = true;
        }

        await WhenIdleAsync().ConfigureAwait(false);
        return Snapshot();
    }

    /// <summary>The unfinished jobs as they stand: a running one is kept as queued, from its last checkpoint.</summary>
    /// <returns>The jobs.</returns>
    public JsonArray Snapshot()
    {
        lock (_gate)
        {
            return new JsonArray([.. _jobs.Where(j => j.State is JobState.Queued or JobState.Running or JobState.Paused).Select(j => (JsonNode)new JsonObject
            {
                ["id"] = j.Id,
                ["kind"] = j.Request.Kind,
                ["title"] = j.Request.Title,
                ["arguments"] = j.Request.Arguments.DeepClone(),
                ["priority"] = j.Request.Priority.ToString(),
                ["needs"] = new JsonObject(j.Request.Needs.Select(n => KeyValuePair.Create(n.Key, (JsonNode?)n.Value))),
                ["max_attempts"] = j.Request.MaxAttempts,
                ["paused"] = j.State == JobState.Paused,
                ["progress"] = j.Progress,
                ["checkpoint"] = j.CheckpointNode.DeepClone(),
            })]);
        }
    }

    /// <summary>Queues again what a <see cref="Snapshot"/> kept, each from its checkpoint; a paused job stays paused.</summary>
    /// <param name="saved">The snapshot.</param>
    /// <returns>The jobs, in the snapshot's order.</returns>
    /// <exception cref="ArgumentException">An entry is malformed or names a kind this build does not know.</exception>
    public IReadOnlyList<Job> Restore(JsonArray saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        List<Job> restored = [];
        foreach (JsonObject entry in saved.OfType<JsonObject>())
        {
            try
            {
                JobRequest request = new(
                    entry["kind"]!.GetValue<string>(),
                    entry["title"]?.GetValue<string>() ?? string.Empty,
                    (JsonObject)(entry["arguments"]?.DeepClone() ?? new JsonObject()),
                    Enum.Parse<JobPriority>(entry["priority"]!.GetValue<string>()),
                    (entry["needs"] as JsonObject ?? []).ToDictionary(n => n.Key, n => n.Value!.GetValue<int>(), StringComparer.Ordinal),
                    entry["max_attempts"]?.GetValue<int>() ?? 1);
                JobState state = entry["paused"]?.GetValue<bool>() == true ? JobState.Paused : JobState.Queued;
                restored.Add(Add(request, entry["id"]!.GetValue<string>(), (JsonObject)(entry["checkpoint"]?.DeepClone() ?? new JsonObject()), entry["progress"]?.GetValue<double>() ?? 0, state));
            }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException or ArgumentException)
            {
                throw new ArgumentException($"a saved job could not be read: {entry.ToJsonString()}", nameof(saved), ex);
            }
        }

        return restored;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource[] running;
        lock (_gate)
        {
            _stopping = true;
            running = [.. _running.Keys.Select(j => j.Cancel).OfType<CancellationTokenSource>()];
        }

        foreach (CancellationTokenSource cancel in running)
        {
            try
            {
                await cancel.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The job finished in between.
            }
        }

        await WhenIdleAsync().ConfigureAwait(false);
    }

    internal void Report(Job job, double fraction)
    {
        lock (_gate)
        {
            job.Progress = Math.Max(job.Progress, Math.Clamp(fraction, 0, 1));
        }

        Changed?.Invoke(this, job);
    }

    internal bool MustStop(Job job)
    {
        lock (_gate)
        {
            return _stopping || job.PauseRequested || (_throttled && job.Request.Priority == JobPriority.Background);
        }
    }

    private Job Add(JobRequest request, string id, JsonObject checkpoint, double progress, JobState state = JobState.Queued)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_kinds(request.Kind) is null)
        {
            throw new ArgumentException($"no work is registered for jobs of kind '{request.Kind}'", nameof(request));
        }

        foreach ((string resource, int count) in request.Needs)
        {
            if (count > _capacity.GetValueOrDefault(resource))
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                    $"{request.Title} needs {count} of {resource}, and there are {_capacity.GetValueOrDefault(resource)}"), nameof(request));
            }
        }

        Job job;
        lock (_gate)
        {
            job = new Job(id, request, _order++) { State = state, CheckpointNode = checkpoint, Progress = progress };
            _jobs.Add(job);
        }

        Changed?.Invoke(this, job);
        Pump();
        return job;
    }

    private void Requeue(Job job, JobState from, bool resetAttempts)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            if (job.State != from)
            {
                return;
            }

            job.State = JobState.Queued;
            job.Error = null;
            if (resetAttempts)
            {
                job.Attempts = 0;
            }
        }

        Changed?.Invoke(this, job);
        Pump();
    }

    /// <summary>Starts every queued job that may start now, highest priority and oldest first.</summary>
    private void Pump()
    {
        List<Job> started = [];
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            foreach (Job job in _jobs.Where(j => j.State == JobState.Queued).OrderByDescending(j => j.Request.Priority).ThenBy(j => j.Order))
            {
                if ((_throttled && job.Request.Priority == JobPriority.Background) || !Fits(job))
                {
                    continue;
                }

                foreach ((string resource, int count) in job.Request.Needs)
                {
                    _inUse[resource] = _inUse.GetValueOrDefault(resource) + count;
                }

                job.State = JobState.Running;
                job.PauseRequested = false;
                job.Attempts++;
                job.Cancel = new CancellationTokenSource();
                job.Started();
                started.Add(job);
                _running[job] = Task.Run(() => RunAsync(job));
            }
        }

        foreach (Job job in started)
        {
            Changed?.Invoke(this, job);
        }
    }

    private bool Fits(Job job) =>
        job.Request.Needs.All(n => _inUse.GetValueOrDefault(n.Key) + n.Value <= _capacity.GetValueOrDefault(n.Key));

    private async Task RunAsync(Job job)
    {
        JobWork work = _kinds(job.Request.Kind)!;
        JobState next;
        string? error = null;
        try
        {
            await work(new JobContext(job, this, job.Cancel!.Token)).ConfigureAwait(false);
            next = JobState.Completed;
        }
        catch (JobStoppedException)
        {
            next = job.PauseRequested ? JobState.Paused : JobState.Queued;
        }
        catch (OperationCanceledException) when (job.Cancel!.IsCancellationRequested)
        {
            next = _stopping ? JobState.Queued : JobState.Cancelled;
        }
#pragma warning disable CA1031 // A job's failure of any kind is recorded on the job, never thrown into the scheduler.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            error = ex.Message;
            next = job.Attempts < job.Request.MaxAttempts ? JobState.Queued : JobState.Failed;
        }

        lock (_gate)
        {
            foreach ((string resource, int count) in job.Request.Needs)
            {
                _inUse[resource] -= count;
            }

            job.State = next;
            job.Error = error;
            job.PauseRequested = false;
            if (next == JobState.Completed)
            {
                job.Progress = 1;
            }

            job.Cancel!.Dispose();
            job.Cancel = null;
            _running.Remove(job);
        }

        Changed?.Invoke(this, job);
        Pump();
    }
}
