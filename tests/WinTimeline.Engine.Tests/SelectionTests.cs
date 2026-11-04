using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>
/// WT52: clicks, Ctrl and Shift, the marquee, track select and select all, on cut_v1: V1 holds
/// c1 to c7 cut at 0, 2, 4, 5, 6.5, 7.5 and 8.5 seconds, A1 one clip from 0 to 5.
/// </summary>
public sealed class SelectionTests
{
    private static TimelineLayout Cut() =>
        TimelineLayout.Build(Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio"))));

    private static string[] Sorted(Selection selection) => [.. selection.Ids.Order(StringComparer.Ordinal)];

    [Fact]
    public void AnItemIsFoundByItsIdAndUnderAPoint()
    {
        TimelineLayout layout = Cut();

        Assert.Equal("c2", layout.Find("V1-1")?.Name);
        Assert.Equal("V1-1", layout.IdOf(layout.At(2.0, 0)!));
        Assert.Equal("a1", layout.At(4.99, 1)?.Name);
        Assert.Null(layout.At(6, 1));
        Assert.Null(layout.Find("V9-0"));
    }

    [Fact]
    public void ClickReplacesCtrlTogglesAndShiftTakesTheRangeAcrossLanes()
    {
        TimelineLayout layout = Cut();
        Selection selection = new();
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Click(layout, layout.Find("V1-1")!, SelectGesture.Replace);
        Assert.Equal(["V1-1"], Sorted(selection));
        selection.Click(layout, layout.Find("V1-3")!, SelectGesture.Toggle);
        selection.Click(layout, layout.Find("V1-1")!, SelectGesture.Toggle);
        Assert.Equal(["V1-3"], Sorted(selection));

        // The anchor is the last item clicked, V1-1 at two seconds; A1's clip starts at none.
        selection.Click(layout, layout.Find("A1-0")!, SelectGesture.Extend);
        Assert.Equal(["A1-0", "V1-0", "V1-1", "V1-3"], Sorted(selection));
        selection.Click(layout, layout.Find("V1-5")!, SelectGesture.Replace);
        Assert.Equal(["V1-5"], Sorted(selection));
        Assert.Equal(5, changes);
    }

    [Fact]
    public void AMarqueeTakesWhatItTouchesAndAddsWithAModifier()
    {
        TimelineLayout layout = Cut();
        Selection selection = new();

        selection.Marquee(layout, 4.5, 1.0, 0, 0, add: false);
        Assert.Equal(["V1-0", "V1-1", "V1-2"], Sorted(selection));
        selection.Marquee(layout, 7, 8, 1, 0, add: true);
        Assert.Equal(["V1-0", "V1-1", "V1-2", "V1-4", "V1-5"], Sorted(selection));
        selection.Marquee(layout, 4.5, 4.6, 1, 1, add: false);
        Assert.Equal(["A1-0"], Sorted(selection));
    }

    [Fact]
    public void TrackSelectTakesEveryLaneFromThePlayheadOnOrUpToItAndAllTakesEverything()
    {
        TimelineLayout layout = Cut();
        Selection selection = new();

        selection.TrackSelect(layout, 5.0, forward: true);
        Assert.Equal(["V1-3", "V1-4", "V1-5"], Sorted(selection));
        selection.TrackSelect(layout, 5.0, forward: false);
        Assert.Equal(["A1-0", "V1-0", "V1-1", "V1-2"], Sorted(selection));
        selection.All(layout);
        Assert.Equal(7, selection.Ids.Count);
        selection.Clear();
        Assert.Empty(selection.Ids);
    }
}
