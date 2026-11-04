using WinTimeline.Engine.Jobs;

namespace WinTimeline.App.Notices;

/// <summary>How a notice reads: its colour and how long it stays.</summary>
public enum NoticeSeverity
{
    /// <summary>Something finished; goes by itself.</summary>
    Success,

    /// <summary>Worth knowing; goes by itself.</summary>
    Informational,

    /// <summary>Something may be wrong; stays until dismissed.</summary>
    Warning,

    /// <summary>Something failed; stays until dismissed.</summary>
    Error,
}

/// <summary>A button on a notice, e.g. Retry or Show file.</summary>
/// <param name="Label">What it says.</param>
/// <param name="Run">What it does; the notice closes after.</param>
public sealed record NoticeAction(string Label, Action Run);

/// <summary>
/// WT69: one non-modal notice, shown in the stack under the menu instead of a dialog that stops
/// the editing, or a status bar line that the next one overwrites.
/// </summary>
/// <param name="Severity">How it reads.</param>
/// <param name="Title">The headline, e.g. "Proxy for A001.mov failed".</param>
/// <param name="Message">The detail, e.g. why.</param>
/// <param name="Actions">Its buttons, besides Dismiss.</param>
public sealed record Notice(NoticeSeverity Severity, string Title, string Message, IReadOnlyList<NoticeAction> Actions)
{
    /// <summary>How long a notice that goes by itself stays.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    /// <summary>Whether it goes by itself after <see cref="Lifetime"/>.</summary>
    public bool Transient => Severity is NoticeSeverity.Success or NoticeSeverity.Informational;
}

/// <summary>
/// WT69: what a background job's change says, if anything: a failure stays with a Retry that
/// queues the job again from its checkpoint, a completion goes by itself, and the rest (queued,
/// running, paused, cancelled by the user) says nothing, as the Jobs panel shows it.
/// </summary>
public static class JobNotices
{
    /// <summary>The notice for a job as it now stands, or null.</summary>
    /// <param name="job">The job.</param>
    /// <param name="scheduler">Its scheduler, which Retry goes through.</param>
    /// <returns>The notice, or null.</returns>
    public static Notice? For(Job job, JobScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(scheduler);
        return job.State switch
        {
            JobState.Failed => new Notice(NoticeSeverity.Error, $"{job.Request.Title} failed", job.Error ?? "It stopped with an error.",
                [new NoticeAction("Retry", () => scheduler.Retry(job))]),
            JobState.Completed => new Notice(NoticeSeverity.Success, $"{job.Request.Title} finished", string.Empty, []),
            _ => null,
        };
    }
}
