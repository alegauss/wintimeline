using System.IO;
using System.Text;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT20: a file of several timelines opens as all of them, saves back as one collection, and grows versions.</summary>
public sealed class CollectionTests
{
    private static readonly string Reels = Path.Combine(Repository.Root, "tests", "fixtures", "collections", "reels.otio");

    [Fact]
    public void ThreeReelsAreThreeTimelinesAndSaveBackAsTheReferenceWroteThem()
    {
        ModelObject document = OtioReader.ReadFile(Reels);

        Assert.Equal(["reel1", "reel2", "reel3"], DocumentTimelines.Of(document).Select(t => t.Name));
        Assert.Equal([2, 3, 1], DocumentTimelines.Of(document).Select(t => t.FindClips().Count()));
        Assert.Equal(File.ReadAllText(Reels, Encoding.UTF8), OtioWriter.Write(document));
    }

    [Fact]
    public void AnEditToAReelInACollectionIsAnnouncedByThatReel()
    {
        SerializableCollection document = Assert.IsType<SerializableCollection>(OtioReader.ReadFile(Reels));
        Timeline reel2 = DocumentTimelines.Of(document)[1];
        List<ModelChange> heard = [];
        reel2.ModelChanged += (_, change) => heard.Add(change);

        new History().Execute(new Rename(reel2.FindClips().First(), "renamed"));

        Assert.Equal("name", Assert.Single(heard).What);
    }

    [Fact]
    public void ATimelineIsDuplicatedAsItsNextVersionBesideItAndTheCopyIsIndependent()
    {
        SerializableCollection document = Assert.IsType<SerializableCollection>(OtioReader.ReadFile(Reels));
        string before = OtioWriter.Write(document);
        Timeline reel2 = DocumentTimelines.Of(document)[1];
        History history = new();

        history.Execute(DuplicateTimeline.Command(reel2));

        Assert.Equal(["reel1", "reel2", "reel2 v002", "reel3"], DocumentTimelines.Of(document).Select(t => t.Name));
        Timeline copy = DocumentTimelines.Of(document)[2];
        history.Execute(new Rename(copy.FindClips().First(), "changed in the copy"));
        Assert.Equal("r2c1", reel2.FindClips().First().Name);

        history.Undo();
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(document));
    }

    [Theory]
    [InlineData("reel2", new string[0], "reel2 v002")]
    [InlineData("reel2 v002", new string[0], "reel2 v003")]
    [InlineData("reel2", new[] { "reel2 v002" }, "reel2 v003")]
    [InlineData("cut v9", new string[0], "cut v9 v002")]
    public void TheNextVersionNumberSkipsTakenNames(string name, string[] taken, string next)
    {
        Assert.Equal(next, DocumentTimelines.NextVersion(name, taken));
    }

    [Fact]
    public void ALoneTimelineIsOneTimelineAndCannotBeVersionedOutsideACollection()
    {
        ModelObject single = OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus", "simple_cut.otio"));

        Timeline only = Assert.Single(DocumentTimelines.Of(single));
        Assert.Throws<InvalidOperationException>(() => DuplicateTimeline.Command(only));
    }
}
