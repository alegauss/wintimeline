namespace WinTimeline.Engine.Timeline;

/// <summary>
/// WT47: which part of a timeline is in view and at what scale, in device pixels: time runs
/// across from <see cref="Start"/>, lanes run down from <see cref="Top"/>. Every change returns a
/// new viewport, clamped so the timeline cannot be scrolled away.
/// </summary>
/// <param name="Start">The second at the left edge.</param>
/// <param name="PixelsPerSecond">The zoom.</param>
/// <param name="Top">How far down the lanes are scrolled, in pixels.</param>
/// <param name="LaneHeight">How tall a lane is.</param>
/// <param name="Width">The view's width.</param>
/// <param name="Height">The view's height.</param>
public sealed record TimelineViewport(double Start, double PixelsPerSecond, double Top, double LaneHeight, double Width, double Height)
{
    /// <summary>The closest zoom: a frame at 120 fps is still 10 pixels wide.</summary>
    public const double MaxPixelsPerSecond = 1200;

    /// <summary>The farthest zoom: ten hours fit in 3,600 pixels.</summary>
    public const double MinPixelsPerSecond = 0.1;

    /// <summary>The second at the right edge.</summary>
    public double End => Start + (Width / PixelsPerSecond);

    /// <summary>The first lane any part of which is in view.</summary>
    public int FirstLane => (int)Math.Floor(Top / LaneHeight);

    /// <summary>The last lane any part of which is in view.</summary>
    public int LastLane => (int)Math.Floor((Top + Height) / LaneHeight);

    /// <summary>A view that shows all of a timeline across a width.</summary>
    /// <param name="duration">The timeline's length in seconds.</param>
    /// <param name="width">The view's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="laneHeight">A lane's height.</param>
    /// <returns>The viewport.</returns>
    public static TimelineViewport Fit(double duration, double width, double height, double laneHeight) =>
        new(0, Math.Clamp(width / Math.Max(duration, 1), MinPixelsPerSecond, MaxPixelsPerSecond), 0, laneHeight, width, height);

    /// <summary>Where a time is across the view.</summary>
    /// <param name="seconds">The time.</param>
    /// <returns>The x coordinate.</returns>
    public double X(double seconds) => (seconds - Start) * PixelsPerSecond;

    /// <summary>The time at a point across the view.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <returns>The time.</returns>
    public double Seconds(double x) => Start + (x / PixelsPerSecond);

    /// <summary>Where a lane's top is down the view.</summary>
    /// <param name="lane">The lane.</param>
    /// <returns>The y coordinate.</returns>
    public double Y(int lane) => (lane * LaneHeight) - Top;

    /// <summary>The lane at a point down the view, or -1 above the first.</summary>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The lane.</returns>
    public int Lane(double y) => y + Top < 0 ? -1 : (int)Math.Floor((y + Top) / LaneHeight);

    /// <summary>Zooms by a factor, keeping the time under a point where it is.</summary>
    /// <param name="x">The point across the view.</param>
    /// <param name="factor">Above 1 zooms in.</param>
    /// <param name="duration">The timeline's length, which the view is clamped to.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport ZoomAround(double x, double factor, double duration)
    {
        double pinned = Seconds(x);
        double pps = Math.Clamp(PixelsPerSecond * factor, MinPixelsPerSecond, MaxPixelsPerSecond);
        return (this with { PixelsPerSecond = pps, Start = pinned - (x / pps) }).Clamped(duration, int.MaxValue);
    }

    /// <summary>Scrolls by pixels across and down.</summary>
    /// <param name="dx">Pixels across; positive shows later time.</param>
    /// <param name="dy">Pixels down; positive shows lower lanes.</param>
    /// <param name="duration">The timeline's length.</param>
    /// <param name="lanes">How many lanes there are.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport ScrollBy(double dx, double dy, double duration, int lanes) =>
        (this with { Start = Start + (dx / PixelsPerSecond), Top = Top + dy }).Clamped(duration, lanes);

    /// <summary>The same view at a new size.</summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport Resized(double width, double height) => this with { Width = width, Height = height };

    private TimelineViewport Clamped(double duration, int lanes)
    {
        double lastStart = Math.Max(0, duration - (Width / PixelsPerSecond * 0.5));
        double lastTop = Math.Max(0, (lanes * LaneHeight) - Height);
        return this with { Start = Math.Clamp(Start, 0, lastStart), Top = Math.Clamp(Top, 0, lastTop) };
    }
}
