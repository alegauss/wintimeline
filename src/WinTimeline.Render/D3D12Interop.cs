using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinTimeline.Render;

/// <summary>
/// WT151: a D3D12 device on the D3D11 device's own adapter, for Windows ML and heavy compute,
/// which take D3D12 resources. A texture made shareable on the D3D11 device reaches it without a
/// copy: exported by NT handle and opened here, with a shared fence ordering the hand-off.
/// </summary>
public sealed class D3D12Interop : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly ID3D11Fence _fence11;
    private readonly ID3D12Fence _fence12;
    private ulong _value;

    private D3D12Interop(GpuContext gpu, ID3D12Device device, ID3D12CommandQueue queue, ID3D11Fence fence11, ID3D12Fence fence12)
    {
        _gpu = gpu;
        Device = device;
        Queue = queue;
        _fence11 = fence11;
        _fence12 = fence12;
    }

    /// <summary>The D3D12 device, on the same adapter as the D3D11 one.</summary>
    public ID3D12Device Device { get; }

    /// <summary>Its direct queue, which waits on the shared fence.</summary>
    public ID3D12CommandQueue Queue { get; }

    /// <summary>Opens a D3D12 device on the D3D11 device's adapter, with a fence the two share.</summary>
    /// <param name="gpu">The app's device.</param>
    /// <returns>The interop.</returns>
    /// <exception cref="NotSupportedException">The adapter has no D3D12, or the D3D11 device cannot share fences.</exception>
    public static D3D12Interop Create(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        using IDXGIDevice dxgi = gpu.Device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        if (D3D12.D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out ID3D12Device? device).Failure || device is null)
        {
            throw new NotSupportedException($"{gpu.Adapter.Name} has no Direct3D 12 device");
        }

        ID3D11Device5? device5 = gpu.Device.QueryInterfaceOrNull<ID3D11Device5>();
        if (device5 is null)
        {
            device.Dispose();
            throw new NotSupportedException("the Direct3D 11 device cannot share a fence (it needs 11.4)");
        }

        using (device5)
        {
            ID3D11Fence fence11 = device5.CreateFence(0, Vortice.Direct3D11.FenceFlags.Shared);
            nint handle = fence11.CreateSharedHandle(null, null!);
            try
            {
                ID3D12Fence fence12 = device.OpenSharedHandle<ID3D12Fence>(handle);
                ID3D12CommandQueue queue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
                return new D3D12Interop(gpu, device, queue, fence11, fence12);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>A texture on the D3D11 device that D3D12 can open: shared by NT handle, with no keyed mutex, which D3D12 lacks.</summary>
    /// <param name="width">Pixels across.</param>
    /// <param name="height">Pixels down.</param>
    /// <param name="format">Its format.</param>
    /// <param name="bind">How D3D11 uses it.</param>
    /// <returns>The texture; the caller disposes it.</returns>
    public ID3D11Texture2D CreateShareable(int width, int height, Format format, Vortice.Direct3D11.BindFlags bind) =>
        _gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = bind,
            MiscFlags = ResourceOptionFlags.Shared | ResourceOptionFlags.SharedNTHandle,
        });

    /// <summary>Opens a shareable D3D11 texture as a D3D12 resource: the same memory, no copy.</summary>
    /// <param name="texture">A texture from <see cref="CreateShareable"/>.</param>
    /// <returns>The D3D12 resource; the caller disposes it.</returns>
    public ID3D12Resource Open(ID3D11Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        using IDXGIResource1 resource = texture.QueryInterface<IDXGIResource1>();
        nint handle = resource.CreateSharedHandle(null, Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write, null);
        try
        {
            return Device.OpenSharedHandle<ID3D12Resource>(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Hands what the D3D11 device has queued to D3D12: D3D11 signals the shared fence, and the
    /// D3D12 queue waits on it before anything submitted after this call.
    /// </summary>
    public void HandToD3D12()
    {
        ulong value = ++_value;
        using ID3D11DeviceContext4 context = _gpu.ImmediateContext.QueryInterface<ID3D11DeviceContext4>();
        context.Signal(_fence11, value);
        context.Flush();
        Queue.Wait(_fence12, value);
    }

    /// <summary>Hands what D3D12 has submitted back: its queue signals, and the D3D11 device waits before its next work.</summary>
    public void HandToD3D11()
    {
        ulong value = ++_value;
        Queue.Signal(_fence12, value);
        using ID3D11DeviceContext4 context = _gpu.ImmediateContext.QueryInterface<ID3D11DeviceContext4>();
        context.Wait(_fence11, value);
    }

    /// <summary>Blocks until the D3D12 queue has finished everything submitted.</summary>
    public void Drain()
    {
        ulong value = ++_value;
        Queue.Signal(_fence12, value);
        // A kernel event: the fence sets the handle itself, which a ManualResetEventSlim never notices.
        using ManualResetEvent done = new(false);
        _fence12.SetEventOnCompletion(value, done.SafeWaitHandle.DangerousGetHandle()).CheckError();
        if (!done.WaitOne(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("the Direct3D 12 queue did not finish within 10 s");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Queue.Dispose();
        _fence12.Dispose();
        _fence11.Dispose();
        Device.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
