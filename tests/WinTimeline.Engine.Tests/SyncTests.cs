using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT79: linked picture and sound select together, show how far apart they slid, and come back by moving into sync.</summary>
public sealed class SyncTests
{
    private static ModelTimeline Cut() =>
        Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    private static void Run(History history, IEnumerable<IEditCommand> commands)
    {
        foreach (IEditCommand command in commands)
        {
            history.Execute(command);
        }
    }

    [Fact]
    public void LinkedClipsArePartnersAndAGroupIsWhateverLinkedSelectionSays()
    {
        ModelTimeline timeline = Cut();
        History history = new();
        Track v1 = timeline.VideoTracks.First(), a1 = timeline.AudioTracks.First();
        Run(history, ItemLinks.Link([v1.Children[0], a1.Children[0]]));
        Run(history, ItemLinks.Group([v1.Children[2], v1.Children[3]]));
        TimelineLayout layout = TimelineLayout.Build(timeline);

        Assert.Equal(["a1"], Sync.Partners(layout, layout.Find("V1-0")!, linked: true).Select(i => i.Name));
        Assert.Empty(Sync.Partners(layout, layout.Find("V1-0")!, linked: false));
        Assert.Equal(["c4"], Sync.Partners(layout, layout.Find("V1-2")!, linked: false).Select(i => i.Name));
        Assert.Throws<EditRefusedException>(() => ItemLinks.Link([v1.Children[0]]));
    }

    [Fact]
    public void ALinkedClipMovedAloneShowsItsOffsetInFramesAndNoneOnceBack()
    {
        ModelTimeline timeline = Cut();
        History history = new();
        Track v1 = timeline.VideoTracks.First(), a1 = timeline.AudioTracks.First();
        Run(history, ItemLinks.Link([v1.Children[0], a1.Children[0]]));
        Assert.Null(Sync.Offset(TimelineLayout.Build(timeline), TimelineLayout.Build(timeline).Find("V1-0")!, 24));

        // c1 moved half a second later on its own: twelve frames late against a1.
        Composable c1 = TrackEdits.Lift(history, v1, v1.Children[0]);
        TrackEdits.Overwrite(history, v1, 0.5, [c1]);
        TimelineLayout moved = TimelineLayout.Build(timeline);
        LaneItem late = moved.Lanes[0].Items.First(i => i.Name == "c1");

        Assert.Equal(12, Sync.Offset(moved, late, 24));
        Assert.Equal(-12, Sync.Offset(moved, moved.Find("A1-0")!, 24));

        Composable back = TrackEdits.Lift(history, v1, late.Item);
        TrackEdits.Overwrite(history, v1, 0, [back]);
        TimelineLayout fixedUp = TimelineLayout.Build(timeline);
        Assert.Null(Sync.Offset(fixedUp, fixedUp.Lanes[0].Items.First(i => i.Name == "c1"), 24));
    }
}
