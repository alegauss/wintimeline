using System.Diagnostics;
using System.IO;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT47: the timeline's lanes and the viewport query the GPU panel draws from.</summary>
public sealed class TimelineLayoutTests
{
    private static ModelTimeline Load(params string[] path) =>
        Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine([Repository.Root, "tests", "fixtures", .. path])));

    [Fact]
    public void VideoLanesStackFromTheHighestTrackDownAndAudioBelow()
    {
        TimelineLayout layout = TimelineLayout.Build(Load("diff", "cut_v1.otio"));

        Assert.Equal(["V1", "A1"], layout.Lanes.Select(l => l.Label));
        Assert.Equal(["c1", "c2", "c3", "c4", "c5", "c7"], layout.Lanes[0].Items.Select(i => i.Name));
        LaneItem c3 = layout.Lanes[0].Items[2];
        Assert.Equal((4.0, 5.0), (c3.Start, c3.End));
        Assert.Equal(8.5, layout.Duration);
    }

    [Fact]
    public void ATransitionStraddlesTheCutBetweenItsNeighbours()
    {
        TimelineLayout layout = TimelineLayout.Build(Load("otio", "corpus", "transition_test.otio"));

        // Every transition with a clip before it covers that clip's cut, strictly inside its own span.
        LaneItem[] transitions = [.. layout.Lanes.SelectMany(l => l.Items).Where(i => i.Kind == LaneItemKind.Transition)];
        Assert.NotEmpty(transitions);
        int straddling = 0;
        foreach (LaneItem transition in transitions)
        {
            LaneItem? before = layout.Lanes[transition.Lane].Items.Where(i => i.Kind != LaneItemKind.Transition && i.Start < transition.Start).MaxBy(i => i.Start);
            if (before is not null && before.End > transition.Start && before.End < transition.End)
            {
                straddling++;
                Assert.Same(transition, layout.At(before.End, transition.Lane));
            }
        }

        Assert.True(straddling > 0, "no transition straddles a cut");
    }

    [Fact]
    public void TheViewportQueryReturnsExactlyWhatOverlapsIt()
    {
        TimelineLayout layout = TimelineLayout.Build(Big.Timeline());
        Random random = new(47);
        for (int probe = 0; probe < 200; probe++)
        {
            double from = random.NextDouble() * layout.Duration;
            double to = from + (random.NextDouble() * 60);
            int first = random.Next(24);
            int last = first + random.Next(24 - first);

            LaneItem[] expected = [.. layout.Lanes.Skip(first).Take(last - first + 1).SelectMany(l => l.Items).Where(i => i.End > from && i.Start < to)];
            Assert.Equal(expected, layout.Visible(from, to, first, last));
        }
    }

    [Fact]
    public void AFeatureLengthCutIsLaidOutOnceAndQueriedPerFrameInMicroseconds()
    {
        ModelTimeline feature = Big.Timeline();
        Stopwatch build = Stopwatch.StartNew();
        TimelineLayout layout = TimelineLayout.Build(feature);
        build.Stop();
        Assert.Equal(3000, layout.Lanes.Sum(l => l.Items.Count(i => i.Kind == LaneItemKind.Clip)));
        Assert.Equal(24, layout.Lanes.Count);

        TimelineViewport view = TimelineViewport.Fit(layout.Duration, 1920, 720, 30).ZoomAround(960, 40, layout.Duration);
        Stopwatch frames = Stopwatch.StartNew();
        int drawn = 0;
        for (int frame = 0; frame < 1000; frame++)
        {
            view = view.ScrollBy(7, 0, layout.Duration, layout.Lanes.Count);
            drawn += layout.Visible(view.Start, view.End, view.FirstLane, view.LastLane).Count();
        }

        frames.Stop();
        Assert.True(drawn > 0);
        Assert.True(build.ElapsedMilliseconds < 500, $"laying out 3,000 clips took {build.ElapsedMilliseconds} ms");
        Assert.True(frames.Elapsed.TotalMilliseconds / 1000 < 0.5, $"a viewport query took {frames.Elapsed.TotalMilliseconds / 1000:F3} ms");
    }

    [Fact]
    public void ZoomKeepsTheTimeUnderThePointerAndScrollingStaysOnTheTimeline()
    {
        TimelineViewport view = TimelineViewport.Fit(600, 1200, 300, 30);
        double under = view.Seconds(300);

        TimelineViewport zoomed = view.ZoomAround(300, 8, 600);
        Assert.Equal(under, zoomed.Seconds(300), 6);

        TimelineViewport scrolled = zoomed.ScrollBy(-1e9, -1e9, 600, 4);
        Assert.Equal((0.0, 0.0), (scrolled.Start, scrolled.Top));
        Assert.Equal(view.MostPixelsPerSecond, view.ZoomAround(0, 1e9, 600).PixelsPerSecond);
    }
}
