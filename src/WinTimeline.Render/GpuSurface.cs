using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Mathematics;

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
            msg == WmEraseBkgnd ? 1 : DefWindowProcW(hwnd, msg, wParam, lParam);
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
            return string.Create(CultureInfo.InvariantCulture, $"{w} × {h} px, {owner.FramesPresented} frames, {(owner.IsWarp ? "WARP" : "hardware")}");
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
