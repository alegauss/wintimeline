using WinTimeline.Engine.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>WT48: zoom goes from the whole program to one frame and back without the frame under the cursor moving.</summary>
public sealed class ZoomTests
{
    private const double Duration = 5400; // a 90-minute programme
    private static readonly TimelineViewport Fit = new TimelineViewport(0, 1, 0, 30, 1600, 600) { FrameRate = 25 }.FitAll(Duration);

    [Fact]
    public void FromFitAllToOneFrameAndBackTheFrameUnderTheCursorStaysWithinAPixel()
    {
        const double cursor = 1234;
        double frame = Math.Floor(Fit.Seconds(cursor) * 25) / 25;
        double x = Fit.X(frame);

        TimelineViewport view = Fit;
        while (view.PixelsPerSecond < view.MostPixelsPerSecond)
        {
            view = view.ZoomAround(x, 1.25, Duration);
            Assert.InRange(view.X(frame), x - 1, x + 1);
        }

        Assert.Equal(TimelineViewport.FramePixels, view.PixelsPerSecond / 25, 6);
        while (view.PixelsPerSecond > view.LeastPixelsPerSecond(Duration))
        {
            view = view.ZoomAround(x, 0.8, Duration);
        }

        Assert.InRange(view.X(frame), x - 1, x + 1);
        Assert.Equal(Fit.PixelsPerSecond, view.PixelsPerSecond, 6);
    }

    [Fact]
    public void ZoomNeverGoesPastOneFrameOrPastTheWholeProgramme()
    {
        Assert.Equal(1600 / Duration, Fit.PixelsPerSecond, 9);
        Assert.Equal(5000, Fit.ZoomAround(800, 1e9, Duration).PixelsPerSecond);
        Assert.Equal(Fit.PixelsPerSecond, Fit.ZoomAround(800, 1e-9, Duration).PixelsPerSecond);
    }

    [Fact]
    public void PlusAndMinusZoomAboutThePlayheadAndBringItIntoView()
    {
        TimelineViewport close = Fit.ZoomAround(0, 100, Duration);
        Assert.False(close.End > 3000);

        TimelineViewport about = close.ZoomAroundTime(3000, 2, Duration);

        Assert.InRange(3000, about.Start, about.End);
        Assert.InRange(about.X(3000), (about.Width / 2) - 1, (about.Width / 2) + 1);
    }

    [Fact]
    public void AZoomBoxShowsExactlyItsRange()
    {
        TimelineViewport boxed = Fit.ZoomToRange(600, 660, Duration);

        Assert.Equal(600, boxed.Start, 6);
        Assert.Equal(660, boxed.End, 6);
    }

    [Fact]
    public void TheAnimationHoldsTheZoomPointAtEveryStep()
    {
        TimelineViewport to = Fit.ZoomAround(400, 40, Duration);
        double anchor = Fit.Seconds(400);

        foreach (double share in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            TimelineViewport step = Fit.Toward(to, share);
            Assert.InRange(step.X(anchor), 399, 401);
        }

        Assert.Equal(to.PixelsPerSecond, Fit.Toward(to, 1).PixelsPerSecond, 6);
    }
}
