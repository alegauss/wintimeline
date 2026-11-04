using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.App.Notices;
using WinTimeline.Engine.Jobs;

namespace WinTimeline.App.Tests;

/// <summary>WT69: a failing job says so with a Retry that runs it again, and a finished one says so briefly.</summary>
public sealed class NoticeTests
{
    [Fact]
    public async Task AFailedJobsRetryRunsItAgainUntilItWorks()
    {
        int runs = 0;
        JobWork flaky = _ => Interlocked.Increment(ref runs) == 1 ? throw new IOException("the encoder is busy") : Task.CompletedTask;
        await using JobScheduler scheduler = new(new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-encoder"] = 1 }, _ => flaky);
        Job job = scheduler.Enqueue(new JobRequest("proxy", "Proxy for A001.mov", [], JobPriority.Background,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-encoder"] = 1 }));

        await Until(() => job.State == JobState.Failed);
        Notice failed = Assert.IsType<Notice>(JobNotices.For(job, scheduler));
        Assert.Equal((NoticeSeverity.Error, "Proxy for A001.mov failed", "the encoder is busy"), (failed.Severity, failed.Title, failed.Message));
        Assert.False(failed.Transient);

        NoticeAction retry = Assert.Single(failed.Actions);
        Assert.Equal("Retry", retry.Label);
        retry.Run();

        await Until(() => job.State == JobState.Completed);
        Assert.Equal(2, runs);
        Notice done = Assert.IsType<Notice>(JobNotices.For(job, scheduler));
        Assert.Equal((NoticeSeverity.Success, "Proxy for A001.mov finished"), (done.Severity, done.Title));
        Assert.True(done.Transient);
    }

    [Fact]
    public async Task AJobWaitingOrRunningOrCancelledByTheUserSaysNothing()
    {
        TaskCompletionSource hold = new();
        await using JobScheduler scheduler = new(new Dictionary<string, int>(StringComparer.Ordinal), _ => _ => hold.Task);
        Job job = scheduler.Enqueue(new JobRequest("analyse", "Shots in A001.mov", new JsonObject(), JobPriority.Visible, new Dictionary<string, int>(StringComparer.Ordinal)));

        Assert.Null(JobNotices.For(job, scheduler));
        scheduler.Cancel(job);
        hold.SetCanceled(TestContext.Current.CancellationToken);
        await Until(() => job.State == JobState.Cancelled);
        Assert.Null(JobNotices.For(job, scheduler));
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the job did not get there within 10 s");
            await Task.Delay(5);
        }
    }
}
