using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Engine.Analysis;
using WinTimeline.Engine.Jobs;
using WinTimeline.Model.Project;

namespace WinTimeline.Engine.Tests;

/// <summary>WT104: analysis runs once per file and model, and comes back from the sidecar without running again.</summary>
public sealed class AnalysisTests
{
    private static readonly string[] Media =
    [
        Path.Combine(Repository.Root, "tests", "fixtures", "media", "bars-3s-25p.mp4"),
        Path.Combine(Repository.Root, "tests", "fixtures", "media", "tone-2s.wav"),
    ];

    private readonly string _folder = Directory.CreateTempSubdirectory("wt-analysis-").FullName;

    [Fact]
    public async Task AFolderAnalysedOnceComesBackOnReopenWithoutRunningAModel()
    {
        Counting shots = new("shots", "transnetv2/fp16", "1");
        string document = Path.Combine(_folder, "cut.otio");

        AnalysisStore first = new();
        Assert.Equal(2, await Analyse(first, shots));
        ProjectSidecar.Save(document, first.Into(ProjectState.Empty));

        AnalysisStore reopened = AnalysisStore.From(ProjectSidecar.Load(document));
        Assert.Equal(0, await Analyse(reopened, shots));
        StoredAnalysis stored = Assert.IsType<StoredAnalysis>(reopened.Get(MediaSignature.Of(Media[0]), "shots", "transnetv2/fp16", "1"));
        Assert.False(stored.Stale);
        Assert.Equal("bars-3s-25p.mp4", stored.Result.Data["of"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUpgradedModelMarksOlderResultsStaleAndRunsAgain()
    {
        AnalysisStore store = new();
        await Analyse(store, new Counting("tags", "florence2/base", "1"));

        Assert.True(store.Get(MediaSignature.Of(Media[1]), "tags", "florence2/base", "2")!.Stale);
        Assert.Equal(2, await Analyse(store, new Counting("tags", "florence2/base", "2")));
        Assert.False(store.Get(MediaSignature.Of(Media[1]), "tags", "florence2/base", "2")!.Stale);
    }

    [Fact]
    public async Task AFileAnalysedForOneProjectIsNotAnalysedAgainForAnother()
    {
        AnalysisCache cache = new(Path.Combine(_folder, "cache"));
        Counting transcript = new("transcript", "whisper-base/base", "1");

        Assert.Equal(2, await Analyse(new AnalysisStore(cache), transcript));
        Assert.Equal(0, await Analyse(new AnalysisStore(cache), transcript));
    }

    [Fact]
    public void AMovedOrRenamedFileKeepsItsSignature()
    {
        string renamed = Path.Combine(_folder, "renamed.mp4");
        File.Copy(Media[0], renamed);

        Assert.Equal(MediaSignature.Of(Media[0]), MediaSignature.Of(renamed));
        Assert.NotEqual(MediaSignature.Of(Media[0]), MediaSignature.Of(Media[1]));
        Assert.StartsWith("s1-118551-", MediaSignature.Of(Media[0]), StringComparison.Ordinal);
    }

    /// <summary>Runs the Analyse job through a real scheduler and says how many times a model ran.</summary>
    private static async Task<int> Analyse(AnalysisStore store, Counting analyser)
    {
        int before = analyser.Runs;
        Dictionary<string, IAnalyser> analysers = new(StringComparer.Ordinal) { [analyser.Kind] = analyser };
        JobWork work = AnalyseJob.Work(store, analysers);
        await using JobScheduler scheduler = new(new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-inference"] = 1 }, kind => kind == AnalyseJob.Kind ? work : null);
        Job job = scheduler.Enqueue(AnalyseJob.Request(Media, [analyser.Kind], visible: true));
        DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (job.State is not (JobState.Completed or JobState.Failed))
        {
            Assert.True(DateTime.UtcNow < until, "the analysis did not finish");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(job.State == JobState.Completed, job.Error);
        Assert.Equal(1, job.Progress);
        return analyser.Runs - before;
    }

    private sealed class Counting(string kind, string model, string version) : IAnalyser
    {
        private int _runs;

        public string Kind { get; } = kind;

        public string Model { get; } = model;

        public string Version { get; } = version;

        public int Runs => _runs;

        public Task<JsonNode> AnalyseAsync(string media, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _runs);
            return Task.FromResult<JsonNode>(new JsonObject { ["of"] = Path.GetFileName(media) });
        }
    }
}
