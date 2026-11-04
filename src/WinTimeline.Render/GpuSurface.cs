using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Mathematics;
using Rect = System.Windows.Rect;

namespace WinTimeline.Render;

/// <summary>
/// Where the GPU draws inside the WPF window (WT4): a child HWND hosting a
/// <see cref="SurfaceSwapChain"/> on the shared device. The viewer and the timeline are each one.
/// Generalised from Revelei's ImageSurface: what is drawn is the owner's, through <see cref="Draw"/>.
///
/// <para><b>Airspace</b>: nothing WPF draws can overlap the child window, so anything that sits
/// over the picture (the playhead, guides) is drawn inside the surface. Popups and tooltips are
/// windows of their own and are fine.</para>
/// </summary>
public sealed class GpuSurface : HwndHost
{
    private const string ClassName = "WinTimelineGpuSurface";
    private const uint WmEraseBkgnd = 0x0014;
    private static readonly Dictionary<nint, GpuSurface> Hosts = [];
    private static SurfaceProc? _procKeepAlive;

    private readonly GpuContext _gpu;
    private SurfaceSwapChain? _swapChain;
    private nint _child;
    private bool _dirty = true;
    private bool _overlayDirty;
    private bool _hooked;

    /// <summary>Creates a surface on the shared device.</summary>
    /// <param name="gpu">The app's one device.</param>
    public GpuSurface(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        _gpu = gpu;
        Focusable = true;
    }

    private delegate nint SurfaceProc(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>Raised once per frame with the swap chain to draw into; the surface presents afterwards.</summary>
    public event EventHandler<SurfaceSwapChain>? Draw;

    /// <summary>
    /// Raised for the mouse wheel over the surface, which the child window receives and WPF never
    /// sees (WT47). Coordinates are device pixels in the surface.
    /// </summary>
    public event EventHandler<SurfaceWheel>? Wheel;

    /// <summary>Raised for a left click on the surface, in device pixels.</summary>
    public event EventHandler<SurfacePoint>? Click;

    /// <summary>
    /// WT152: raised for every press, drag and release of the mouse or a pen over the surface, in
    /// device pixels; the surface captures the mouse from press to release, so a drag that leaves
    /// it still reports.
    /// </summary>
    public event EventHandler<SurfacePointer>? Pointer;

    /// <summary>WT49: raised for a right click on the surface, where a context menu belongs, in device pixels.</summary>
    public event EventHandler<SurfacePoint>? ContextRequested;

    /// <summary>WT152: raised when files are dropped on the surface from Explorer or another app.</summary>
    public event EventHandler<SurfaceDrop>? FilesDropped;

    /// <summary>
    /// WT152: raised after <see cref="Draw"/> on the same frame, for what sits over the picture
    /// (playhead, guides, safe areas): nothing WPF draws can sit over the child window.
    /// </summary>
    public event EventHandler<SurfaceSwapChain>? Overlay;

    /// <summary>
    /// What is drawn that UI Automation should see, since a swap chain publishes nothing: each
    /// item becomes a child of the surface's peer, so a UI case can find a clip by its name.
    /// </summary>
    public Func<IReadOnlyList<SurfaceItem>>? Items { get; set; }

    /// <summary>A line the surface's peer adds to its status, e.g. the frame time.</summary>
    public string? Status { get; set; }

    /// <summary>Tells UI Automation the items have changed; cheap, and batched by WPF.</summary>
    public void ItemsChanged() => UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();

    /// <summary>What the surface is cleared to before <see cref="Draw"/>.</summary>
    public Color4 Background { get; set; } = new(0.08f, 0.08f, 0.09f, 1f);

    /// <summary>Whether it draws every display frame (playback) or only when invalidated.</summary>
    public bool Continuous
    {
        get;
        set
        {
            field = value;
            Hook();
        }
    }

    /// <summary>The swap chain's size in device pixels, zero before the window exists.</summary>
    public (int Width, int Height) PixelSize => _swapChain is { } s ? (s.Width, s.Height) : (0, 0);

    /// <summary>Frames presented since the window was built.</summary>
    public long FramesPresented => _swapChain?.Presented ?? 0;

    /// <summary>Whether the device under it is the software rasteriser.</summary>
    public bool IsWarp => _gpu.IsWarp;

    /// <summary>Asks for one more frame.</summary>
    public void Invalidate()
    {
        _dirty = true;
        Hook();
    }

    /// <summary>
    /// WT51: asks for one more frame in which only the <see cref="Overlay"/> changed: the picture
    /// under it is the one last drawn, so <see cref="Draw"/> is not raised.
    /// </summary>
    public void InvalidateOverlay()
    {
        _overlayDirty = true;
        Hook();
    }

    /// <summary>WT51: how many frames raised <see cref="Draw"/>, as against those that only redrew the overlay.</summary>
    public long LayersDrawn { get; private set; }

    /// <inheritdoc/>
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureClass();
        const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;
        _child = CreateWindowExW(0, ClassName, null, WsChild | WsVisible | WsClipSiblings, 0, 0, 1, 1, hwndParent.Handle, 0, GetModuleHandleW(null), 0);
        if (_child == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }

        Hosts[_child] = this;
        DragAcceptFiles(_child, true);
        (int w, int h) = DevicePixels();
        _swapChain = new SurfaceSwapChain(_gpu, _child, w, h);
        Invalidate();
        return new HandleRef(this, _child);
    }

