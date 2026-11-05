using WinTimeline.Engine.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT57: a heat lane counts what falls in each bin, scaled to its busiest, and finds the hot spot under a click.</summary>
public sealed class HeatTests
{
    [Fact]
    public void BinsCountWhatFallsInThemScaledToTheBusiest()
    {
        HeatLane lane = HeatLane.Of(HeatKind.Cuts, [0.5, 1.2, 1.8, 7.5, 9.99, 12], 10, 5);

        // Bins of two seconds: 0.5, 1.2 and 1.8 in the first, 7.5 in the fourth, 9.99 in the last; 12 is past the end.
        Assert.Equal([1f, 0f, 0f, 1f / 3, 1f / 3], lane.Bins);
        Assert.Equal(5, lane.Count);
    }

    [Fact]
    public void AClickNearAHotSpotFindsItAndOneFarFromAnyDoesNot()
    {
        HeatLane lane = HeatLane.Of(HeatKind.Markers, [3.7667, 9.9333, 16.4], 42.4, 100);

        Assert.Equal(3.7667, lane.Nearest(3.9, 0.3));
        Assert.Equal(16.4, lane.Nearest(16.2, 0.3));
        Assert.Null(lane.Nearest(6, 0.3));
    }

    [Fact]
    public void AnEmptyLaneIsColdEverywhere()
    {
        HeatLane lane = HeatLane.Of(HeatKind.Problems, [], 10, 4);

        Assert.All(lane.Bins, b => Assert.Equal(0f, b));
        Assert.Null(lane.Nearest(5, 10));
    }
}
