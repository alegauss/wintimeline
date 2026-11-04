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
    /// <summary>The farthest zoom any timeline goes to: ten hours fit in 3,600 pixels.</summary>
    public const double MinPixelsPerSecond = 0.1;

    /// <summary>WT48: how wide one frame is at the closest zoom.</summary>
    public const double FramePixels = 200;

    /// <summary>The timeline's frame rate, which sets the closest zoom; 24 unless given.</summary>
    public double FrameRate { get; init; } = 24;

    /// <summary>WT48: the closest zoom, one frame <see cref="FramePixels"/> wide.</summary>
    public double MostPixelsPerSecond => FramePixels * FrameRate;

    /// <summary>WT48: the farthest zoom for a timeline: all of it across the view.</summary>
    /// <param name="duration">The timeline's length.</param>
    /// <returns>The zoom.</returns>
    public double LeastPixelsPerSecond(double duration) =>
        Math.Clamp(Width / Math.Max(duration, 1 / FrameRate), MinPixelsPerSecond, MostPixelsPerSecond);

    /// <summary>WT48: all of the timeline across the view, scrolled to its start.</summary>
    /// <param name="duration">The timeline's length.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport FitAll(double duration) => this with { Start = 0, PixelsPerSecond = LeastPixelsPerSecond(duration) };

    /// <summary>WT48: zooms by a factor keeping a time where it is on screen, as +/- do about the playhead.</summary>
    /// <param name="seconds">The time held in place; scrolled into view first when it is outside.</param>
    /// <param name="factor">Above 1 zooms in.</param>
    /// <param name="duration">The timeline's length.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport ZoomAroundTime(double seconds, double factor, double duration)
    {
        double x = seconds >= Start && seconds <= End ? X(seconds) : Width / 2;
        TimelineViewport at = this with { Start = seconds - (x / PixelsPerSecond) };
        return at.ZoomAround(x, factor, duration);
    }

    /// <summary>WT48: shows exactly a range of time across the view, as a zoom box does.</summary>
    /// <param name="from">One end of the range.</param>
    /// <param name="to">The other.</param>
    /// <param name="duration">The timeline's length.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport ZoomToRange(double from, double to, double duration)
    {
        double start = Math.Min(from, to), span = Math.Max(Math.Abs(to - from), 1 / FrameRate);
        double pps = Math.Clamp(Width / span, LeastPixelsPerSecond(duration), MostPixelsPerSecond);
        return (this with { PixelsPerSecond = pps, Start = start + ((span - (Width / pps)) / 2) }).Clamped(duration, int.MaxValue);
    }

    /// <summary>
    /// WT48: the view a share of the way to another, zoom eased in log space so a deep zoom moves
    /// as smoothly at frame level as at fit-all; what a 120 ms zoom animation draws each frame.
    /// </summary>
    /// <param name="to">Where the animation ends.</param>
    /// <param name="share">0 for this view, 1 for the other.</param>
    /// <returns>The view in between.</returns>
    public TimelineViewport Toward(TimelineViewport to, double share)
    {
        ArgumentNullException.ThrowIfNull(to);
        double t = Math.Clamp(share, 0, 1);
        double pps = Math.Exp(Math.Log(PixelsPerSecond) + ((Math.Log(to.PixelsPerSecond) - Math.Log(PixelsPerSecond)) * t));

        // The time at the screen point both views agree on stays put, so the zoom does not drift.
        double pivot = Math.Abs(to.PixelsPerSecond - PixelsPerSecond) < 1e-9 ? 0 : (to.Start - Start) / ((1 / PixelsPerSecond) - (1 / to.PixelsPerSecond));
        double start = double.IsFinite(pivot) && Math.Abs(to.PixelsPerSecond - PixelsPerSecond) > 1e-9
            ? Start + (pivot / PixelsPerSecond) - (pivot / pps)
            : Start + ((to.Start - Start) * t);
        return this with { PixelsPerSecond = pps, Start = start, Top = Top + ((to.Top - Top) * t) };
    }

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
        new(0, Math.Clamp(width / Math.Max(duration, 1), MinPixelsPerSecond, FramePixels * 24), 0, laneHeight, width, height);

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
        double pps = Math.Clamp(PixelsPerSecond * factor, LeastPixelsPerSecond(duration), MostPixelsPerSecond);
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

    /// <summary>
    /// WT51: the view that keeps a playhead in sight. Page jumps a whole view on when the
    /// playhead leaves it, putting it at the left edge; smooth keeps it centred; off stays put.
    /// </summary>
    /// <param name="seconds">Where the playhead is.</param>
    /// <param name="mode">The follow mode.</param>
    /// <param name="duration">The timeline's length.</param>
    /// <returns>The new viewport.</returns>
    public TimelineViewport Follow(double seconds, PlayheadFollow mode, double duration) => mode switch
    {
        PlayheadFollow.Page when seconds < Start || seconds >= End => (this with { Start = seconds }).Clamped(duration, int.MaxValue),
        PlayheadFollow.Smooth => (this with { Start = seconds - (Width / PixelsPerSecond / 2) }).Clamped(duration, int.MaxValue),
        _ => this,
    };

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
