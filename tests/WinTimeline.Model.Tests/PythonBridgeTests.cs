using System.IO;
using WinTimeline.Model.Adapters;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT157: formats only OpenTimelineIO's Python adapters read open through a child process, where
/// such a Python exists. The reference venv (.cache\venv, with otio-aaf-adapter) stands in for a
/// desk that has one; CI has none, and says so.
/// </summary>
[Collection(AdapterRegistry.Name)]
public sealed class PythonBridgeTests
{
    private static readonly string Venv = Path.Combine(Repository.Root, ".cache", "venv", "Scripts", "python.exe");
    private static readonly string Aaf = Path.Combine(Repository.Root, "tests", "fixtures", "aaf", "2997fps-DFTC.aaf");

    [Fact]
    public void APythonWithTheAafPluginBridgesAafAndNothingTheAppReadsItself()
    {
        PythonBridge bridge = Bridge();

        Assert.Contains(bridge.Adapters, a => a.Name == "AAF" && a.Suffixes.Contains(".aaf"));
        ITimelineAdapter aaf = Assert.Single(bridge.AdaptersBeside(TimelineAdapters.Native));
        Assert.Equal(("AAF (OpenTimelineIO)", ".aaf"), (aaf.Name, Assert.Single(aaf.Suffixes)));
    }

    [Fact]
    public void AnAafSampleOpensThroughTheBridge()
    {
        PythonBridge bridge = Bridge();

        ModelObject read = bridge.AdaptersBeside(TimelineAdapters.Native)[0].Read(Aaf, new AdapterOptions());

        Timeline timeline = Assert.IsType<Timeline>(read);
        Assert.NotEmpty(timeline.FindClips());
    }

    [Fact]
    public void TheMenuOffersAafOnlyWhileABridgeIsInUse()
    {
        PythonBridge bridge = Bridge();
        try
        {
            TimelineAdapters.UseBridge(bridge);
            Assert.Equal("AAF (OpenTimelineIO)", TimelineAdapters.ForPath(@"D:\cuts\reel1.aaf")?.Name);
            Assert.Contains("*.aaf", TimelineAdapters.DialogFilter(writing: false), StringComparison.Ordinal);
        }
        finally
        {
            TimelineAdapters.UseBridge(null);
        }

        Assert.Null(TimelineAdapters.ForPath(@"D:\cuts\reel1.aaf"));
        Assert.DoesNotContain("*.aaf", TimelineAdapters.DialogFilter(writing: false), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTheAdapterCannotReadIsRefusedNamingTheAdapterAndPythonsWords()
    {
        PythonBridge bridge = Bridge();
        string fake = Path.Combine(Directory.CreateTempSubdirectory("wt-bridge-").FullName, "notes.aaf");
        File.WriteAllText(fake, "this is not an AAF file");

        PythonBridgeException refused = Assert.Throws<PythonBridgeException>(() => bridge.Read(fake, "AAF"));

        Assert.StartsWith("OpenTimelineIO's AAF adapter could not read notes.aaf: ", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APythonThatIsNotThereOrHasNoOpenTimelineIOIsNoBridge()
    {
        Assert.Null(PythonBridge.Probe(@"C:\no\such\python.exe"));
        Assert.Null(PythonBridge.Probe("cmd.exe"));
    }

    private static PythonBridge Bridge()
    {
        Assert.SkipUnless(File.Exists(Venv), "the reference venv (.cache\\venv with otio-aaf-adapter) is not on this desk");
        PythonBridge? bridge = PythonBridge.Probe(Venv);
        Assert.SkipWhen(bridge is null || bridge.Adapters.All(a => a.Name != "AAF"), "the reference venv has no otio-aaf-adapter: pip install otio-aaf-adapter");
        return bridge!;
    }
}

/// <summary>The tests that read or change <see cref="TimelineAdapters"/>' list run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class AdapterRegistry
{
    /// <summary>The collection's name.</summary>
    public const string Name = "adapter registry";
}
