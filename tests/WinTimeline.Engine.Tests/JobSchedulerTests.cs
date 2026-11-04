using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Engine.Jobs;
using WinTimeline.Model.Project;

namespace WinTimeline.Engine.Tests;

/// <summary>WT40: one queue for long work, which runs, pauses, survives a restart and finishes with true progress.</summary>
public sealed class JobSchedulerTests
{
    private static readonly Dictionary<string, int> FourCores = new(StringComparer.Ordinal) { ["cpu"] = 4, ["gpu-encoder"] = 1 };

    // Each step of each job, counted where it ran, so a step done twice or never shows.
    private readonly ConcurrentDictionary<string, int[]> _steps = new(StringComparer.Ordinal);

    [Fact]
    public async Task TwoHundredJobsRunPauseSurviveARestartAndEachStepRunsExactlyOnce()
    {
        JsonArray saved;
        await using (JobScheduler first = new(FourCores, Kinds))
        {
            Job[] jobs = [.. Enumerable.Range(0, 200).Select(i => first.Enqueue(Steps($"job{i:000}", 20, i % 3 == 0 ? JobPriority.Visible : JobPriority.Background)))];
            first.Pause(jobs[150]);
            first.Pause(jobs[199]);
            await WaitUntil(() => jobs.Count(j => j.State == JobState.Completed) >= 50);

            saved = await first.ShutdownAsync();
        }

        // The restart: what was saved goes through the sidecar, as the app keeps it.
        string document = Path.Combine(Directory.CreateTempSubdirectory("wt-jobs-").FullName, "cut.otio");
        ProjectSidecar.Save(document, new ProjectState { Jobs = saved });
        JsonArray reloaded = ProjectSidecar.Load(document).Jobs!;
        Assert.InRange(reloaded.Count, 2, 150);

        await using JobScheduler second = new(FourCores, Kinds);
        IReadOnlyList<Job> restored = second.Restore(reloaded);
        Job[] paused = [.. restored.Where(j => j.State == JobState.Paused)];
        Assert.Contains(paused, j => j.Request.Title == "job150");
        Assert.Contains(paused, j => j.Request.Title == "job199");
        foreach (Job job in paused)
        {
            second.Resume(job);
        }

        await WaitUntil(() => second.Jobs.All(j => j.State == JobState.Completed));
        Assert.Equal(1, second.OverallProgress);
        Assert.Equal(200, _steps.Count);
        Assert.All(_steps, kv => Assert.All(kv.Value, ran => Assert.Equal(1, ran)));
    }

    [Fact]
    public async Task HigherPriorityGoesFirstAndAResourceIsNeverOverbooked()
    {
        List<string> order = [];
        int encoding = 0;
        int most = 0;
        JobWork encode = async ctx =>
        {
            int now = Interlocked.Increment(ref encoding);
            InterlockedMax(ref most, now);
            lock (order)
            {
                order.Add(ctx.Arguments["name"]!.GetValue<string>());
            }

            await Task.Delay(5, ctx.Cancellation);
            Interlocked.Decrement(ref encoding);
        };
        await using JobScheduler scheduler = new(FourCores, kind => kind == "encode" ? encode : null);
        scheduler.Throttled = true;
        foreach (string name in new[] { "b1", "b2", "v1", "i1" })
        {
            JobPriority priority = name[0] switch { 'i' => JobPriority.Interactive, 'v' => JobPriority.Visible, _ => JobPriority.Background };
            scheduler.Enqueue(new JobRequest("encode", name, new JsonObject { ["name"] = name }, priority, new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-encoder"] = 1 }));
        }

        await scheduler.WhenIdleAsync();
        Assert.Equal(["i1", "v1"], order.Take(2).Order(StringComparer.Ordinal));

        scheduler.Throttled = false;
        await WaitUntil(() => scheduler.Jobs.All(j => j.State == JobState.Completed));
        Assert.Equal(["b1", "b2"], order.Skip(2));
        Assert.Equal(1, most);
    }

    [Fact]
    public async Task ACancelledJobStopsForGoodAndARetriedOneCarriesOnFromItsCheckpoint()
    {
        await using JobScheduler scheduler = new(FourCores, Kinds);
        Job job = scheduler.Enqueue(Steps("slow", 400, JobPriority.Interactive, delayMs: 2));
        await WaitUntil(() => job.Progress > 0.05);

        scheduler.Cancel(job);
        await WaitUntil(() => job.State == JobState.Cancelled);
        double stoppedAt = job.Progress;
        Assert.InRange(stoppedAt, 0.05, 0.99);

        scheduler.Retry(job);
        await WaitUntil(() => job.State == JobState.Completed);
        Assert.Equal(1, job.Progress);
        Assert.All(_steps["slow"], ran => Assert.InRange(ran, 1, 2));
    }

    [Fact]
    public async Task AFailingJobIsTriedAgainUpToItsLimitAndThenSaysWhy()
    {
        int tries = 0;
        JobWork flaky = _ =>
        {
            Interlocked.Increment(ref tries);
            throw new IOException("the disk is full");
        };
        await using JobScheduler scheduler = new(FourCores, _ => flaky);
        Job job = scheduler.Enqueue(new JobRequest("flaky", "flaky", [], JobPriority.Background, new Dictionary<string, int>(StringComparer.Ordinal), MaxAttempts: 3));

        await WaitUntil(() => job.State == JobState.Failed);
        Assert.Equal(3, tries);
        Assert.Equal("the disk is full", job.Error);
    }

    [Fact]
    public async Task AJobThatCannotFitOrHasNoWorkIsRefusedWhenQueued()
    {
        await using JobScheduler scheduler = new(FourCores, Kinds);

        ArgumentException tooBig = Assert.Throws<ArgumentException>(() => scheduler.Enqueue(new JobRequest("steps", "export", [], JobPriority.Interactive,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-encoder"] = 2 })));
        ArgumentException unknown = Assert.Throws<ArgumentException>(() => scheduler.Enqueue(new JobRequest("teleport", "x", [], JobPriority.Interactive, new Dictionary<string, int>(StringComparer.Ordinal))));

        Assert.StartsWith("export needs 2 of gpu-encoder, and there are 1", tooBig.Message, StringComparison.Ordinal);
        Assert.StartsWith("no work is registered for jobs of kind 'teleport'", unknown.Message, StringComparison.Ordinal);
    }

    private static JobRequest Steps(string name, int steps, JobPriority priority, int delayMs = 0) =>
        new("steps", name, new JsonObject { ["name"] = name, ["steps"] = steps, ["delay"] = delayMs }, priority,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["cpu"] = 1 });

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition was not met within 30 s");
            await Task.Delay(5);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    private JobWork? Kinds(string kind) => kind == "steps" ? RunSteps : null;

    /// <summary>A job of N steps, each counted, carrying on from the step its checkpoint names.</summary>
    private async Task RunSteps(JobContext context)
    {
        string name = context.Arguments["name"]!.GetValue<string>();
        int steps = context.Arguments["steps"]!.GetValue<int>();
        int delay = context.Arguments["delay"]?.GetValue<int>() ?? 0;
        int[] ran = _steps.GetOrAdd(name, _ => new int[steps]);
        for (int step = context.Checkpoint["done"]?.GetValue<int>() ?? 0; step < steps; step++)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, context.Cancellation);
            }
            else
            {
                await Task.Yield();
            }

            Interlocked.Increment(ref ran[step]);
            context.Report((step + 1) / (double)steps);
            context.Save(new JsonObject { ["done"] = step + 1 });
        }
    }
}
