using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace WinTimeline.Render.Tests;

/// <summary>
/// WT4: the swap chain behind every GPU surface, on a hidden window and the WARP device, so it
/// runs on any desk: it presents, resizes in place, and tells a render loop when to draw.
/// </summary>
public sealed class SurfaceSwapChainTests
{
    [Fact]
    public void ItPresentsAFrameAtTheWindowsSize()
    {
        using GpuContext gpu = GpuContext.CreateWarp();
        using HiddenWindow window = new();
        using SurfaceSwapChain swapChain = new(gpu, window.Handle, 320, 180);

        gpu.ImmediateContext.OMSetRenderTargets(swapChain.Target);
        swapChain.Clear(new Color4(0.1f, 0.2f, 0.3f, 1f));
        swapChain.Present();

        Assert.Equal((320, 180), (swapChain.Width, swapChain.Height));
        Assert.Equal(1, swapChain.Presented);
        Assert.NotEqual(0, swapChain.FrameLatencyWaitableObject);
    }

    [Fact]
    public void ItResizesInPlaceAndIgnoresTheSameSize()
    {
        using GpuContext gpu = GpuContext.CreateWarp();
        using HiddenWindow window = new();
        using SurfaceSwapChain swapChain = new(gpu, window.Handle, 320, 180);

        Assert.False(swapChain.Resize(320, 180));
        Assert.True(swapChain.Resize(1280, 720));

        using ID3D11Resource back = swapChain.Target.Resource;
        using ID3D11Texture2D texture = back.QueryInterface<ID3D11Texture2D>();
        Assert.Equal((1280u, 720u), (texture.Description.Width, texture.Description.Height));
        swapChain.Present();
    }

    [Fact]
    public void AZeroSizeIsHeldAtOnePixel()
    {
        // A collapsed splitter pane: ResizeBuffers refuses zero, so the surface keeps one pixel.
        using GpuContext gpu = GpuContext.CreateWarp();
        using HiddenWindow window = new();
        using SurfaceSwapChain swapChain = new(gpu, window.Handle, 0, 0);

        swapChain.Resize(0, 40);

        Assert.Equal((1, 40), (swapChain.Width, swapChain.Height));
    }

    /// <summary>A plain window that is never shown: enough for DXGI to own a swap chain on.</summary>
    private sealed class HiddenWindow : IDisposable
    {
        public HiddenWindow()
        {
            Handle = CreateWindowExW(0, "STATIC", "wt4", 0, 0, 0, 64, 64, 0, 0, 0, 0);
            Assert.NotEqual(0, Handle);
        }

        public nint Handle { get; }

        public void Dispose() => DestroyWindow(Handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint ex, string cls, string? name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint hwnd);
    }
}
