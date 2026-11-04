using System.IO;
using System.Text;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT15: every sample EDL of OpenTimelineIO's own CMX 3600 adapter reads into the timeline that
/// adapter reads, written byte for byte as the reference writes it — or is refused where the
/// reference refuses it (tests/fixtures/edl, made by make_expected.py).
/// </summary>
[Collection(AdapterRegistry.Name)]
public sealed class EdlTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "edl");

    public static TheoryData<string, bool> Samples
    {
        get
        {
            TheoryData<string, bool> rows = [];
            foreach (string edl in Directory.GetFiles(Path.Combine(Fixtures, "samples"), "*.edl").Order(StringComparer.Ordinal))
            {
                rows.Add(Path.GetFileNameWithoutExtension(edl), false);
                rows.Add(Path.GetFileNameWithoutExtension(edl), true);
            }

            return rows;
        }
    }

    [Fact]
    public void EverySampleHasTheReferencesAnswer()
    {
        Assert.Equal(19 * 2, Samples.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySampleReadsAsTheReferenceReadsIt(string name, bool ignoreTimecodeMismatch)
    {
        string text = File.ReadAllText(Path.Combine(Fixtures, "samples", name + ".edl"), Encoding.UTF8);
        double rate = name == "25fps" ? 25 : 24;
        string stem = Path.Combine(Fixtures, "expected", name + (ignoreTimecodeMismatch ? ".ignore" : string.Empty));

        if (File.Exists(stem + ".error.txt"))
        {
            Assert.Throws<EdlParseException>(() => CmxEdlReader.Read(text, rate, ignoreTimecodeMismatch));
            return;
        }

        Timeline timeline = CmxEdlReader.Read(text, rate, ignoreTimecodeMismatch);

        Assert.Equal(File.ReadAllText(stem + ".otio", Encoding.UTF8), OtioWriter.Write(timeline));
    }

    [Fact]
    public void AMismatchIsRefusedUnlessItIsToBeIgnoredAndThenTheClipFollowsOn()
    {
        const string Edl = """
            TITLE: mismatch
            001  AX       V     C        00:00:00:00 00:00:01:00 01:00:00:00 01:00:01:00
            * FROM CLIP NAME:  a.mov
            002  AX       V     C        00:00:00:00 00:00:01:00 01:00:00:12 01:00:01:12
            * FROM CLIP NAME:  b.mov
            """;

        EdlParseException error = Assert.Throws<EdlParseException>(() => CmxEdlReader.Read(Edl));
        Assert.StartsWith("Overlapping record in value: 01:00:00:12", error.Message, StringComparison.Ordinal);

        Timeline timeline = CmxEdlReader.Read(Edl, ignoreTimecodeMismatch: true);
        Track track = Assert.IsType<Track>(Assert.Single(timeline.Tracks.Children));
        Assert.Equal(["a.mov", "b.mov"], track.Children.Select(c => c.Name));
        Assert.Equal(new Time.RationalTime(48, 24), timeline.Duration);
    }

    [Theory]
    [InlineData(@"D:\cuts\reel1.edl", "CMX 3600 EDL")]
    [InlineData(@"D:\cuts\REEL1.EDL", "CMX 3600 EDL")]
    [InlineData(@"D:\cuts\reel1.otio", "OpenTimelineIO")]
    [InlineData(@"D:\cuts\reel1.xml", "Final Cut Pro 7 XML")]
    [InlineData(@"D:\cuts\reel1.fcpxml", "Final Cut Pro XML")]
    [InlineData(@"D:\cuts\reel1.aaf", null)]
    public void TheRegistryFindsTheAdapterByExtension(string path, string? adapter)
    {
        Assert.Equal(adapter, TimelineAdapters.ForPath(path)?.Name);
    }

    [Fact]
    public void TheDialogFilterListsReadersAndWritersApart()
    {
        Assert.Equal("OpenTimelineIO (*.otio)|*.otio|CMX 3600 EDL (*.edl)|*.edl|Final Cut Pro 7 XML (*.xml)|*.xml|Final Cut Pro XML (*.fcpxml;*.fcpxmld)|*.fcpxml;*.fcpxmld", TimelineAdapters.DialogFilter(writing: false));
        Assert.Equal("OpenTimelineIO (*.otio)|*.otio", TimelineAdapters.DialogFilter(writing: true));
    }

    [Fact]
    public void AnEdlIsReadThroughItsAdapter()
    {
        ModelObject read = TimelineAdapters.ForPath("x.edl")!.Read(Path.Combine(Fixtures, "samples", "premiere_example.edl"), new AdapterOptions());

        Assert.Equal(2, Assert.IsType<Timeline>(read).FindClips().Count());
    }

    [Fact]
    public void AnUnknownEventIsRefused()
    {
        Assert.Throws<EdlParseException>(() => CmxEdlReader.Read("TITLE: x\nbogus line\n"));
        Assert.Throws<EdlParseException>(() => CmxEdlReader.Read("001 AX V X 00:00:00:00 00:00:01:00 00:00:00:00 00:00:01:00\n"));
    }
}