    /// <inheritdoc/>
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Unhook();
        Hosts.Remove(hwnd.Handle);
        _swapChain?.Dispose();
        _swapChain = null;
        DestroyWindow(hwnd.Handle);
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Follow();
    }

    /// <inheritdoc/>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Follow();
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);

    /// <summary>
    /// Leaves WM_GETOBJECT on the child window to Windows' default provider, as Revelei learned:
    /// HwndHost would answer through an interop child its own peer does not supply, and throw.
    /// </summary>
    /// <inheritdoc/>
    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmGetObject = 0x003D;
        if (msg == WmGetObject)
        {
            handled = true;
            return DefWindowProcW(hwnd, (uint)msg, wParam, lParam);
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>Resizes the buffers to the element's device pixels and redraws at once, so a resize never shows a stretched frame.</summary>
    private void Follow()
    {
        if (_swapChain is null)
        {
            return;
        }

        (int w, int h) = DevicePixels();
        if (_swapChain.Resize(w, h))
        {
            Render();
        }
    }

    private (int Width, int Height) DevicePixels()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        return (Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
    }

    private void Hook()
    {
        if (!_hooked && _swapChain is not null && (Continuous || _dirty || _overlayDirty))
        {
            CompositionTarget.Rendering += OnRendering;
            _hooked = true;
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (Continuous || _dirty || _overlayDirty)
        {
            Render();
        }

        if (!Continuous && !_dirty && !_overlayDirty)
        {
            Unhook();
        }
    }

    private void Render()
    {
        if (_swapChain is null)
        {
            return;
        }

        // WT51: when only the overlay changed, the frame under it is copied back from the kept
        // layer instead of drawn again, so a moving playhead never redraws the clips.
        bool full = _dirty || Continuous || Overlay is null || !_swapChain.RestoreLayer();
        _dirty = _overlayDirty = false;
        _gpu.ImmediateContext.OMSetRenderTargets(_swapChain.Target);
        if (full)
        {
            _swapChain.Clear(Background);
            Draw?.Invoke(this, _swapChain);
            LayersDrawn++;
            if (Overlay is not null && !Continuous)
            {
                _swapChain.KeepLayer();
            }
        }

        Overlay?.Invoke(this, _swapChain);
        _swapChain.Present();
    }

    private static void EnsureClass()
    {
        if (_procKeepAlive is not null)
        {
            return;
        }

        _procKeepAlive = (hwnd, msg, wParam, lParam) =>
            msg == WmEraseBkgnd ? 1
            : Hosts.TryGetValue(hwnd, out GpuSurface? host) && host.Input(hwnd, msg, wParam, lParam) ? 0
            : DefWindowProcW(hwnd, msg, wParam, lParam);
        WndClassEx description = new()
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_procKeepAlive),
            Instance = GetModuleHandleW(null),
            Cursor = LoadCursorW(0, 32512),
            ClassName = Marshal.StringToHGlobalUni(ClassName),
        };
        if (RegisterClassExW(ref description) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>
    /// Turns the child window's messages into the surface's events (WT152). A press gives the
    /// keyboard focus to this element in WPF rather than to the child window, so the window's
    /// key bindings keep working and Tab leaves the surface as it leaves any control.
    /// </summary>
    /// <returns>Whether the message was handled.</returns>
    private bool Input(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        (int X, int Y) ToClient((int X, int Y) screen)
        {
            Point32 point = new() { X = screen.X, Y = screen.Y };
            ScreenToClient(hwnd, ref point);
            return (point.X, point.Y);
        }

        if (SurfaceInput.Wheel(msg, wParam, lParam, ToClient) is { } wheel)
        {
            Wheel?.Invoke(this, wheel);
            return Wheel is not null;
        }

        if (SurfaceInput.Mouse(msg, wParam, lParam) is { } mouse)
        {
            if (mouse.Phase == PointerPhase.Down)
            {
                SetCapture(hwnd);
                Dispatcher.BeginInvoke(() => Focus());
                Click?.Invoke(this, new SurfacePoint(mouse.X, mouse.Y));
            }
            else if (mouse.Phase == PointerPhase.Up)
            {
                ReleaseCapture();
            }

            Pointer?.Invoke(this, mouse);
            return Pointer is not null || Click is not null;
        }

        if (msg is SurfaceInput.PointerDown or SurfaceInput.PointerUpdate or SurfaceInput.PointerUp)
        {
            // A pen reports pressure here; touch and the mouse fall through to their mouse messages.
            uint id = (uint)(wParam & 0xFFFF);
            if (!GetPointerType(id, out int type) || type != PenPointer || !GetPointerPenInfo(id, out PointerPenInfo pen)
                || SurfaceInput.Pen(msg, lParam, pen.Pressure, ToClient) is not { } stroke)
            {
                return false;
            }

            if (stroke.Phase == PointerPhase.Down)
            {
                Dispatcher.BeginInvoke(() => Focus());
            }

            Pointer?.Invoke(this, stroke);
            return Pointer is not null;
        }

        const uint RightUp = 0x0205;
        if (msg == RightUp && ContextRequested is not null)
        {
            ContextRequested(this, new SurfacePoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
            return true;
        }

        if (msg == SurfaceInput.DropFiles)
        {
            Dropped(wParam);
            return true;
        }

        return false;
    }

    private void Dropped(nint drop)
    {
        try
        {
            uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            List<string> paths = [];
            for (uint i = 0; i < count; i++)
            {
                char[] buffer = new char[DragQueryFileW(drop, i, null, 0) + 1];
                uint copied = DragQueryFileW(drop, i, buffer, (uint)buffer.Length);
                paths.Add(new string(buffer, 0, (int)copied));
            }

            DragQueryPoint(drop, out Point32 at);
            FilesDropped?.Invoke(this, new SurfaceDrop(paths, at.X, at.Y));
        }
        finally
        {
            DragFinish(drop);
        }
    }

    private const int PenPointer = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPenInfo
    {
        // POINTER_INFO, then the pen's own fields; only Pressure is read.
        public int PointerType;
        public uint PointerId;
        public uint FrameId;
        public int PointerFlags;
        public nint SourceDevice;
        public nint HwndTarget;
        public Point32 PixelLocation;
        public Point32 HimetricLocation;
        public Point32 PixelLocationRaw;
        public Point32 HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
        public uint PenFlags;
        public uint PenMask;
        public uint Pressure;
        public uint Rotation;
        public int TiltX;
        public int TiltY;
    }

    [DllImport("user32.dll")]
    private static extern bool GetPointerType(uint pointerId, out int type);

    [DllImport("user32.dll")]
    private static extern bool GetPointerPenInfo(uint pointerId, out PointerPenInfo info);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(nint hwnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint drop, uint index, char[]? file, uint length);

    [DllImport("shell32.dll")]
    private static extern bool DragQueryPoint(nint drop, out Point32 point);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(nint drop);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref Point32 point);


    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint IconSmall;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx description);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string? name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, nint name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    /// <summary>
    /// What the surface tells UI Automation, since a swap chain publishes nothing a case could
    /// read: a pane whose item status gives its size in device pixels, the frames presented and
    /// whether the device is hardware or WARP.
    /// </summary>
    private sealed class SurfacePeer(GpuSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

        protected override string GetClassNameCore() => nameof(GpuSurface);

        protected override string GetItemStatusCore()
        {
            (int w, int h) = owner.PixelSize;
            string status = string.Create(CultureInfo.InvariantCulture, $"{w} × {h} px, {owner.FramesPresented} frames, {(owner.IsWarp ? "WARP" : "hardware")}");
            return owner.Status is { } more ? $"{status}, {more}" : status;
        }

        protected override List<AutomationPeer>? GetChildrenCore()
        {
            IReadOnlyList<SurfaceItem>? items;
            try
            {
                items = owner.Items?.Invoke();
            }
#pragma warning disable CA1031 // An owner's bug must cost its items, not stall every UI Automation client waiting on this thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Diagnostics.Log.Write(Diagnostics.LogLevel.Error, Diagnostics.LogCategory.Render, "A surface's automation items could not be listed.", null, ex);
                return null;
            }

            return items is { Count: > 0 } ? [.. items.Select(item => (AutomationPeer)new SurfaceItemPeer(owner, item))] : null;
        }
    }

    /// <summary>One drawn item as UI Automation sees it: named, with a status and where it is on screen.</summary>
    private sealed class SurfaceItemPeer(GpuSurface owner, SurfaceItem item) : AutomationPeer
    {
        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetAutomationIdCore() => item.AutomationId;

        protected override Rect GetBoundingRectangleCore()
        {
            if (PresentationSource.FromVisual(owner) is null)
            {
                return Rect.Empty;
            }

            // PointToScreen answers in device pixels, as the item's bounds already are.
            Point origin = owner.PointToScreen(new Point(0, 0));
            return new Rect(origin.X + item.X, origin.Y + item.Y, Math.Max(0, item.Width), Math.Max(0, item.Height));
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override string GetClassNameCore() => "SurfaceItem";

        protected override Point GetClickablePointCore()
        {
            Rect bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        }

        protected override string GetHelpTextCore() => string.Empty;

        protected override string GetItemStatusCore() => item.Status;

        protected override string GetItemTypeCore() => item.Kind;

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override string GetLocalizedControlTypeCore() => "list item";

        protected override string GetNameCore() => item.Name;

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        public override object? GetPattern(PatternInterface patternInterface) => null;

        protected override bool HasKeyboardFocusCore() => false;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => false;

        protected override bool IsOffscreenCore() => false;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => false;

        protected override void SetFocusCore()
        {
        }

        protected override AutomationPeer GetPeerFromPointCore(Point point) => this;
    }
}

/// <summary>A mouse wheel turn over a surface.</summary>
/// <param name="X">Across, in device pixels.</param>
/// <param name="Y">Down, in device pixels.</param>
/// <param name="Delta">The turn, 120 to a notch; positive is away from the user, or right for a tilt.</param>
/// <param name="Horizontal">Whether it was a tilt.</param>
/// <param name="Shift">Whether Shift was down.</param>
/// <param name="Control">Whether Control was down.</param>
public sealed record SurfaceWheel(int X, int Y, int Delta, bool Horizontal, bool Shift, bool Control);

/// <summary>A point on a surface, in device pixels.</summary>
/// <param name="X">Across.</param>
/// <param name="Y">Down.</param>
public sealed record SurfacePoint(int X, int Y);

/// <summary>One thing drawn on a surface that UI Automation sees, in device pixels within the surface.</summary>
/// <param name="Name">Its name, e.g. a clip's.</param>
/// <param name="AutomationId">A stable identity, e.g. "V2-14".</param>
/// <param name="Kind">What it is, e.g. "clip".</param>
/// <param name="Status">What a case reads, e.g. where it plays.</param>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record SurfaceItem(string Name, string AutomationId, string Kind, string Status, double X, double Y, double Width, double Height);
