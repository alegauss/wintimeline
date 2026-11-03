using System.Globalization;
using Vortice.DXGI;

namespace WinTimeline.Render;

/// <summary>A GPU as DXGI lists it.</summary>
/// <param name="Index">Its position in high-performance order, which is what <c>--adapter 0</c> names.</param>
/// <param name="Name">Its description, e.g. NVIDIA GeForce RTX 4060.</param>
/// <param name="VendorId">Its PCI vendor id (0x10DE NVIDIA, 0x8086 Intel, 0x1002 AMD).</param>
/// <param name="DeviceId">Its PCI device id.</param>
/// <param name="DedicatedVideoMemory">Bytes of its own video memory.</param>
/// <param name="SharedSystemMemory">Bytes of system memory it may borrow.</param>
/// <param name="DriverVersion">The user-mode driver version, e.g. 32.0.15.6094, or empty when DXGI does not say.</param>
/// <param name="IsSoftware">Whether it is WARP or the Basic Render Driver rather than hardware.</param>
/// <param name="Luid">The adapter's locally unique id, which D3D12 and Windows ML match a device by.</param>
public sealed record GpuAdapter(
    int Index,
    string Name,
    uint VendorId,
    uint DeviceId,
    long DedicatedVideoMemory,
    long SharedSystemMemory,
    string DriverVersion,
    bool IsSoftware,
    long Luid)
{
    /// <summary>The vendor's name, from the PCI id.</summary>
    public string Vendor => VendorId switch
    {
        0x10DE => "NVIDIA",
        0x8086 => "Intel",
        0x1002 or 0x1022 => "AMD",
        0x1414 => "Microsoft",
        0x5143 => "Qualcomm",
        _ => $"0x{VendorId:X4}",
    };

    /// <summary>The name and memory as the status bar shows them, e.g. "NVIDIA GeForce RTX 4060, 8 GB".</summary>
    public string Label => IsSoftware
        ? $"{Name} (software)"
        : $"{Name}, {Math.Round(DedicatedVideoMemory / (1024.0 * 1024 * 1024), 1).ToString(CultureInfo.InvariantCulture)} GB";
}

/// <summary>Lists the GPUs a device can be created on (WT3).</summary>
public static class GpuAdapters
{
    /// <summary>
    /// Every adapter in DXGI's high-performance order, so index 0 is the discrete GPU on a hybrid
    /// laptop. Software adapters are listed too, marked, because a VM may have nothing else.
    /// </summary>
    /// <returns>The adapters, empty when DXGI cannot be reached.</returns>
    public static IReadOnlyList<GpuAdapter> Enumerate()
    {
        List<GpuAdapter> found = [];
        if (DXGI.CreateDXGIFactory1(out IDXGIFactory6? factory).Failure || factory is null)
        {
            return found;
        }

        using (factory)
        {
            for (uint index = 0; factory.EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Success; index++)
            {
                using (adapter)
                {
                    found.Add(Describe((int)index, adapter!));
                }
            }
        }

        return found;
    }

    /// <summary>Reads what the app needs to know about one adapter.</summary>
    internal static GpuAdapter Describe(int index, IDXGIAdapter1 adapter)
    {
        AdapterDescription1 description = adapter.Description1;
        // Through nuint: PointerUSize's implicit conversion to uint overflows past 4 GB.
        nuint dedicated = description.DedicatedVideoMemory;
        nuint shared = description.SharedSystemMemory;
        return new GpuAdapter(
            index,
            description.Description,
            description.VendorId,
            description.DeviceId,
            (long)dedicated,
            (long)shared,
            DriverVersion(adapter),
            (description.Flags & AdapterFlags.Software) != 0,
            ((long)description.Luid.HighPart << 32) | description.Luid.LowPart);
    }

    /// <summary>The UMD version DXGI reports for the D3D11 interface: four 16-bit parts.</summary>
    private static string DriverVersion(IDXGIAdapter adapter)
    {
        if (adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out long umd).Failure)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}");
    }
}

/// <summary>Which adapter a run asked for, and which it got.</summary>
/// <param name="Adapter">The adapter to create the device on, or null for WARP.</param>
/// <param name="Error">Why the request named no adapter, when it did not.</param>
public sealed record AdapterChoice(GpuAdapter? Adapter, string? Error)
{
    /// <summary>The value of <c>--adapter</c> that asks for the software rasteriser.</summary>
    public const string Warp = "warp";

    /// <summary>Whether the request was understood.</summary>
    public bool Ok => Error is null;

    /// <summary>
    /// Resolves a request: none picks the first hardware adapter in high-performance order (WARP
    /// when there is none), digits pick by index, "warp" asks for the software rasteriser, and
    /// anything else is matched against the adapter names, ignoring case. A request that matches
    /// nothing, or more than one adapter, is refused with the list, never quietly replaced.
    /// </summary>
    /// <param name="adapters">What <see cref="GpuAdapters.Enumerate"/> found.</param>
    /// <param name="request">The <c>--adapter</c> value, or null.</param>
    /// <returns>The choice.</returns>
    public static AdapterChoice Pick(IReadOnlyList<GpuAdapter> adapters, string? request)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (request is null)
        {
            return new AdapterChoice(adapters.FirstOrDefault(a => !a.IsSoftware), null);
        }

        if (string.Equals(request, Warp, StringComparison.OrdinalIgnoreCase))
        {
            return new AdapterChoice(null, null);
        }

        if (int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            GpuAdapter? byIndex = adapters.FirstOrDefault(a => a.Index == index);
            return byIndex is null
                ? new AdapterChoice(null, $"--adapter {request}: there is no adapter {index}. {Listed(adapters)}")
                : new AdapterChoice(byIndex, null);
        }

        GpuAdapter[] matches = [.. adapters.Where(a => a.Name.Contains(request, StringComparison.OrdinalIgnoreCase))];
        return matches.Length switch
        {
            1 => new AdapterChoice(matches[0], null),
            0 => new AdapterChoice(null, $"--adapter {request} matches no adapter. {Listed(adapters)}"),
            _ => new AdapterChoice(null, $"--adapter {request} matches {matches.Length} adapters; give its index. {Listed(adapters)}"),
        };
    }

    private static string Listed(IReadOnlyList<GpuAdapter> adapters) =>
        adapters.Count == 0
            ? "This PC lists no adapter; use --adapter warp."
            : "This PC has: " + string.Join("; ", adapters.Select(a => $"{a.Index} {a.Name}")) + ", or warp.";
}
