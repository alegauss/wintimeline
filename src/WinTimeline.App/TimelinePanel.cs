using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Time;
using WinTimeline.Render;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.App;

/// <summary>
/// WT47: the timeline drawn on its GPU surface with Direct2D: a ruler, one lane per track, the
/// clips, gaps and transitions in view, their names, and the playhead. Only what the viewport
/// shows is drawn, found through <see cref="TimelineLayout.Visible"/>. The wheel scrolls lanes,
/// Shift+wheel or a tilt scrolls time, Ctrl+wheel zooms about the pointer, and a click moves the
/// playhead. Every clip in view is a UI Automation item of the surface.
/// </summary>
public sealed class TimelinePanel : IDisposable
{
    private const double RulerDip = 22;
    private const double HeaderDip = 44;
    private const double LaneDip = 28;
    private const int FrameWindow = 120;

    private static readonly Color4 Ground = new(0.11f, 0.11f, 0.13f, 1f);
    private static readonly Color4 LaneEven = new(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color4 LaneOdd = new(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color4 RulerGround = new(0.08f, 0.08f, 0.09f, 1f);
    private static readonly Color4 Tick = new(0.45f, 0.45f, 0.5f, 1f);
    private static readonly Color4 Label = new(0.85f, 0.85f, 0.88f, 1f);
    private static readonly Color4 VideoClip = new(0.25f, 0.4f, 0.72f, 1f);
    private static readonly Color4 AudioClip = new(0.2f, 0.52f, 0.36f, 1f);
    private static readonly Color4 NestedItem = new(0.48f, 0.38f, 0.68f, 1f);
    private static readonly Color4 DisabledClip = new(0.3f, 0.3f, 0.32f, 1f);
    private static readonly Color4 ClipEdge = new(0f, 0f, 0f, 0.55f);
    private static readonly Color4 TransitionFill = new(1f, 1f, 1f, 0.22f);
    private static readonly Color4 Playhead = new(0.95f, 0.3f, 0.25f, 1f);
    private static readonly double[] TickSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    private readonly GpuSurface _surface;
    private readonly Canvas2D _canvas;
    private readonly Queue<double> _frameTimes = new();
    private ModelTimeline? _timeline;
    private TimelineLayout? _layout;
    private TimelineViewport? _view;
    private double _playhead;

    /// <summary>Draws timelines on a surface.</summary>
    /// <param name="surface">The timeline surface.</param>
    /// <param name="gpu">The device it is on.</param>
    public TimelinePanel(GpuSurface surface, GpuContext gpu)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _canvas = new Canvas2D(gpu);
        _surface.Background = Ground;
        _surface.Draw += OnDraw;
        _surface.Wheel += OnWheel;
        _surface.Click += OnClick;
        _surface.Items = VisibleItems;
    }

    /// <summary>The viewport, or null before a timeline is shown on a sized surface.</summary>
    public TimelineViewport? View => _view;

    /// <summary>Shows a timeline, or nothing.</summary>
    /// <param name="timeline">The timeline.</param>
    public void Show(ModelTimeline? timeline)
    {
        if (_timeline is not null)
        {
            _timeline.ModelChanged -= OnModelChanged;
        }

        _timeline = timeline;
        _layout = timeline is null ? null : TimelineLayout.Build(timeline);
        _view = null;
        _playhead = 0;
        if (timeline is not null)
        {
            timeline.ModelChanged += OnModelChanged;
        }

        Changed();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Show(null);
        _surface.Draw -= OnDraw;
        _surface.Wheel -= OnWheel;
        _surface.Click -= OnClick;
        _surface.Items = null;
        _canvas.Dispose();
    }

    private double Scale => VisualTreeHelper.GetDpi(_surface).DpiScaleY;

    private double Ruler => Math.Round(RulerDip * Scale);

    private double Header => Math.Round(HeaderDip * Scale);

    private void Changed()
    {
        _surface.Invalidate();
        _surface.ItemsChanged();
    }

    private void OnModelChanged(object? sender, ModelChange e)
    {
        if (_timeline is not null)
        {
            _layout = TimelineLayout.Build(_timeline);
        }

        Changed();
    }

    /// <summary>The viewport for the surface's size, made on the first frame and kept across resizes.</summary>
    private TimelineViewport? Viewport(int width, int height)
    {
        if (_layout is null)
        {
            return null;
        }

        double w = Math.Max(1, width - Header);
        double h = Math.Max(1, height - Ruler);
        if (_view is null)
        {
            // The whole cut, unless that makes a second narrower than 10 pixels: then its first minutes.
            TimelineViewport fit = TimelineViewport.Fit(_layout.Duration, w, h, Math.Round(LaneDip * Scale));
            _view = fit.PixelsPerSecond < 10 ? fit with { PixelsPerSecond = 10 } : fit;
        }
        else if (_view.Width != w || _view.Height != h)
        {
            _view = _view.Resized(w, h);
        }

        return _view;
    }

    private void OnDraw(object? sender, SurfaceSwapChain target)
    {
        if (Viewport(target.Width, target.Height) is not { } view || _layout is not { } layout)
        {
            return;
        }

        Stopwatch watch = Stopwatch.StartNew();
        using (IDXGISurface back = target.BackBuffer())
        {
            _canvas.Begin(back);
            DrawLanes(view, layout, target.Width);
            DrawItems(view, layout);
            DrawRuler(view, target.Width);
            DrawHeaders(view, layout);
            float x = (float)(Header + view.X(_playhead));
            if (x >= Header)
            {
                _canvas.Line(new Vector2(x, 0), new Vector2(x, target.Height), Playhead, (float)Math.Max(1, Scale));
            }

            _canvas.End();
        }

        Measure(watch.Elapsed.TotalMilliseconds);
    }

    private void DrawLanes(TimelineViewport view, TimelineLayout layout, int width)
    {
        for (int lane = Math.Max(0, view.FirstLane); lane <= Math.Min(view.LastLane, layout.Lanes.Count - 1); lane++)
        {
            _canvas.Fill((float)Header, (float)(Ruler + view.Y(lane)), width, (float)view.LaneHeight, lane % 2 == 0 ? LaneEven : LaneOdd);
        }
    }

    private void DrawItems(TimelineViewport view, TimelineLayout layout)
    {
        float textSize = (float)(11 * Scale);
        foreach (LaneItem item in layout.Visible(view.Start, view.End, view.FirstLane, view.LastLane))
        {
            if (item.Kind == LaneItemKind.Gap)
            {
                continue;
            }

            (float x, float y, float w, float h) = Bounds(view, item);
            if (item.Kind == LaneItemKind.Transition)
            {
                _canvas.Fill(x, y, w, h, TransitionFill);
                _canvas.Line(new Vector2(x, y + h), new Vector2(x + w, y), Label, 1);
                continue;
            }

            bool enabled = item.Item is not Item { Enabled: false };
            Color4 fill = !enabled ? DisabledClip : item.Kind == LaneItemKind.Nested ? NestedItem : layout.Lanes[item.Lane].IsAudio ? AudioClip : VideoClip;
            _canvas.Fill(x, y, w, h, fill);
            if (w >= 3)
            {
                _canvas.Outline(x + 0.5f, y + 0.5f, w - 1, h - 1, ClipEdge);
            }

            // The name stays at the left of the view while the clip runs off it.
            float left = Math.Max(x, (float)Header) + 4;
            _canvas.Text(item.Name, left, y + ((h - (textSize * 1.4f)) / 2), x + w - left - 4, textSize, Label);
        }
    }

    private void DrawRuler(TimelineViewport view, int width)
    {
        _canvas.Fill(0, 0, width, (float)Ruler, RulerGround);
        double step = TickSteps.FirstOrDefault(s => s * view.PixelsPerSecond >= 90 * Scale, TickSteps[^1]);
        float textSize = (float)(10 * Scale);
        for (double t = Math.Floor(view.Start / step) * step; t <= view.End; t += step)
        {
            float x = (float)(Header + view.X(t));
            if (x < Header)
            {
                continue;
            }

            _canvas.Line(new Vector2(x, (float)(Ruler * 0.55)), new Vector2(x, (float)Ruler), Tick, 1);
            _canvas.Text(Timecode(t), x + 3, (float)(Ruler * 0.05), (float)(step * view.PixelsPerSecond) - 6, textSize, Tick);
        }
    }

    private void DrawHeaders(TimelineViewport view, TimelineLayout layout)
    {
        _canvas.Fill(0, (float)Ruler, (float)Header, (float)(view.Height + 1), RulerGround);
        float textSize = (float)(11 * Scale);
        for (int lane = Math.Max(0, view.FirstLane); lane <= Math.Min(view.LastLane, layout.Lanes.Count - 1); lane++)
        {
            float y = (float)(Ruler + view.Y(lane));
            if (y + view.LaneHeight > Ruler)
            {
                _canvas.Text(layout.Lanes[lane].Label, 6, Math.Max(y, (float)Ruler) + ((float)view.LaneHeight - (textSize * 1.4f)) / 2, (float)Header - 8, textSize, Label);
            }
        }
    }

    private (float X, float Y, float Width, float Height) Bounds(TimelineViewport view, LaneItem item)
    {
        double x = Header + view.X(item.Start);
        double right = Header + view.X(item.End);
        double y = Ruler + view.Y(item.Lane) + 1;
        return ((float)x, (float)y, (float)Math.Max(1, right - x), (float)(view.LaneHeight - 2));
    }

    private void Measure(double milliseconds)
    {
        _frameTimes.Enqueue(milliseconds);
        while (_frameTimes.Count > FrameWindow)
        {
            _frameTimes.Dequeue();
        }

        double[] sorted = [.. _frameTimes.Order()];
        _surface.Status = string.Create(CultureInfo.InvariantCulture,
            $"draw {sorted[sorted.Length / 2]:F1} ms median, {sorted[(int)(sorted.Length * 0.95)]:F1} ms p95 over {sorted.Length} frames");
    }

    private void OnWheel(object? sender, SurfaceWheel wheel)
    {
        if (_view is not { } view || _layout is not { } layout)
        {
            return;
        }

        double notches = wheel.Delta / 120.0;
        _view = wheel.Control
            ? view.ZoomAround(Math.Max(0, wheel.X - Header), Math.Pow(1.25, notches), layout.Duration)
            : wheel.Shift || wheel.Horizontal
                ? view.ScrollBy((wheel.Horizontal ? notches : -notches) * 96 * Scale, 0, layout.Duration, layout.Lanes.Count)
                : view.ScrollBy(0, -notches * view.LaneHeight, layout.Duration, layout.Lanes.Count);
        Changed();
    }

    private void OnClick(object? sender, SurfacePoint point)
    {
        if (_view is not { } view || point.X < Header)
        {
            return;
        }

        _playhead = Math.Max(0, view.Seconds(point.X - Header));
        _surface.Invalidate();
    }

    /// <summary>Every clip, transition and nested item in view, as UI Automation items.</summary>
    private List<SurfaceItem> VisibleItems()
    {
        if (_view is not { } view || _layout is not { } layout)
        {
            return [];
        }

        List<SurfaceItem> items = [];
        foreach (LaneItem item in layout.Visible(view.Start, view.End, view.FirstLane, view.LastLane))
        {
            if (item.Kind == LaneItemKind.Gap)
            {
                continue;
            }

            Lane lane = layout.Lanes[item.Lane];
            (float x, float y, float w, float h) = Bounds(view, item);
            string kind = item.Kind.ToString().ToLowerInvariant();
            int index = lane.Track.Children.ToList().IndexOf(item.Item);
            items.Add(new SurfaceItem(
                item.Name,
                string.Create(CultureInfo.InvariantCulture, $"{lane.Label}:{index}"),
                kind,
                $"{lane.Label}, {Timecode(item.Start)} to {Timecode(item.End)}",
                x, y, w, h));
        }

        return items;
    }

    private string Timecode(double seconds) => Timecode(seconds, _timeline?.GlobalStartTime);

    /// <summary>A time on the timeline as timecode from its global start, e.g. "01:00:04:12"; never throws.</summary>
    /// <param name="seconds">Seconds from the timeline's start; may be negative.</param>
    /// <param name="globalStart">The timeline's global start time, or null for zero at 24 fps.</param>
    /// <returns>The timecode, signed before the start, or seconds where the rate has no timecode.</returns>
    public static string Timecode(double seconds, RationalTime? globalStart)
    {
        RationalTime origin = globalStart ?? new RationalTime(0, 24);
        double rate = origin.Rate > 0 ? origin.Rate : 24;
        // Total: a transition can reach before the timeline's first frame, and a file's rate may
        // have no timecode, so neither may throw out of a frame or an automation query.
        RationalTime at = origin.RescaledTo(rate) + new RationalTime(Math.Round(seconds * rate), rate);
        try
        {
            return at.Value < 0 ? "-" + new RationalTime(-at.Value, rate).ToTimecode(rate) : at.ToTimecode(rate);
        }
        catch (TimecodeException)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{at.ValueRescaledTo(1):F3} s");
        }
    }
}
