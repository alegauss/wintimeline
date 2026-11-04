using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinTimeline.Render;

/// <summary>
/// A flip-model swap chain on the shared device, presenting into one window (WT4). Waitable, so
/// a render loop can block until the compositor will take a frame instead of queueing several
/// and adding their latency; resized in place with <c>ResizeBuffers</c>, so a drag of the
/// splitter never recreates it and never flickers.
/// </summary>
public sealed class SurfaceSwapChain : IDisposable
{
    private const SwapChainFlags Flags = SwapChainFlags.FrameLatencyWaitableObject;
    private readonly GpuContext _gpu;
    private readonly IDXGISwapChain2 _swapChain;
    private ID3D11RenderTargetView? _target;

    /// <summary>Creates the swap chain for a window.</summary>
    /// <param name="gpu">The shared device.</param>
    /// <param name="hwnd">The window it presents into.</param>
    /// <param name="width">Pixels across, at least 1.</param>
    /// <param name="height">Pixels down, at least 1.</param>
    public SurfaceSwapChain(GpuContext gpu, nint hwnd, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        _gpu = gpu;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        using IDXGIFactory2 factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        using IDXGISwapChain1 swapChain = factory.CreateSwapChainForHwnd(gpu.Device, hwnd, new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            AlphaMode = AlphaMode.Ignore,
            Flags = Flags,
        });

        // Alt+Enter would take the window full-screen behind WPF's back.
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        _swapChain = swapChain.QueryInterface<IDXGISwapChain2>();
        _swapChain.MaximumFrameLatency = 1;
        FrameLatencyWaitableObject = _swapChain.FrameLatencyWaitableObject;
        CreateTarget();
    }

    /// <summary>Pixels across.</summary>
    public int Width { get; private set; }

    /// <summary>Pixels down.</summary>
    public int Height { get; private set; }

    /// <summary>Frames presented since creation.</summary>
    public long Presented { get; private set; }

    /// <summary>Signalled when the compositor will take the next frame: what a render loop waits on.</summary>
    public nint FrameLatencyWaitableObject { get; }

    /// <summary>The back buffer's render target, valid until the next <see cref="Resize"/>.</summary>
    public ID3D11RenderTargetView Target => _target!;

    /// <summary>Follows the window's new size, keeping the swap chain.</summary>
    /// <param name="width">Pixels across.</param>
    /// <param name="height">Pixels down.</param>
    /// <returns>Whether the size changed.</returns>
    public bool Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
        {
            return false;
        }

        // Every reference to a back buffer must go before ResizeBuffers.
        _gpu.ImmediateContext.ClearState();
        _target?.Dispose();
        _target = null;
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, Flags).CheckError();
        (Width, Height) = (width, height);
        CreateTarget();
        return true;
    }

    /// <summary>The back buffer as a DXGI surface, for Direct2D to draw into; the caller disposes it.</summary>
    /// <returns>The surface.</returns>
    public IDXGISurface BackBuffer() => _swapChain.GetBuffer<IDXGISurface>(0);

    /// <summary>Clears the back buffer to a colour and presents it, the least a frame can be.</summary>
    /// <param name="color">The colour.</param>
    public void Clear(Color4 color) => _gpu.ImmediateContext.ClearRenderTargetView(Target, color);

    /// <summary>Presents the back buffer, synchronised to the display.</summary>
    public void Present()
    {
        _swapChain.Present(1, PresentFlags.None).CheckError();
        Presented++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _target?.Dispose();
        _swapChain.Dispose();
    }

    private void CreateTarget()
    {
        using ID3D11Texture2D back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _target = _gpu.Device.CreateRenderTargetView(back);
    }
}
