using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Threading;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinTimeline.Engine.Timeline;
using TimelineRuler = WinTimeline.Engine.Timeline.Ruler;
using WinTimeline.Model;
using WinTimeline.Model.Time;
using WinTimeline.Render;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.App;

/// <summary>WT70: what a dropped drag of clips does.</summary>
public enum MoveMode
{
    /// <summary>Replaces what the clips land on (no key).</summary>
    Overwrite,

    /// <summary>Pushes what follows later (Ctrl).</summary>
    Insert,

    /// <summary>Changes places with the clip landed on (Ctrl+Alt).</summary>
    Swap,
}

/// <summary>WT70: a released drag of clips.</summary>
/// <param name="Items">The clips dragged.</param>
/// <param name="Delta">How far in time, in seconds, snapped and on a frame.</param>
/// <param name="Lanes">How many lanes down, up when negative.</param>
/// <param name="Mode">Overwrite, insert or swap.</param>
/// <param name="Duplicate">Whether Alt held at the press asked for copies, leaving the clips where they were.</param>
/// <param name="Onto">The clip under the release, for a swap.</param>
public sealed record MoveRequest(IReadOnlyList<LaneItem> Items, double Delta, int Lanes, MoveMode Mode, bool Duplicate, LaneItem? Onto);

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
    private static readonly Color4 SelectedEdge = new(1f, 0.82f, 0.25f, 1f);
    private static readonly Color4 SnapGuide = new(0.35f, 0.9f, 1f, 1f);
    private static readonly Color4 FocusRing = new(1f, 1f, 1f, 1f);

    private readonly GpuSurface _surface;
    private readonly Canvas2D _canvas;
    private readonly Queue<double> _frameTimes = new();
    private ModelTimeline? _timeline;
    private TimelineLayout? _layout;
    private TimelineViewport? _view;
    private TimelineViewport? _from;
    private TimelineViewport? _to;
    private TimelineViewport? _published;
    private SnapTargets? _targets;
    private Snapped? _guide;
    private IReadOnlyList<PlacedMarker>? _markers;
    private readonly List<Stack> _scopes = [];
    private readonly HashSet<HeatKind> _heatShown = [];
    private Dictionary<HeatKind, HeatLane>? _heat;
    private (LaneItem Item, long Ticks)? _lastPress;
    private List<LaneItem> _moving = [];
    private (double X, int Lane) _movePress;
    private double _moveDelta;
    private int _moveLanes;
    private bool _moveStarted;
    private bool _duplicate;
    private readonly Stopwatch _animation = new();
    private Drag _dragging;
    private double? _inPoint;
    private double? _outPoint;
    private double _boxFrom;
    private double _boxTo;
    private double _boxTop;
    private double _boxBottom;
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
        Selection.Changed += OnSelectionChanged;
        _surface.SelectRequested += OnSelectRequested;
        _surface.IsItemSelected = Selection.Contains;
        _surface.IsItemFocused = id => id == FocusedId;
    }

    /// <summary>WT52: the selected items of the shown timeline.</summary>
    public Selection Selection { get; } = new();

    /// <summary>WT52: selects everything, as Ctrl+A does.</summary>
    public void SelectAll()
    {
        if (_layout is { } layout)
        {
            Selection.All(layout);
        }
    }

    /// <summary>WT52: selects every item from the playhead on (A), or up to it (Shift+A).</summary>
    /// <param name="forward">Which way.</param>
    public void TrackSelect(bool forward)
    {
        if (_layout is { } layout)
        {
            Selection.TrackSelect(layout, Position.Seconds, forward);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _surface.Invalidate();
        _surface.ItemsChanged();
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

    /// <summary>WT53: whether drags snap to cuts, markers, the playhead and in/out; S toggles it, and Alt held inverts it for one drag.</summary>
    public bool Snapping
    {
        get;
        set
        {
            field = value;
            _surface.Invalidate();
        }
    } = true;

    /// <summary>
    /// WT53: the time under a point, snapped to the nearest cut, marker, playhead or in/out within
    /// eight pixels when snapping is on (Alt inverts it). What is being dragged is not its own
    /// target. Leaves the guide for the overlay to draw.
    /// </summary>
    private double SnapAt(TimelineViewport view, double x, SnapKind dragging)
    {
        double seconds = Math.Max(0, view.Seconds(x - Header));
        bool alt = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
        if (Snapping == alt || _layout is not { } layout)
        {
            _guide = null;
            return seconds;
        }

        _targets ??= SnapTargets.Of(layout);
        List<SnapTarget> moving = [];
        if (dragging != SnapKind.Playhead)
        {
            moving.Add(new SnapTarget(Position.Seconds, SnapKind.Playhead));
        }

        if (dragging != SnapKind.InOut && InPoint is { } a && OutPoint is { } b)
        {
            moving.Add(new SnapTarget(a, SnapKind.InOut));
            moving.Add(new SnapTarget(b, SnapKind.InOut));
        }

        Snapped snapped = _targets.Snap(seconds, view.PixelsPerSecond, Rate, moving);
        _guide = snapped.Target is null ? null : snapped;
        return snapped.Seconds;
    }

    /// <summary>WT54: every marker on the shown timeline, placed and sorted; empty with none shown.</summary>
    public IReadOnlyList<PlacedMarker> Markers => _markers ??= _layout is { } layout ? TimelineMarkers.Of(layout) : [];

    /// <summary>WT54: the timeline's own marker on the playhead's frame, or null.</summary>
    public PlacedMarker? MarkerAtPlayhead =>
        Markers.FirstOrDefault(m => m.Lane is null && Math.Abs(m.Start - Position.Seconds) < 0.5 / Rate);

    /// <summary>WT54: moves the playhead to the next marker, or the last one before it, as Shift+Down and Shift+Up do.</summary>
    /// <param name="forward">Which way.</param>
    public void JumpToMarker(bool forward)
    {
        if (TimelineMarkers.Beside(Markers, Position.Seconds, Rate, forward) is { } marker)
        {
            Position.GoTo(marker.Start + (0.5 / Rate));
        }
    }

    /// <summary>WT58: the id of the item the keyboard's focus is on, or null.</summary>
    public string? FocusedId { get; private set; }

    /// <summary>
    /// WT58: moves the keyboard's focus: across lanes (Ctrl+Up/Down) to the item nearest the one
    /// focused, or along its lane (Tab, Shift+Tab) to the next or previous item. With nothing
    /// focused yet it lands on the item under the playhead on the top lane that has one. False
    /// when there is nowhere to go, so Tab past the last item leaves the timeline as it should.
    /// </summary>
    /// <param name="lanes">Lanes down, up when negative.</param>
    /// <param name="items">Items along, back when negative.</param>
    /// <returns>Whether the focus moved.</returns>
    public bool MoveFocus(int lanes, int items)
    {
        if (_layout is not { } layout || layout.Lanes.All(l => !Focusable(l).Any()))
        {
            return false;
        }

        LaneItem? from = FocusedId is { } id ? layout.Find(id) : null;
        LaneItem? to;
        if (from is null)
        {
            Lane first = layout.Lanes.First(l => Focusable(l).Any());
            to = Nearest(first, Position.Seconds);
        }
        else if (lanes != 0)
        {
            int lane = from.Lane + Math.Sign(lanes);
            while (lane >= 0 && lane < layout.Lanes.Count && !Focusable(layout.Lanes[lane]).Any())
            {
                lane += Math.Sign(lanes);
            }

            to = lane >= 0 && lane < layout.Lanes.Count ? Nearest(layout.Lanes[lane], from.Start) : null;
        }
        else
        {
            List<LaneItem> along = [.. Focusable(layout.Lanes[from.Lane])];
            int at = along.IndexOf(from) + Math.Sign(items);
            to = at >= 0 && at < along.Count ? along[at] : null;
        }

        if (to is null)
        {
            return false;
        }

        FocusedId = layout.IdOf(to);
        if (_view is { } view && (to.End <= view.Start || to.Start >= view.End))
        {
            _view = view.Follow(to.Start, PlayheadFollow.Page, layout.Duration);
            Changed();
        }

        _surface.Invalidate();
        _surface.ItemsChanged();
        return true;
    }

    /// <summary>WT58: selects the focused item, as Space does; with Ctrl, adds or takes it away.</summary>
    /// <param name="toggle">Whether Ctrl was held.</param>
    public void SelectFocused(bool toggle)
    {
        if (_layout is { } layout && FocusedId is { } id && layout.Find(id) is { } item)
        {
            Selection.Click(layout, item, toggle ? SelectGesture.Toggle : SelectGesture.Replace);
        }
    }

    private static IEnumerable<LaneItem> Focusable(Lane lane) => lane.Items.Where(i => i.Kind is LaneItemKind.Clip or LaneItemKind.Nested);

    /// <summary>The lane's item that plays at a time, or else the one starting nearest it.</summary>
    private static LaneItem? Nearest(Lane lane, double seconds) =>
        Focusable(lane).FirstOrDefault(i => i.Start <= seconds && seconds < i.End)
        ?? Focusable(lane).OrderBy(i => Math.Abs(i.Start - seconds)).FirstOrDefault();

    /// <summary>WT57: the heat lanes shown over the overview strip.</summary>
    public IReadOnlySet<HeatKind> HeatShown => _heatShown;

    /// <summary>WT57: shows or hides a heat lane.</summary>
    /// <param name="kind">Which.</param>
    /// <param name="on">Whether it shows.</param>
    public void ShowHeat(HeatKind kind, bool on)
    {
        if (on ? _heatShown.Add(kind) : _heatShown.Remove(kind))
        {
            _surface.Invalidate();
        }
    }

    /// <summary>WT57: the lanes shown, counted for the strip's width; recounted after an edit.</summary>
    private Dictionary<HeatKind, HeatLane> Heat(TimelineLayout layout, int bins)
    {
        if (_heat is { } kept && kept.Values.All(l => l.Bins.Count == bins) && _heatShown.All(kept.ContainsKey))
        {
            return kept;
        }

        _heat = [];
        foreach (HeatKind kind in _heatShown)
        {
            _heat[kind] = HeatLane.Of(kind, HeatTimes(kind, layout), layout.Duration, bins);
        }

        return _heat;
    }

    private IEnumerable<double> HeatTimes(HeatKind kind, TimelineLayout layout) => kind switch
    {
        HeatKind.Cuts => EditPoints.Of(layout),
        HeatKind.Markers => Markers.Select(m => m.Start),
        _ => ProblemTimes(layout),
    };

    /// <summary>Where the validator's problems are: the start of the item each is on, where the layout shows it.</summary>
    private IEnumerable<double> ProblemTimes(TimelineLayout layout)
    {
        if (_timeline is null)
        {
            return [];
        }

        Dictionary<Composable, double> starts = new(ReferenceEqualityComparer.Instance);
        foreach (LaneItem item in layout.Lanes.SelectMany(l => l.Items))
        {
            starts.TryAdd(item.Item, item.Start);
        }

        return Model.Validation.Validator.Check(_timeline).Select(p => starts.TryGetValue(p.Target, out double at) ? at : -1).Where(at => at >= 0);
    }

    /// <summary>WT54: the frame rate the shown timeline counts in.</summary>
    public double FrameRate => Rate;

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
        _scopes.Clear();
        if (timeline is not null)
        {
            timeline.ModelChanged += OnModelChanged;
        }

        Reshow();
    }

    /// <summary>WT70: raised when a drag of clips is released, for the window to carry out in its history.</summary>
    public event EventHandler<MoveRequest>? MoveRequested;

    /// <summary>WT56: raised when a compound is opened or left, so the breadcrumb follows.</summary>
    public event EventHandler? ScopeChanged;

    /// <summary>WT56: the compound clips opened, outermost first; empty at the timeline itself.</summary>
    public IReadOnlyList<Stack> Scopes => _scopes;

    /// <summary>WT56: the stack shown: the innermost compound opened, or the timeline's own.</summary>
    public Stack? Shown => _scopes.Count > 0 ? _scopes[^1] : _timeline?.Tracks;

    /// <summary>WT56: shows a compound clip of the shown stack in its place, editable; <see cref="Up"/> comes back.</summary>
    /// <param name="compound">The compound.</param>
    public void Open(Stack compound)
    {
        ArgumentNullException.ThrowIfNull(compound);
        _scopes.Add(compound);
        Reshow();
    }

    /// <summary>WT56: leaves the compound shown for the one around it, as Esc does.</summary>
    /// <returns>Whether there was one to leave.</returns>
    public bool Up()
    {
        if (_scopes.Count == 0)
        {
            return false;
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        Reshow();
        return true;
    }

    /// <summary>Lays out the stack shown afresh, at its start, with nothing selected.</summary>
    private void Reshow()
    {
        _layout = Shown is { } stack ? TimelineLayout.Build(stack) : null;
        _view = null;
        _published = null;
        FocusedId = null;
        Selection.Clear();
        _targets = null;
        _markers = null;
        _heat = null;
        Position.Reset(Rate, _layout?.Duration ?? 0);
        ScopeChanged?.Invoke(this, EventArgs.Empty);
        Changed();
    }

    /// <summary>Whether a compound opened is still inside the timeline: an undo can take it out.</summary>
    private bool Within(Stack stack)
    {
        for (Composable? at = stack; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, _timeline?.Tracks))
            {
                return true;
            }
        }

        return false;
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
        _surface.SelectRequested -= OnSelectRequested;
        _surface.IsItemSelected = null;
        _surface.IsItemFocused = null;
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
            if (_scopes.Count > 0 && !Within(_scopes[^1]))
            {
                // The compound shown is gone (undone, broken apart): back out to what still holds it.
                while (_scopes.Count > 0 && !Within(_scopes[^1]))
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }

                Reshow();
                return;
            }

            _layout = TimelineLayout.Build(Shown!);
            Position.Lengthen(_layout.Duration);
            Selection.Keep(_layout);
            _targets = null;
            _markers = null;
            _heat = null;
            if (FocusedId is { } focused && _layout.Find(focused) is null)
            {
                FocusedId = null;
            }
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

            // WT160: the items exist from this frame on, and a client that listed none (or the last
            // timeline's) before it must be told, or a tab chosen just now reads the old reel.
            LanesMoved?.Invoke(this, EventArgs.Empty);
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
            DrawMarkers(view);
            _canvas.End();
        }

        // WT160: UI Automation lists what is in view, so a view it has not seen since it was made
        // (a timeline just shown, a resize) is published once drawn. Posted, because a peer
        // invalidated inside the render callback kept the old children; a zoom publishes as it ends.
        if (_from is null && !ReferenceEquals(view, _published))
        {
            _published = view;
            _surface.Dispatcher.BeginInvoke(DispatcherPriority.Background, _surface.ItemsChanged);
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

        // WT58: the keyboard's focus, a ring inside the focused item while the surface has the focus.
        if (FocusedId is { } focused && _surface.IsKeyboardFocusWithin && _layout.Find(focused) is { } item
            && item.End > view.Start && item.Start < view.End && item.Lane >= view.FirstLane && item.Lane <= view.LastLane)
        {
            (float fx, float fy, float fw, float fh) = Bounds(view, item);
            float ring = (float)Math.Max(2, 2 * Scale);
            _canvas.Outline(fx + ring, fy + ring, Math.Max(1, fw - (2 * ring)), Math.Max(1, fh - (2 * ring)), FocusRing, ring);
        }

        // WT53: the target a drag snapped to, and how many frames the snap moved it.
        if (_guide is { Target: { } snapped } guide)
        {
            float at = (float)(Header + view.X(snapped.Seconds));
            _canvas.Line(new Vector2(at, (float)Overview), new Vector2(at, target.Height), SnapGuide, (float)Math.Max(1, Scale));
            float size = (float)(10 * Scale);
            _canvas.Text(UiText.SnapDelta(guide.Frames), at + 4, (float)(Ruler + (2 * Scale)), (float)(80 * Scale), size, SnapGuide);
        }

        if (_dragging == Drag.ZoomBox)
        {
            float from = (float)(Header + Math.Min(_boxFrom, _boxTo)), span = (float)Math.Abs(_boxTo - _boxFrom);
            _canvas.Fill(from, (float)Ruler, span, target.Height - (float)Ruler, ZoomBoxFill);
            _canvas.Outline(from, (float)Ruler, span, target.Height - (float)Ruler, Label, 1);
        }
        else if (_dragging == Drag.Move && _moveStarted)
        {
            // WT70: where the dragged clips would land.
            foreach (LaneItem ghost in _moving)
            {
                int lane = ghost.Lane + _moveLanes;
                if (lane < view.FirstLane || lane > view.LastLane)
                {
                    continue;
                }

                float gx = (float)(Header + view.X(ghost.Start + _moveDelta)), gw = (float)((ghost.End - ghost.Start) * view.PixelsPerSecond);
                float gy = (float)(Ruler + view.Y(lane) + 1), gh = (float)(view.LaneHeight - 2);
                _canvas.Fill(gx, gy, Math.Max(1, gw), gh, ZoomBoxFill);
                _canvas.Outline(gx, gy, Math.Max(1, gw), gh, SelectedEdge, (float)Math.Max(1, Scale));
            }
        }
        else if (_dragging == Drag.Marquee)
        {
            float from = (float)(Header + Math.Min(_boxFrom, _boxTo)), top = (float)(Ruler + Math.Min(_boxTop, _boxBottom));
            float across = (float)Math.Abs(_boxTo - _boxFrom), down = (float)Math.Abs(_boxBottom - _boxTop);
            _canvas.Fill(from, top, across, down, ZoomBoxFill);
            _canvas.Outline(from, top, across, down, SelectedEdge, 1);
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
            if (pointer.Y < Overview && JumpToHotSpot(view, layout, pointer.X))
            {
                _dragging = Drag.None;
                return;
            }

            _dragging = Pressed(view, layout, pointer);
            if (_dragging == Drag.InOut)
            {
                _inPoint = _outPoint = SnapAt(view, pointer.X, SnapKind.InOut);
            }

            _boxFrom = _boxTo = Math.Max(0, pointer.X - Header);
            _boxTop = _boxBottom = pointer.Y - Ruler;
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
            case Drag.Marquee:
                _boxTo = Math.Max(0, pointer.X - Header);
                _boxBottom = pointer.Y - Ruler;
                if (pointer.Phase == PointerPhase.Up)
                {
                    _dragging = Drag.None;
                    bool add = pointer.Control || pointer.Shift;
                    if (Math.Abs(_boxTo - _boxFrom) > 4 || Math.Abs(_boxBottom - _boxTop) > 4)
                    {
                        Selection.Marquee(layout, view.Seconds(_boxFrom), view.Seconds(_boxTo), view.Lane(Math.Min(_boxTop, _boxBottom)), view.Lane(Math.Max(_boxTop, _boxBottom)), add);
                    }
                    else if (!add)
                    {
                        Selection.Clear();
                    }
                }

                _surface.InvalidateOverlay();
                break;
            case Drag.InOut:
                _outPoint = SnapAt(view, pointer.X, SnapKind.InOut);
                _surface.Invalidate();
                break;
            case Drag.Move:
                DragMove(view, layout, pointer);
                break;
            case Drag.Scrub when pointer.Phase == PointerPhase.Move && pointer.Pressed:
                // A scrub started on the ruler keeps scrubbing wherever the pointer goes down the lanes.
                OnClick(sender, new SurfacePoint(pointer.X, (int)Overview));
                break;
        }

        if (pointer.Phase == PointerPhase.Up)
        {
            _dragging = Drag.None;
            if (_guide is not null)
            {
                _guide = null;
                _surface.InvalidateOverlay();
            }
        }
    }

    /// <summary>
    /// What a press starts: over the overview strip, moving the view; over the ruler, scrubbing
    /// (with Shift, marking in and out); over the lanes with Z held, a zoom box; over an item, a
    /// selection by the click (Ctrl toggles, Shift extends); over empty lane space, a marquee.
    /// </summary>
    private Drag Pressed(TimelineViewport view, TimelineLayout layout, SurfacePointer pointer)
    {
        if (pointer.Y < Overview)
        {
            return Drag.Overview;
        }

        if (pointer.Y < Ruler)
        {
            return pointer.Shift ? Drag.InOut : Drag.Scrub;
        }

        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Z))
        {
            return Drag.ZoomBox;
        }

        if (pointer.X >= Header && layout.At(view.Seconds(pointer.X - Header), view.Lane(pointer.Y - Ruler)) is { Kind: not LaneItemKind.Gap } item)
        {
            // WT56: a second press on the same compound within the system's double-click time opens it.
            long now = Environment.TickCount64;
            if (item.Item is Stack compound && _lastPress is { } last && ReferenceEquals(last.Item.Item, item.Item)
                && now - last.Ticks <= GpuSurface.DoubleClickMilliseconds)
            {
                _lastPress = null;
                _surface.Dispatcher.BeginInvoke(() => Open(compound));
                return Drag.None;
            }

            _lastPress = (item, now);
            if (pointer.Control || pointer.Shift)
            {
                Selection.Click(layout, item, pointer.Control ? SelectGesture.Toggle : SelectGesture.Extend);
                return Drag.None;
            }

            // WT70: a press on a clip already selected keeps the selection, so all of it can be dragged.
            if (!Selection.Contains(layout.IdOf(item)))
            {
                Selection.Click(layout, item, SelectGesture.Replace);
            }

            _moving = [.. Selection.Ids.Select(layout.Find).OfType<LaneItem>().Where(i => i.Kind is LaneItemKind.Clip or LaneItemKind.Nested)];
            _movePress = (pointer.X, item.Lane);
            _moveDelta = 0;
            _moveLanes = 0;
            _moveStarted = false;
            _duplicate = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
            return _moving.Count > 0 ? Drag.Move : Drag.None;
        }

        return Drag.Marquee;
    }

    /// <summary>WT52: a UI Automation client selecting a drawn item through its pattern, as a click would.</summary>
    private void OnSelectRequested(object? sender, SurfaceSelect select)
    {
        if (_layout is not { } layout || layout.Find(select.AutomationId) is not { } item)
        {
            return;
        }

        bool selected = Selection.Contains(select.AutomationId);
        if (select.Mode == SurfaceSelectMode.Replace)
        {
            Selection.Click(layout, item, SelectGesture.Replace);
        }
        else if (selected != (select.Mode == SurfaceSelectMode.Add))
        {
            Selection.Click(layout, item, SelectGesture.Toggle);
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

        // WT57: each heat lane shown is a stripe along the strip's foot, its bins as hot as they are busy.
        if (_heatShown.Count > 0)
        {
            int bins = Math.Max(1, (int)(across / (4 * Scale)));
            float stripe = height / (_heatShown.Count + 1), at = top + height;
            float binWidth = across / bins;
            foreach (HeatLane lane in Heat(layout, bins).Values.OrderBy(l => l.Kind))
            {
                at -= stripe;
                Color4 hot = HeatColour(lane.Kind);
                for (int i = 0; i < lane.Bins.Count; i++)
                {
                    if (lane.Bins[i] > 0)
                    {
                        _canvas.Fill((float)Header + (i * binWidth), at, Math.Max(1, binWidth), stripe, new Color4(hot.R, hot.G, hot.B, 0.35f + (0.65f * lane.Bins[i])));
                    }
                }
            }
        }

        float left = (float)(Header + (view.Start * scale)), right = (float)(Header + (view.End * scale));
        _canvas.Outline(left, top + 0.5f, Math.Max(2, right - left), height - 1, Label, 1);
    }

    private static Color4 HeatColour(HeatKind kind) => kind switch
    {
        HeatKind.Cuts => new Color4(0.95f, 0.95f, 0.95f, 1f),
        HeatKind.Problems => new Color4(1f, 0.3f, 0.25f, 1f),
        _ => new Color4(1f, 0.85f, 0.2f, 1f),
    };

    /// <summary>WT57: a press on the overview strip within six pixels of something a heat lane counts jumps the playhead to it.</summary>
    private bool JumpToHotSpot(TimelineViewport view, TimelineLayout layout, double x)
    {
        if (_heat is not { Count: > 0 } heat)
        {
            return false;
        }

        double perPixel = layout.Duration / Math.Max(1, view.Width);
        double clicked = Math.Max(0, x - Header) * perPixel;
        double? best = null;
        foreach (HeatLane lane in heat.Values)
        {
            if (lane.Nearest(clicked, 6 * Scale * perPixel) is { } at && (best is null || Math.Abs(at - clicked) < Math.Abs(best.Value - clicked)))
            {
                best = at;
            }
        }

        if (best is not { } spot)
        {
            return false;
        }

        Seek(spot + (0.5 / Rate));
        return true;
    }

    private enum Drag
    {
        None,
        Scrub,
        Overview,
        ZoomBox,
        InOut,
        Marquee,
        Move,
    }

    /// <summary>
    /// WT70: a drag of the selected clips. Past four pixels a ghost follows the pointer, its
    /// leading edge snapped, across lanes too; the release asks for the move with the keys held
    /// then deciding the edit: none overwrites, Ctrl inserts, Ctrl+Alt swaps with the clip there.
    /// </summary>
    private void DragMove(TimelineViewport view, TimelineLayout layout, SurfacePointer pointer)
    {
        if (pointer.Phase == PointerPhase.Move && pointer.Pressed)
        {
            double dx = pointer.X - _movePress.X;
            int lanes = view.Lane(pointer.Y - Ruler) - _movePress.Lane;
            _moveStarted |= Math.Abs(dx) > 4 * Scale || lanes != 0;
            if (_moveStarted)
            {
                double first = _moving.Min(i => i.Start);
                double snapped = SnapAt(view, Header + view.X(first + (dx / view.PixelsPerSecond)), SnapKind.EditPoint);
                _moveDelta = Math.Round((snapped - first) * Rate) / Rate;
                _moveLanes = lanes;
                _surface.InvalidateOverlay();
            }

            return;
        }

        if (pointer.Phase != PointerPhase.Up || !_moveStarted)
        {
            return;
        }

        System.Windows.Input.ModifierKeys keys = System.Windows.Input.Keyboard.Modifiers;
        bool control = (keys & System.Windows.Input.ModifierKeys.Control) != 0, alt = (keys & System.Windows.Input.ModifierKeys.Alt) != 0;
        MoveMode mode = control && alt ? MoveMode.Swap : control ? MoveMode.Insert : MoveMode.Overwrite;
        LaneItem? onto = layout.At(view.Seconds(pointer.X - Header), view.Lane(pointer.Y - Ruler)) is { Kind: LaneItemKind.Clip or LaneItemKind.Nested } under ? under : null;
        MoveRequest request = new(_moving, _moveDelta, _moveLanes, mode, _duplicate && mode == MoveMode.Overwrite, onto);
        _moving = [];
        _moveStarted = false;
        _surface.InvalidateOverlay();
        MoveRequested?.Invoke(this, request);
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
            if (Selection.Ids.Count > 0 && Selection.Contains(layout.IdOf(item)))
            {
                float edge = (float)Math.Max(2, 2 * Scale);
                _canvas.Outline(x + (edge / 2), y + (edge / 2), w - edge, h - edge, SelectedEdge, edge);
            }
            else if (w >= 3)
            {
                _canvas.Outline(x + 0.5f, y + 0.5f, w - 1, h - 1, ClipEdge);
            }

            // The name stays at the left of the view while the clip runs off it.
            float left = Math.Max(x, (float)Header) + 4;

            // WT56: a compound clip carries two stacked layers before its name.
            if (item.Kind == LaneItemKind.Nested && w > 24 * Scale)
            {
                float s = (float)(6 * Scale), top = y + ((h - (s * 1.6f)) / 2);
                _canvas.Outline(left + (s * 0.5f), top, s, s, Label, 1);
                _canvas.Outline(left, top + (s * 0.6f), s, s, Label, 1);
                left += s * 2;
            }
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

    /// <summary>
    /// WT54: the timeline's own markers as flags along the foot of the ruler, and the markers of
    /// tracks and clips as ticks along the top of their lane; a ranged marker is a band as long as
    /// it is.
    /// </summary>
    private void DrawMarkers(TimelineViewport view)
    {
        foreach (PlacedMarker marker in Markers)
        {
            if (marker.End < view.Start || marker.Start > view.End || Place(view, marker) is not { } at)
            {
                continue;
            }

            (float r, float g, float b) = MarkerColors.Rgb(marker.Marker.Color);
            Color4 solid = new(r, g, b, 1f);
            if (at.Width > 2)
            {
                _canvas.Fill(at.X, at.Y, at.Width, at.Height, new Color4(r, g, b, 0.35f));
            }

            _canvas.Fill(at.X - (float)Scale, at.Y, (float)Math.Max(2, 2 * Scale), at.Height, solid);
        }
    }

    /// <summary>WT54: where a marker is drawn, in device pixels: on the ruler or the top of its lane; null when its lane is out of view.</summary>
    private (float X, float Y, float Width, float Height)? Place(TimelineViewport view, PlacedMarker marker)
    {
        float x = (float)(Header + view.X(marker.Start));
        float width = (float)(marker.Duration * view.PixelsPerSecond);
        if (marker.Lane is not { } lane)
        {
            float band = (float)(Ruler - Overview);
            return (x, (float)(Overview + (band * 0.6)), width, band * 0.4f);
        }

        if (lane < view.FirstLane || lane > view.LastLane)
        {
            return null;
        }

        return (x, (float)(Ruler + view.Y(lane) + 1), width, (float)Math.Max(4, view.LaneHeight * 0.22));
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
        string span = (_view is { } view ? ", " + UiText.ViewSpan(Timecode(view.Start), Timecode(view.End)) : string.Empty)
            + (FocusedId is { } focused && _layout?.Find(focused) is { } item ? ", " + UiText.FocusStatus(item.Name, _layout.Lanes[item.Lane].Label) : string.Empty)
            + ", " + UiText.RulerStatus(UiText.RulerUnitName(Unit));

        // WT57: the heat lanes shown and how much each counts, said only while there are some.
        if (_heat is { Count: > 0 } heat)
        {
            span += ", " + UiText.HeatStatus(heat.Values.OrderBy(l => l.Kind).Select(l => (UiText.HeatName(l.Kind), l.Count)));
        }
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

    // WT52: only over the ruler: a press on the lanes selects or starts a marquee.
    private void OnClick(object? sender, SurfacePoint point)
    {
        if (_view is not { } view || point.X < Header || point.Y < Overview || point.Y >= Ruler)
        {
            return;
        }

        // A click or a scrub lands where the pointer is, which is already in view: no follow.
        _scrubbing = true;
        try
        {
            Position.GoTo(SnapAt(view, point.X, SnapKind.Playhead));
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
            string id = layout.IdOf(item);
            items.Add(new SurfaceItem(
                item.Name,
                id,
                kind,
                UiText.ItemSpan(lane.Label, Timecode(item.Start), Timecode(item.End)),
                x, y, w, h,
                lane.Label,
                UiText.ItemReading(kind, Timecode(item.Start), Timecode(item.End), lane.IsAudio, lane.Label[1..])));
        }

        // WT54: the markers in view, read by name and by colour, lane and time.
        foreach (PlacedMarker marker in Markers)
        {
            if (marker.End < view.Start || marker.Start > view.End || Place(view, marker) is not { } at)
            {
                continue;
            }

            string? lane = marker.Lane is { } index ? layout.Lanes[index].Label : null;
            items.Add(new SurfaceItem(
                marker.Marker.Name.Length > 0 ? marker.Marker.Name : UiText.UnnamedMarker,
                marker.Id,
                "marker",
                UiText.MarkerStatus(marker.Marker.Color, Timecode(marker.Start), lane, (long)Math.Round(marker.Duration * Rate)),
                at.X - (2 * Scale), at.Y, Math.Max(4 * Scale, at.Width + (4 * Scale)), at.Height,
                lane));
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
