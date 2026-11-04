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
    private bool _hooked;

    /// <summary>Creates a surface on the shared device.</summary>
    /// <param name="gpu">The app's one device.</param>
    public GpuSurface(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        _gpu = gpu;
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
        if (!_hooked && _swapChain is not null && (Continuous || _dirty))
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
        if (Continuous || _dirty)
        {
            Render();
        }

        if (!Continuous && !_dirty)
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

        _dirty = false;
        _gpu.ImmediateContext.OMSetRenderTargets(_swapChain.Target);
        _swapChain.Clear(Background);
        Draw?.Invoke(this, _swapChain);
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

    /// <summary>Turns the child window's mouse messages into the surface's events.</summary>
    /// <returns>Whether the message was handled.</returns>
    private bool Input(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WmMouseWheel = 0x020A, WmMouseHWheel = 0x020E, WmLButtonDown = 0x0201;
        const int MkShift = 0x0004, MkControl = 0x0008;
        switch (msg)
        {
            case WmMouseWheel or WmMouseHWheel when Wheel is not null:
                // Wheel messages carry screen coordinates; a click carries the window's own.
                Point32 screen = new() { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
                ScreenToClient(hwnd, ref screen);
                int keys = (int)(wParam & 0xFFFF);
                Wheel(this, new SurfaceWheel(screen.X, screen.Y, (short)((wParam >> 16) & 0xFFFF), msg == WmMouseHWheel, (keys & MkShift) != 0, (keys & MkControl) != 0));
                return true;
            case WmLButtonDown:
                SetFocus(hwnd);
                Click?.Invoke(this, new SurfacePoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
                return Click is not null;
            default:
                return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref Point32 point);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

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
/// <param name="AutomationId">A stable identity, e.g. "Clip:V2:14".</param>
/// <param name="Kind">What it is, e.g. "clip".</param>
/// <param name="Status">What a case reads, e.g. where it plays.</param>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record SurfaceItem(string Name, string AutomationId, string Kind, string Status, double X, double Y, double Width, double Height);
