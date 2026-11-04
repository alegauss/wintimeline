using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinTimeline.Engine.Timeline;
using TimelineRuler = WinTimeline.Engine.Timeline.Ruler;
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
    private const double OverviewDip = 12;
    private static readonly TimeSpan ZoomTime = TimeSpan.FromMilliseconds(120);
    // WT50: the lane labels are WPF track headers beside the surface now (TrackHeaders), so the surface draws none.
    private const double HeaderDip = 0;
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
    private static readonly Color4 OverviewGround = new(0.06f, 0.06f, 0.07f, 1f);
    private static readonly Color4 ZoomBoxFill = new(1f, 1f, 1f, 0.12f);
    private static readonly Color4 InOutFill = new(0.3f, 0.55f, 0.95f, 0.45f);

    private readonly GpuSurface _surface;
    private readonly Canvas2D _canvas;
    private readonly Queue<double> _frameTimes = new();
    private ModelTimeline? _timeline;
    private TimelineLayout? _layout;
    private TimelineViewport? _view;
    private TimelineViewport? _from;
    private TimelineViewport? _to;
    private readonly Stopwatch _animation = new();
    private Drag _dragging;
    private double? _inPoint;
    private double? _outPoint;
    private double _boxFrom;
    private double _boxTo;
    private bool _scrubbing;

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
        _surface.Pointer += OnPointer;
        _surface.Overlay += OnOverlay;
        _surface.Items = VisibleItems;
        Position.Changed += OnPositionChanged;
    }

    /// <summary>Raised when the playhead moves: a click, a seek, a step, or a new timeline shown.</summary>
    public event EventHandler? PlayheadMoved;

    /// <summary>WT51: the current frame, which the viewer and the transport read and set too.</summary>
    public PlaybackPosition Position { get; } = new();

    /// <summary>WT51: how the view keeps the playhead in sight.</summary>
    public PlayheadFollow Follow { get; set; } = PlayheadFollow.Page;

    /// <summary>WT50: raised when the lanes scroll or change, so the track headers follow.</summary>
    public event EventHandler? LanesMoved;

    /// <summary>WT50: how far the lanes start below the surface's top, in device-independent pixels.</summary>
    public static double LanesTopDip => OverviewDip + RulerDip;

    /// <summary>WT50: a lane's height, in device-independent pixels.</summary>
    public static double LaneHeightDip => LaneDip;

    /// <summary>WT50: how far the lanes are scrolled down, in device-independent pixels.</summary>
    public double LanesScrollDip => (_view?.Top ?? 0) / Scale;

    /// <summary>WT49: what the ruler labels time in; changing it redraws.</summary>
    public RulerUnit Unit
    {
        get;
        set
        {
            field = value;
            _surface.Invalidate();
        }
    }

    /// <summary>WT49: the in point set by Shift-dragging the ruler, in seconds, or null.</summary>
    public double? InPoint => _inPoint is { } a && _outPoint is { } b ? Math.Min(a, b) : null;

    /// <summary>WT49: the out point, in seconds, or null.</summary>
    public double? OutPoint => _inPoint is { } a && _outPoint is { } b ? Math.Max(a, b) : null;

    /// <summary>The viewport, or null before a timeline is shown on a sized surface.</summary>
    public TimelineViewport? View => _view;

    /// <summary>The shown timeline's layout, or null.</summary>
    public TimelineLayout? Layout => _layout;

    /// <summary>Where the playhead is, in seconds from the timeline's start.</summary>
    public double PlayheadSeconds => Position.Seconds;

    /// <summary>A time on the shown timeline as timecode.</summary>
    /// <param name="seconds">Seconds from its start.</param>
    /// <returns>The timecode.</returns>
    public string TimecodeAt(double seconds) => Timecode(seconds);

    /// <summary>A typed timecode as seconds on the shown timeline, or null when it is not one.</summary>
    /// <param name="text">e.g. "01:00:04:12".</param>
    /// <returns>The seconds, or null.</returns>
    public double? ParseTimecode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_timeline is null || text.Count(c => c is ':' or ';') != 3 || !text.All(c => char.IsDigit(c) || c is ':' or ';'))
        {
            return null;
        }

        RationalTime origin = _timeline.GlobalStartTime ?? new RationalTime(0, 24);
        double rate = origin.Rate > 0 ? origin.Rate : 24;
        try
        {
            return (RationalTime.FromTimecode(text, rate) - origin.RescaledTo(rate)).ValueRescaledTo(1);
        }
        catch (TimecodeException)
        {
            return null;
        }
    }

    /// <summary>Moves the playhead, scrolling the view so it shows whatever the follow mode.</summary>
    /// <param name="seconds">Where, in seconds from the timeline's start.</param>
    public void Seek(double seconds)
    {
        Position.GoTo(Math.Max(0, seconds));
        if (_view is { } view && _layout is { } layout && view.Follow(Position.Seconds, PlayheadFollow.Page, layout.Duration) is var shown && shown != view)
        {
            _view = shown;
            Changed();
        }
    }

    /// <summary>WT51: steps the playhead by frames, back when negative.</summary>
    /// <param name="frames">How many.</param>
    public void Step(long frames) => Position.Step(frames);

    /// <summary>WT51: jumps the playhead to the next or previous edit point.</summary>
    /// <param name="forward">Whether to go forward.</param>
    public void JumpToEdit(bool forward)
    {
        if (_layout is { } layout)
        {
            Position.Jump(EditPoints.Of(layout), forward);
        }
    }

    /// <summary>WT51: puts the playhead on the programme's first frame, or its last.</summary>
    /// <param name="end">Whether to go to the last.</param>
    public void GoToLimit(bool end) => Position.Go(end ? Position.LastFrame : 0);

    /// <summary>WT51: draws the playhead where the position is, scrolling the view as the follow mode says.</summary>
    private void OnPositionChanged(object? sender, long frame)
    {
        PlayheadMoved?.Invoke(this, EventArgs.Empty);
        if (!_scrubbing && _view is { } view && _layout is { } layout && view.Follow(Position.Seconds, Follow, layout.Duration) is var followed && followed != view)
        {
            _view = followed;
            Changed();
            return;
        }

        _surface.InvalidateOverlay();
    }

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
        Position.Reset(Rate, _layout?.Duration ?? 0);
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
        _surface.Pointer -= OnPointer;
        _surface.Overlay -= OnOverlay;
        _surface.Items = null;
        _canvas.Dispose();
    }

    private double Scale => VisualTreeHelper.GetDpi(_surface).DpiScaleY;

    /// <summary>The timeline's frame rate, from its global start time; 24 when it states none.</summary>
    private double Rate => _timeline?.GlobalStartTime is { Rate: > 0 } start ? start.Rate : 24;

    /// <summary>WT48: the overview strip's height above the ruler.</summary>
    private double Overview => Math.Round(OverviewDip * Scale);

    /// <summary>Where the lanes start: below the overview strip and the ruler.</summary>
    private double Ruler => Overview + Math.Round(RulerDip * Scale);

    private double Header => Math.Round(HeaderDip * Scale);

    private void Changed()
    {
        LanesMoved?.Invoke(this, EventArgs.Empty);
        _surface.Invalidate();
        _surface.ItemsChanged();
    }

    private void OnModelChanged(object? sender, ModelChange e)
    {
        if (_timeline is not null)
        {
            _layout = TimelineLayout.Build(_timeline);
            Position.Lengthen(_layout.Duration);
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
            fit = fit with { FrameRate = Rate };
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

        Animate();
        view = _view!;
        Stopwatch watch = Stopwatch.StartNew();
        using (IDXGISurface back = target.BackBuffer())
        {
            _canvas.Begin(back);
            DrawOverview(view, layout, target.Width);
            DrawLanes(view, layout, target.Width);
            DrawItems(view, layout);
            DrawRuler(view, target.Width);
            _canvas.End();
        }

        Measure(watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>WT152: the playhead, drawn over the frame as the surface's overlay.</summary>
    private void OnOverlay(object? sender, SurfaceSwapChain target)
    {
        if (_view is not { } view || _layout is null)
        {
            return;
        }

        float x = (float)(Header + view.X(Position.Seconds));
        if (x < Header)
        {
            return;
        }

        using IDXGISurface back = target.BackBuffer();
        _canvas.Begin(back);
        _canvas.Line(new Vector2(x, (float)Overview), new Vector2(x, target.Height), Playhead, (float)Math.Max(1, Scale));
        if (_dragging == Drag.ZoomBox)
        {
            float from = (float)(Header + Math.Min(_boxFrom, _boxTo)), span = (float)Math.Abs(_boxTo - _boxFrom);
            _canvas.Fill(from, (float)Ruler, span, target.Height - (float)Ruler, ZoomBoxFill);
            _canvas.Outline(from, (float)Ruler, span, target.Height - (float)Ruler, Label, 1);
        }

        _canvas.End();
    }

    /// <summary>
    /// WT152 and WT48: a drag over the overview strip moves the view there; a drag with Z held
    /// draws a zoom box and zooms to it on release; any other drag over the lanes scrubs.
    /// </summary>
    private void OnPointer(object? sender, SurfacePointer pointer)
    {
        if (_view is not { } view || _layout is not { } layout)
        {
            return;
        }

        if (pointer.Phase == PointerPhase.Down)
        {
            _dragging = pointer.Y < Overview ? Drag.Overview
                : pointer.Y < Ruler && pointer.Shift ? Drag.InOut
                : System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Z) ? Drag.ZoomBox
                : Drag.Scrub;
            if (_dragging == Drag.InOut)
            {
                _inPoint = _outPoint = Math.Max(0, view.Seconds(pointer.X - Header));
            }
            _boxFrom = _boxTo = Math.Max(0, pointer.X - Header);
        }

        switch (_dragging)
        {
            case Drag.Overview when pointer.Pressed:
                double centre = (pointer.X - Header) / Math.Max(1, view.Width) * layout.Duration;
                _view = view.ScrollBy(view.X(centre) - (view.Width / 2), 0, layout.Duration, layout.Lanes.Count);
                Changed();
                break;
            case Drag.ZoomBox:
                _boxTo = Math.Max(0, pointer.X - Header);
                if (pointer.Phase == PointerPhase.Up)
                {
                    _dragging = Drag.None;
                    if (Math.Abs(_boxTo - _boxFrom) > 4)
                    {
                        AnimateTo(view.ZoomToRange(view.Seconds(_boxFrom), view.Seconds(_boxTo), layout.Duration));
                    }
                }

                _surface.Invalidate();
                break;
            case Drag.InOut:
                _outPoint = Math.Max(0, view.Seconds(pointer.X - Header));
                _surface.Invalidate();
                break;
            case Drag.Scrub when pointer.Phase == PointerPhase.Move && pointer.Pressed:
                OnClick(sender, new SurfacePoint(pointer.X, pointer.Y));
                break;
        }

        if (pointer.Phase == PointerPhase.Up)
        {
            _dragging = Drag.None;
        }
    }

    /// <summary>WT48: zooms in about the playhead, as + does.</summary>
    public void ZoomIn() => ZoomAboutPlayhead(2);

    /// <summary>WT48: zooms out about the playhead, as - does.</summary>
    public void ZoomOut() => ZoomAboutPlayhead(0.5);

    /// <summary>WT48: shows the whole programme, as Shift+Z does.</summary>
    public void ZoomToFit()
    {
        if (_view is { } view && _layout is { } layout)
        {
            AnimateTo(view.FitAll(layout.Duration));
        }
    }

    private void ZoomAboutPlayhead(double factor)
    {
        if (_view is { } view && _layout is { } layout)
        {
            AnimateTo((_to ?? view).ZoomAroundTime(Position.Seconds, factor, layout.Duration));
        }
    }

    /// <summary>WT48: eases the view to another over 120 ms, a frame at a time.</summary>
    private void AnimateTo(TimelineViewport to)
    {
        _from = _view;
        _to = to;
        _animation.Restart();
        _surface.Continuous = true;
        Changed();
    }

    /// <summary>Advances a running zoom; true while it is still moving.</summary>
    private void Animate()
    {
        if (_from is not { } from || _to is not { } to)
        {
            return;
        }

        double share = Math.Min(1, _animation.Elapsed.TotalMilliseconds / ZoomTime.TotalMilliseconds);
        double eased = 1 - Math.Pow(1 - share, 3);
        _view = from.Toward(to, eased);
        if (share >= 1)
        {
            _view = to;
            _from = _to = null;
            _surface.Continuous = false;
            _surface.ItemsChanged();
        }
    }

    /// <summary>WT48: the whole programme in a strip above the ruler, its clips as bands and the view as a window.</summary>
    private void DrawOverview(TimelineViewport view, TimelineLayout layout, int width)
    {
        float top = 0, height = (float)Overview, across = (float)Math.Max(1, width - Header);
        _canvas.Fill(0, top, width, height, OverviewGround);
        double scale = across / Math.Max(layout.Duration, 1e-6);
        float band = Math.Max(1, height / Math.Max(1, layout.Lanes.Count));
        foreach (Lane lane in layout.Lanes)
        {
            foreach (LaneItem item in lane.Items)
            {
                if (item.Kind is LaneItemKind.Clip or LaneItemKind.Nested)
                {
                    float x = (float)(Header + (item.Start * scale));
                    _canvas.Fill(x, top + (item.Lane * band), Math.Max(1, (float)((item.End - item.Start) * scale)), band, lane.IsAudio ? AudioClip : VideoClip);
                }
            }
        }

        float left = (float)(Header + (view.Start * scale)), right = (float)(Header + (view.End * scale));
        _canvas.Outline(left, top + 0.5f, Math.Max(2, right - left), height - 1, Label, 1);
    }

    private enum Drag
    {
        None,
        Scrub,
        Overview,
        ZoomBox,
        InOut,
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
        _canvas.Fill(0, (float)Overview, width, (float)(Ruler - Overview), RulerGround);
        if (_inPoint is { } inAt && _outPoint is { } outAt)
        {
            float from = (float)(Header + view.X(Math.Min(inAt, outAt))), to = (float)(Header + view.X(Math.Max(inAt, outAt)));
            _canvas.Fill(Math.Max(from, (float)Header), (float)Overview, Math.Max(1, to - Math.Max(from, (float)Header)), (float)(Ruler - Overview), InOutFill);
        }

        // WT49: ticks on multiples of a step from frame 0, labelled in the chosen unit.
        float textSize = (float)(10 * Scale);
        double spacing = 90 * Scale;
        double labelWidth = TimelineRuler.Step(view.PixelsPerSecond, Rate, spacing) / Rate * view.PixelsPerSecond;
        RationalTime origin = _timeline?.GlobalStartTime ?? new RationalTime(0, Rate);
        float band = (float)(Ruler - Overview);
        foreach (RulerTick tick in TimelineRuler.Ticks(view, Rate, origin, Unit, spacing))
        {
            float x = (float)(Header + view.X(tick.Seconds));
            if (x < Header)
            {
                continue;
            }

            _canvas.Line(new Vector2(x, (float)(Overview + (band * (tick.Major ? 0.55 : 0.8)))), new Vector2(x, (float)Ruler), Tick, 1);
            if (tick.Major)
            {
                _canvas.Text(tick.Label, x + 3, (float)(Overview + (band * 0.05)), (float)labelWidth - 6, textSize, Tick);
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
        string span = (_view is { } view ? ", " + UiText.ViewSpan(Timecode(view.Start), Timecode(view.End)) : string.Empty) + ", " + UiText.RulerStatus(UiText.RulerUnitName(Unit));
        _surface.Status = UiText.DrawTimes(sorted[sorted.Length / 2], sorted[(int)(sorted.Length * 0.95)], sorted.Length) + span;
    }

    private void OnWheel(object? sender, SurfaceWheel wheel)
    {
        if (_view is not { } view || _layout is not { } layout)
        {
            return;
        }

        double notches = wheel.Delta / 120.0;
        if (wheel.Control)
        {
            AnimateTo((_to ?? view).ZoomAround(Math.Max(0, wheel.X - Header), Math.Pow(1.25, notches), layout.Duration));
            return;
        }

        _view = wheel.Shift || wheel.Horizontal
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

        // A click or a scrub lands where the pointer is, which is already in view: no follow.
        _scrubbing = true;
        try
        {
            Position.GoTo(Math.Max(0, view.Seconds(point.X - Header)));
        }
        finally
        {
            _scrubbing = false;
        }
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
                string.Create(CultureInfo.InvariantCulture, $"{lane.Label}-{index}"),
                kind,
                UiText.ItemSpan(lane.Label, Timecode(item.Start), Timecode(item.End)),
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
            return UiText.Seconds(at.ValueRescaledTo(1));
        }
    }
}
