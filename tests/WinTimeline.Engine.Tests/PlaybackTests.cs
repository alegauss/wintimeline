using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT51: one current frame that steps, jumps between cuts and stays on the timeline.</summary>
public sealed class PlaybackTests
{
    private static TimelineLayout Cut() =>
        TimelineLayout.Build(Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio"))));

    [Fact]
    public void AStepMovesWholeFramesAndStopsAtTheEnds()
    {
        PlaybackPosition position = new();
        position.Reset(24, 10);
        List<long> seen = [];
        position.Changed += (_, frame) => seen.Add(frame);

        position.Step(3);
        position.Step(10);
        position.Step(-100);
        position.Go(1_000_000);
        position.GoTo(2.51);

        Assert.Equal([3, 13, 0, 239, 60], seen);
        Assert.Equal(60 / 24.0, position.Seconds);
    }

    [Fact]
    public void PageJumpsAViewOnSmoothCentresAndOffStays()
    {
        // 100 px per second over 1000 px: ten seconds in view, of a sixty-second timeline.
        TimelineViewport view = new(0, 100, 0, 40, 1000, 400);

        Assert.Same(view, view.Follow(5, PlayheadFollow.Page, 60));
        Assert.Equal(12, view.Follow(12, PlayheadFollow.Page, 60).Start);
        Assert.Equal(7, view.Follow(12, PlayheadFollow.Smooth, 60).Start);
        Assert.Equal(0, view.Follow(2, PlayheadFollow.Smooth, 60).Start);
        Assert.Same(view, view.Follow(40, PlayheadFollow.Off, 60));
    }

    [Fact]
    public void UpAndDownJumpBetweenTheCutsOfEveryLane()
    {
        TimelineLayout layout = Cut();
        IReadOnlyList<double> cuts = EditPoints.Of(layout);
        PlaybackPosition position = new();
        position.Reset(24, layout.Duration);

        Assert.Equal([0, 2, 4, 5, 6.5, 7.5, 8.5], cuts);
        position.Step(3);
        position.Jump(cuts, forward: true);
        Assert.Equal(48, position.Frame);
        position.Jump(cuts, forward: true);
        Assert.Equal(96, position.Frame);
        position.Jump(cuts, forward: false);
        Assert.Equal(48, position.Frame);

        position.Go(layout.Duration > 0 ? 203 : 0);
        position.Jump(cuts, forward: true);
        Assert.Equal(position.LastFrame, position.Frame);
    }
}
