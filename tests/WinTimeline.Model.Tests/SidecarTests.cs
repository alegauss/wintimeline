using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Project;

namespace WinTimeline.Model.Tests;

/// <summary>WT22: the editor's per-project state lives beside the .otio and comes back whole.</summary>
public sealed class SidecarTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("wt-sidecar-").FullName;

    private string Document => Path.Combine(_folder, "cut.otio");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void BinsProxiesLayoutAndAnalysisComeBackAsTheyWereSaved()
    {
        float[] values = [.. Enumerable.Range(0, 3 * 4).Select(i => i * 0.5f)];
        ProjectState state = new()
        {
            Bins = [new Bin("Selects", ["file:///media/A001.mov", "0/2"]), new Bin("Music", [])],
            Proxies = new Dictionary<string, string>(StringComparer.Ordinal) { ["file:///media/A001.mov"] = "proxies/A001.mp4" },
            Layout = new WorkspaceLayout(0.45, "reel3"),
            Analysis = new Dictionary<string, JsonNode>(StringComparer.Ordinal) { ["shots"] = new JsonArray(0, 48, 96) },
            Embeddings = new EmbeddingIndex("test@1", 4, ["a@0", "a@24", "b@0"], values),
        };

        Assert.True(ProjectSidecar.Save(Document, state));
        ProjectState back = ProjectSidecar.Load(Document);

        Assert.Equal(["Selects", "Music"], back.Bins.Select(b => b.Name));
        Assert.Equal(["file:///media/A001.mov", "0/2"], back.Bins[0].Items);
        Assert.Equal("proxies/A001.mp4", back.Proxies["file:///media/A001.mov"]);
        Assert.Equal(new WorkspaceLayout(0.45, "reel3"), back.Layout);
        Assert.Equal("[0,48,96]", back.Analysis["shots"].ToJsonString());
        EmbeddingIndex index = Assert.IsType<EmbeddingIndex>(back.Embeddings);
        Assert.Equal(("test@1", 4), (index.Model, index.Dimensions));
        Assert.Equal(["a@0", "a@24", "b@0"], index.Keys);
        Assert.Equal(values.AsSpan(4, 4).ToArray(), index.Vector(1).ToArray());
    }

    [Fact]
    public void NothingWorthKeepingMakesNoSidecar()
    {
        Assert.False(ProjectSidecar.Save(Document, ProjectState.Empty with { Layout = WorkspaceLayout.Default }));

        Assert.False(Directory.Exists(ProjectSidecar.FolderFor(Document)));
        Assert.Same(ProjectState.Empty, ProjectSidecar.Load(Document));
    }

    [Fact]
    public void AnalysisTheStateNoLongerHoldsIsRemoved()
    {
        ProjectSidecar.Save(Document, new ProjectState
        {
            Analysis = new Dictionary<string, JsonNode>(StringComparer.Ordinal) { ["shots"] = new JsonArray(), ["transcript"] = new JsonObject() },
            Embeddings = new EmbeddingIndex("m", 1, ["k"], new float[] { 1 }),
        });

        ProjectSidecar.Save(Document, ProjectState.Empty);

        string analysis = Path.Combine(ProjectSidecar.FolderFor(Document), "analysis");
        Assert.Empty(Directory.EnumerateFiles(analysis));
        Assert.True(ProjectSidecar.Load(Document).IsEmpty);
    }

    [Fact]
    public void VectorsThatDisagreeWithTheirIndexAreRefusedNotMisread()
    {
        ProjectSidecar.Save(Document, new ProjectState { Embeddings = new EmbeddingIndex("m", 2, ["a", "b"], new float[] { 1, 2, 3, 4 }) });
        string values = Path.Combine(ProjectSidecar.FolderFor(Document), "analysis", "embeddings.f32");
        File.WriteAllBytes(values, new byte[12]);

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ProjectSidecar.Load(Document));
        Assert.Contains("not 2 vectors of 2 floats", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASidecarFromANewerWinTimelineIsRefused()
    {
        Directory.CreateDirectory(ProjectSidecar.FolderFor(Document));
        File.WriteAllText(Path.Combine(ProjectSidecar.FolderFor(Document), "project.json"), """{ "format": 99 }""");

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ProjectSidecar.Load(Document));
        Assert.Contains("format 99", refused.Message, StringComparison.Ordinal);
    }
}
