using System.IO;
using WinTimeline.Model;
using WinTimeline.Model.Serialization;

namespace WinTimeline.App.Tests;

/// <summary>WT13: what the status bar says about an opened document.</summary>
public sealed class DocumentTextTests
{
    private static ModelObject Corpus(string name) =>
        OtioFile.Open(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus", name));

    [Fact]
    public void ATimelineIsCountedInClipsAndTracks()
    {
        Assert.Equal("Opened simple_cut.otio: 4 clips on 1 track.", MainWindow.OpenedText("simple_cut.otio", Corpus("simple_cut.otio")));
        Assert.Equal("Opened multitrack.otio: 5 clips on 3 tracks.", MainWindow.OpenedText("multitrack.otio", Corpus("multitrack.otio")));
    }

    [Theory]
    [InlineData(@"D:\x\cut.otioz", @"D:\x\cut.otioz")]
    [InlineData(@"D:\x\cut.otiod", @"D:\x\cut.otiod")]
    [InlineData(@"D:\x\cut.otiod\content.otio", @"D:\x\cut.otiod")]
    [InlineData(@"D:\x\cut.otio", null)]
    [InlineData(@"D:\x\content.otio", null)]
    public void AnOtiodIsOpenedByPickingTheContentInsideIt(string picked, string? bundle)
    {
        // WT14: a file dialog cannot pick a folder, so the content.otio inside one stands for it.
        Assert.Equal(bundle, MainWindow.BundlePath(picked));
    }

    [Fact]
    public void SomethingElseSaysWhatItIs()
    {
        ModelObject collection = OtioReader.ReadString("""{ "OTIO_SCHEMA": "SerializableCollection.1", "children": [] }""");

        Assert.Equal("Opened reels.otio: a SerializableCollection, not a single timeline.", MainWindow.OpenedText("reels.otio", collection));
    }
}
