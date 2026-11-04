using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinTimeline.Diagnostics;

namespace WinTimeline.Render;

/// <summary>
/// The application's one D3D11 device (WT3). Decoders, the compositor, the viewers and inference
/// all take it from here, so a frame decoded into a texture is composited and presented without
/// leaving the GPU. Created on the chosen adapter, falling back to WARP when no hardware device
/// opens, so a VM with no GPU still starts.
///
/// <para>The device has video support for DXVA decoding and is multithread-protected: the
/// decoder, the render loop and the UI thread all touch the immediate context.</para>
/// </summary>
public sealed class GpuContext : IDisposable
{
    private static readonly FeatureLevel[] Levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    private GpuContext(ID3D11Device1 device, ID3D11DeviceContext1 context, GpuAdapter adapter, bool isWarp, bool videoSupport)
    {
        Device = device;
        ImmediateContext = context;
        Adapter = adapter;
        IsWarp = isWarp;
        VideoSupport = videoSupport;

        using ID3D11Multithread multithread = context.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
    }

    /// <summary>The device every GPU resource in the app is created on.</summary>
    public ID3D11Device1 Device { get; }

    /// <summary>The immediate context, multithread-protected.</summary>
    public ID3D11DeviceContext1 ImmediateContext { get; }

    /// <summary>The adapter the device runs on.</summary>
    public GpuAdapter Adapter { get; }

    /// <summary>Whether the device is the WARP software rasteriser.</summary>
    public bool IsWarp { get; }

    /// <summary>Whether the device was created with video support, which DXVA decoding needs.</summary>
    public bool VideoSupport { get; }

    /// <summary>The feature level the device was created at.</summary>
    public FeatureLevel FeatureLevel => Device.FeatureLevel;

    /// <summary>
    /// Media Foundation's handle on this device, created on first use. Null when the device has no
    /// video support (WARP on some builds), so decoding falls back to system memory.
    /// </summary>
    public MediaDevice? Media
    {
        get
        {
            if (!VideoSupport)
            {
                return null;
            }

            lock (_mediaLock)
            {
                return _media ??= new MediaDevice(this);
            }
        }
    }

    private readonly Lock _mediaLock = new();
    private MediaDevice? _media;

    /// <summary>
    /// Creates the device on the chosen adapter, or on WARP when the choice names none or no
    /// hardware device opens on it.
    /// </summary>
    /// <param name="choice">What <see cref="AdapterChoice.Pick"/> resolved.</param>
    /// <returns>The device.</returns>
    public static GpuContext Create(AdapterChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (!choice.Ok)
        {
            throw new ArgumentException(choice.Error, nameof(choice));
        }

        if (choice.Adapter is { IsSoftware: false } wanted && TryHardware(wanted) is { } hardware)
        {
            return hardware;
        }

        if (choice.Adapter is { IsSoftware: false } missed)
        {
            Log.Write(LogLevel.Warning, LogCategory.Render, "No hardware device opened; falling back to WARP.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["adapter"] = missed.Name, ["driver"] = missed.DriverVersion });
        }

        return CreateWarp();
    }

    /// <summary>Creates a WARP device, as the tests and a GPU-less VM run on.</summary>
    /// <returns>The device.</returns>
    public static GpuContext CreateWarp()
    {
        // Video support first; WARP on some Windows builds refuses it, and then the device opens without.
        foreach (DeviceCreationFlags flags in (ReadOnlySpan<DeviceCreationFlags>)[DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, DeviceCreationFlags.BgraSupport])
        {
            if (D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, Levels, out ID3D11Device? device, out ID3D11DeviceContext? context).Success)
            {
                return Wrap(device!, context!, WarpAdapter(device!), isWarp: true, (flags & DeviceCreationFlags.VideoSupport) != 0);
            }
        }

        throw new InvalidOperationException("Neither a hardware device nor WARP could be created.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _media?.Dispose();
        ImmediateContext.ClearState();
        ImmediateContext.Flush();
        ImmediateContext.Dispose();
        Device.Dispose();
    }

    private static GpuContext? TryHardware(GpuAdapter wanted)
    {
        using IDXGIFactory6 factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
        if (factory.EnumAdapterByGpuPreference((uint)wanted.Index, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Failure)
        {
            return null;
        }

        using (adapter)
        {
            // Video support first; a GPU that cannot decode still renders on itself (WT155) rather
            // than falling back to WARP. A driver type of Unknown is what D3D11 requires with an adapter.
            foreach (DeviceCreationFlags flags in (ReadOnlySpan<DeviceCreationFlags>)[DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, DeviceCreationFlags.BgraSupport])
            {
                if (D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, flags, Levels, out ID3D11Device? device, out ID3D11DeviceContext? context).Success)
                {
                    return Wrap(device!, context!, GpuAdapters.Describe(wanted.Index, adapter!), isWarp: false, (flags & DeviceCreationFlags.VideoSupport) != 0);
                }
            }

            return null;
        }
    }

    private static GpuContext Wrap(ID3D11Device device, ID3D11DeviceContext context, GpuAdapter adapter, bool isWarp, bool videoSupport)
    {
        ID3D11Device1 device1 = device.QueryInterface<ID3D11Device1>();
        ID3D11DeviceContext1 context1 = context.QueryInterface<ID3D11DeviceContext1>();
        device.Dispose();
        context.Dispose();
        return new GpuContext(device1, context1, adapter, isWarp, videoSupport);
    }

    private static GpuAdapter WarpAdapter(ID3D11Device device)
    {
        using IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        using IDXGIAdapter1 adapter1 = adapter.QueryInterface<IDXGIAdapter1>();
        return GpuAdapters.Describe(-1, adapter1);
    }
}
