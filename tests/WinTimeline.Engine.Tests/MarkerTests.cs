using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>
/// WT54: markers from files the reference adapters wrote land where the reference's own ranges put
/// them: the timeline's on the ruler, a clip's at the clip's start plus its offset into what it plays.
/// </summary>
public sealed class MarkerTests
{
    private static TimelineLayout Layout(params string[] path) =>
        TimelineLayout.Build(Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine([Repository.Root, "tests", "fixtures", .. path]))));

    [Fact]
    public void PremieresTimelineMarkersSitOnTheRulerAndItsClipMarkerOnTheClip()
    {
        IReadOnlyList<PlacedMarker> markers = TimelineMarkers.Of(Layout("otio", "corpus", "premiere_example.otio"));

        // Three on the timeline's stack (no lane) and one on sc01_sh030_anim.mov, which starts at
        // 10.733 s and plays its media from 0, so its marker 2.433 s in is at 13.167 s. Its track is
        // V2, the third lane down under V4 and V3.
        Assert.Equal([3.7667, 9.9333, 13.1667, 16.4], markers.Select(m => Math.Round(m.Start, 4)));
        Assert.Equal([null, null, 2, null], markers.Select(m => m.Lane));
        Assert.All(markers, m => Assert.Equal("RED", m.Marker.Color));
        Assert.Equal("My MArker 1", markers[0].Marker.Name);
    }

    [Fact]
    public void AClipMarkerIsPlacedFromTheClipsTrimmedStartAndOneOutsideWhatItPlaysIsNotShown()
    {
        IReadOnlyList<PlacedMarker> markers = TimelineMarkers.Of(Layout("fcpx", "expected", "fcpx_project.otio"));

        // IMG_0740 starts at 59.5 s and plays its media from 0.5 s, so Marker 4 at 5.567 s in the
        // media is at 64.567 s. IMG_0687 plays from 3.567 s, so Marker 3 at 1.933 s is before it.
        PlacedMarker four = Assert.Single(markers, m => m.Marker.Name == "Marker 4");
        Assert.Equal(64.5667, Math.Round(four.Start, 4));
        Assert.Equal("PURPLE", four.Marker.Color);
        Assert.DoesNotContain(markers, m => m.Marker.Name == "Marker 3");
        Assert.Equal(1 / 30.0, Assert.Single(markers, m => m.Marker.Name == "Marker 5").Duration, 6);
    }

    [Fact]
    public void ShiftUpAndDownFindTheMarkerBeforeAndAfterThePlayheadAndTheTimelinesOwnSnap()
    {
        TimelineLayout layout = Layout("otio", "corpus", "premiere_example.otio");
        IReadOnlyList<PlacedMarker> markers = TimelineMarkers.Of(layout);

        Assert.Equal(9.9333, Math.Round(TimelineMarkers.Beside(markers, 5, 30, forward: true)!.Start, 4));
        Assert.Equal(3.7667, Math.Round(TimelineMarkers.Beside(markers, 5, 30, forward: false)!.Start, 4));
        Assert.Null(TimelineMarkers.Beside(markers, 3.7667, 30, forward: false));
        Assert.Equal(SnapKind.Marker, SnapTargets.Of(layout).Snap(16.42, 100, 30).Target?.Kind);
    }

    [Fact]
    public void ColoursAreTheReferencesNamesAndAnUnknownOneIsGreen()
    {
        Assert.Equal((0.5f, 0f, 0.5f), MarkerColors.Rgb("PURPLE"));
        Assert.Equal((1f, 0f, 0f), MarkerColors.Rgb("red"));
        Assert.Equal((0f, 1f, 0f), MarkerColors.Rgb("TEAL"));
        Assert.Equal(11, MarkerColors.Names.Count);
    }
}
