using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT53: a drag lands exactly on a cut, marker, playhead or in/out within eight pixels, and nowhere else.</summary>
public sealed class SnappingTests
{
    private static TimelineLayout Cut() =>
        TimelineLayout.Build(Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio"))));

    [Fact]
    public void ATimeWithinEightPixelsOfACutLandsExactlyOnItAndOneFurtherDoesNot()
    {
        SnapTargets targets = SnapTargets.Of(Cut());

        // At 100 px a second, eight pixels are 0.08 s.
        Snapped near = targets.Snap(2.07, 100, 24);
        Assert.Equal(2.0, near.Seconds);
        Assert.Equal(SnapKind.EditPoint, near.Target?.Kind);
        Assert.Equal(-2, near.Frames);

        Snapped far = targets.Snap(2.09, 100, 24);
        Assert.Null(far.Target);
        Assert.Equal(2.09, far.Seconds);

        // Zoomed out, the same time is near enough.
        Assert.Equal(2.0, targets.Snap(2.09, 50, 24).Seconds);
    }

    [Fact]
    public void TheNearestTargetWinsAndTheMovingOnesCountToo()
    {
        SnapTargets targets = SnapTargets.Of(Cut());

        Assert.Equal(4.0, targets.Snap(4.03, 100, 24).Seconds);
        Assert.Equal(5.0, targets.Snap(4.97, 100, 24).Seconds);

        Snapped playhead = targets.Snap(3.02, 100, 24, new SnapTarget(3.0, SnapKind.Playhead));
        Assert.Equal(SnapKind.Playhead, playhead.Target?.Kind);
        Assert.Equal(3.0, playhead.Seconds);
    }

    [Fact]
    public void AMarkerOnAClipIsATargetWhereItFallsOnTheTimeline()
    {
        // c1 plays its source from 0 at the timeline's start, so a marker half a second into it is at 0.5.
        string text = File.ReadAllText(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio"));
        int c1 = text.IndexOf("\"name\": \"c1\"", StringComparison.Ordinal);
        int markers = text.IndexOf("\"markers\": []", c1, StringComparison.Ordinal);
        const string Marker = "\"markers\": [{\"OTIO_SCHEMA\": \"Marker.2\", \"name\": \"beat\", \"color\": \"RED\", \"comment\": \"\", \"metadata\": {}, "
            + "\"marked_range\": {\"OTIO_SCHEMA\": \"TimeRange.1\", \"start_time\": {\"OTIO_SCHEMA\": \"RationalTime.1\", \"rate\": 24.0, \"value\": 12.0}, "
            + "\"duration\": {\"OTIO_SCHEMA\": \"RationalTime.1\", \"rate\": 24.0, \"value\": 0.0}}}]";
        string marked = string.Concat(text.AsSpan(0, markers), Marker, text.AsSpan(markers + "\"markers\": []".Length));
        SnapTargets targets = SnapTargets.Of(TimelineLayout.Build(Assert.IsType<ModelTimeline>(OtioReader.ReadString(marked))));

        Snapped snapped = targets.Snap(0.53, 100, 24);

        Assert.Equal(0.5, snapped.Seconds);
        Assert.Equal(SnapKind.Marker, snapped.Target?.Kind);
    }

    [Fact]
    public void NoTwoAdjacentFramesAreLeftBetweenClipsSnappedEndToEnd()
    {
        // Every cut found by snapping a pointer a few pixels off it is the cut itself, so a clip
        // dropped there leaves no one-frame gap or overlap.
        TimelineLayout layout = Cut();
        SnapTargets targets = SnapTargets.Of(layout);
        foreach (double cut in EditPoints.Of(layout))
        {
            Assert.Equal(cut, targets.Snap(cut + 0.03, 120, 24).Seconds);
            Assert.Equal(cut, targets.Snap(cut - 0.03, 120, 24).Seconds);
        }
    }
}
