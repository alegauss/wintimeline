using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WinTimeline.Render;

/// <summary>
/// WT151: tells the caches when the OS changes how much video memory this process may use, so
/// they shrink before the driver starts evicting. Registered with
/// IDXGIAdapter3::RegisterVideoMemoryBudgetChangeNotificationEvent; a worker waits on the event
/// and raises <see cref="BudgetChanged"/> with the usage and budget it then reads.
/// </summary>
public sealed class VideoMemoryWatch : IDisposable
{
    private readonly IDXGIAdapter3 _adapter;
    private readonly AutoResetEvent _changed = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly int _cookie;
    private readonly Thread _worker;

    /// <summary>Starts watching the device's adapter.</summary>
    /// <param name="gpu">The app's device.</param>
    /// <exception cref="NotSupportedException">The adapter does not report a budget.</exception>
    public VideoMemoryWatch(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        using IDXGIDevice dxgi = gpu.Device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        _adapter = adapter.QueryInterfaceOrNull<IDXGIAdapter3>() ?? throw new NotSupportedException($"{gpu.Adapter.Name} does not report a video memory budget");
        _cookie = _adapter.RegisterVideoMemoryBudgetChangeNotificationEvent(_changed.SafeWaitHandle.DangerousGetHandle());
        _worker = new Thread(Run) { IsBackground = true, Name = "VRAM budget" };
        _worker.Start();
    }

    /// <summary>Raised, on the watch's thread, when the budget changes, with what is in use and the new budget.</summary>
    public event EventHandler<(long InUse, long Budget)>? BudgetChanged;

    /// <summary>What is in use and the budget now, local memory.</summary>
    public (long InUse, long Budget) Now
    {
        get
        {
            QueryVideoMemoryInfo info = _adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            return ((long)info.CurrentUsage, (long)info.Budget);
        }
    }

    /// <summary>Whether the process is past a share of its budget, the point at which caches should shrink.</summary>
    /// <param name="share">e.g. 0.9.</param>
    /// <returns>True when over.</returns>
    public bool Over(double share)
    {
        (long inUse, long budget) = Now;
        return budget > 0 && inUse > budget * share;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Set();
        _worker.Join(TimeSpan.FromSeconds(2));
        _adapter.UnregisterVideoMemoryBudgetChangeNotification(_cookie);
        _adapter.Dispose();
        _changed.Dispose();
        _stop.Dispose();
    }

    private void Run()
    {
        WaitHandle[] handles = [_stop, _changed];
        while (WaitHandle.WaitAny(handles) == 1)
        {
            BudgetChanged?.Invoke(this, Now);
        }
    }
}

/// <summary>A video codec the GPU decodes in hardware, as the media layer asks before opening a file.</summary>
/// <param name="Name">e.g. "HEVC Main10".</param>
/// <param name="Profile">The D3D11 decoder profile GUID.</param>
public sealed record DecodeProfile(string Name, Guid Profile);

/// <summary>WT151: the hardware decode profiles a device supports, by name, so the media layer knows a file decodes on the GPU before it opens it.</summary>
public static class DecodeProfiles
{
    private static readonly Dictionary<Guid, string> Known = new()
    {
        [new Guid("1b81be68-a0c7-11d3-b984-00c04f2e73c5")] = "H.264",
        [new Guid("5b11d51b-2f4c-4452-bcc3-09f2a1160cc0")] = "HEVC Main",
        [new Guid("107af0e0-ef1a-4d19-aba8-67a163073d13")] = "HEVC Main10",
        [new Guid("463707f8-a1d0-4585-876d-83aa6d60b89e")] = "VP9",
        [new Guid("a4c749ef-6ecf-48aa-8448-50a7a1165ff7")] = "VP9 10-bit",
        [new Guid("b8be4ccb-cf53-46ba-8d59-d6b8a6da5d2a")] = "AV1 Main",
        [new Guid("ee27417f-5e28-4e65-beea-1d26b508adc9")] = "MPEG-2",
    };

    /// <summary>What the device decodes in hardware, known codecs by name; empty without video support.</summary>
    /// <param name="gpu">The device.</param>
    /// <returns>The profiles, sorted by name.</returns>
    public static IReadOnlyList<DecodeProfile> Of(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        using ID3D11VideoDevice? video = gpu.Device.QueryInterfaceOrNull<ID3D11VideoDevice>();
        if (video is null)
        {
            return [];
        }

        List<DecodeProfile> found = [];
        uint count = video.VideoDecoderProfileCount;
        for (uint i = 0; i < count; i++)
        {
            Guid profile = video.GetVideoDecoderProfile(i);
            if (Known.TryGetValue(profile, out string? name) && found.All(f => f.Name != name))
            {
                found.Add(new DecodeProfile(name, profile));
            }
        }

        return [.. found.OrderBy(f => f.Name, StringComparer.Ordinal)];
    }
}
