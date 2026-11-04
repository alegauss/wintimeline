using System.Globalization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Vortice.DXGI;
using WinTimeline.Render;
using WinML = Microsoft.Windows.AI.MachineLearning;

namespace WinTimeline.Inference;

/// <summary>Where an ONNX model runs.</summary>
public enum ExecutionProvider
{
    /// <summary>ONNX Runtime's CPU provider, always there.</summary>
    Cpu,

    /// <summary>DirectML on a DirectX 12 GPU, which Windows ML's ONNX Runtime carries.</summary>
    DirectML,

    /// <summary>A provider Windows ML installed for this PC, e.g. TensorRT-RTX, OpenVINO or QNN for an NPU.</summary>
    WindowsML,
}

/// <summary>What the user or <c>--provider</c> asked for.</summary>
public enum ProviderPreference
{
    /// <summary>DirectML on the app's GPU, then CPU: needs nothing from Windows Update.</summary>
    Auto,

    /// <summary>A Windows ML provider on the app's GPU or NPU, else what Auto picks.</summary>
    WindowsML,

    /// <summary>CPU, without trying a GPU.</summary>
    Cpu,
}

/// <summary>The provider chosen, and why a GPU was not, if it was not.</summary>
/// <param name="Provider">Where every session runs.</param>
/// <param name="Adapter">The GPU it runs on, when it runs on one.</param>
/// <param name="Fallback">Why a GPU was asked for and not used, or null.</param>
/// <param name="Accelerator">The Windows ML provider's ONNX Runtime name, for <see cref="ExecutionProvider.WindowsML"/>.</param>
public sealed record ProviderSelection(ExecutionProvider Provider, GpuAdapter? Adapter, string? Fallback, string? Accelerator = null)
{
    /// <summary>The provider as the log and the status bar name it: DirectML, Cpu, or e.g. TensorRT-RTX.</summary>
    public string Name => Provider == ExecutionProvider.WindowsML && Accelerator is not null ? WindowsMLDevice.ShortName(Accelerator) : Provider.ToString();

    /// <summary>What the status bar shows, e.g. "DirectML · NVIDIA GeForce RTX 4060, 8 GB".</summary>
    public string Label => Provider switch
    {
        ExecutionProvider.Cpu => Fallback is null ? "CPU" : $"CPU (GPU unavailable: {Fallback})",
        _ when Adapter is not null => $"{Name} · {Adapter.Label}",
        _ => Name,
    };
}

/// <summary>Runs the probe model on one provider.</summary>
public interface IProviderProbe
{
    /// <summary>Tries a provider.</summary>
    /// <param name="candidate">The provider and its GPU.</param>
    /// <param name="error">Why it failed, when it did.</param>
    /// <returns>Whether a session ran the probe model.</returns>
    bool TryRun(ProviderSelection candidate, out string? error);
}

/// <summary>
/// WT99: picks where ONNX models run, once at start, following the app's GPU choice (WT3) so the
/// models and the editor share one adapter. Ported from Revelei's PB14 and PB43. A provider is
/// chosen only after the probe model ran on it; a GPU that will not run is a fallback with its
/// reason, never an exception.
/// </summary>
public static class ProviderDetector
{
    /// <summary>Detects on the app's GPU, with the Windows ML providers already on disk and the real probe.</summary>
    /// <param name="preference">What was asked for.</param>
    /// <param name="adapter">The GPU the app runs on, or null for none.</param>
    /// <returns>The selection.</returns>
    public static ProviderSelection Detect(ProviderPreference preference, GpuAdapter? adapter) =>
        Detect(preference, adapter, preference == ProviderPreference.WindowsML ? WindowsMLProviders.Devices() : [], new OnnxRuntimeProbe());

    /// <summary>Detects against the given GPU, Windows ML devices and probe.</summary>
    /// <param name="preference">What was asked for.</param>
    /// <param name="adapter">The app's GPU, or null.</param>
    /// <param name="accelerators">The Windows ML providers registered with ONNX Runtime, best first.</param>
    /// <param name="probe">How a provider is tried.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="InvalidOperationException">Not even the CPU runs the probe: ONNX Runtime is broken.</exception>
    public static ProviderSelection Detect(ProviderPreference preference, GpuAdapter? adapter, IReadOnlyList<WindowsMLDevice> accelerators, IProviderProbe probe)
    {
        ArgumentNullException.ThrowIfNull(accelerators);
        ArgumentNullException.ThrowIfNull(probe);
        string? fallback = null;
        if (preference == ProviderPreference.WindowsML)
        {
            // The app's GPU first, then an NPU or another device Windows ML offers.
            foreach (WindowsMLDevice device in accelerators.OrderBy(d => adapter is not null && d.Matches(adapter) ? 0 : 1))
            {
                ProviderSelection candidate = new(ExecutionProvider.WindowsML, adapter is not null && device.Matches(adapter) ? adapter : null, null, device.EpName);
                if (probe.TryRun(candidate, out _))
                {
                    return candidate;
                }
            }
        }

        if (preference != ProviderPreference.Cpu)
        {
            if (adapter is null || adapter.IsSoftware)
            {
                fallback = adapter is null ? "no GPU" : "the app runs on WARP";
            }
            else
            {
                ProviderSelection directML = new(ExecutionProvider.DirectML, adapter, null);
                if (probe.TryRun(directML, out string? error))
                {
                    return directML;
                }

                fallback = error ?? "DirectML did not run";
            }
        }

        return probe.TryRun(new ProviderSelection(ExecutionProvider.Cpu, null, null), out string? cpuError)
            ? new ProviderSelection(ExecutionProvider.Cpu, null, fallback)
            : throw new InvalidOperationException($"ONNX Runtime cannot run on the CPU: {cpuError}");
    }

    /// <summary>Reads a <c>--provider</c> value: auto, winml or cpu.</summary>
    /// <param name="value">The flag's value.</param>
    /// <param name="preference">The preference it names.</param>
    /// <returns>Whether the value named one.</returns>
    public static bool TryParse(string? value, out ProviderPreference preference)
    {
        preference = value?.Trim().ToUpperInvariant() switch
        {
            "AUTO" or "DML" or "DIRECTML" or "GPU" => ProviderPreference.Auto,
            "WINML" or "WINDOWSML" => ProviderPreference.WindowsML,
            "CPU" => ProviderPreference.Cpu,
            _ => (ProviderPreference)(-1),
        };
        return Enum.IsDefined(preference);
    }
}

/// <summary>Builds the options every session uses for a selection.</summary>
public static class SessionOptionsFactory
{
    /// <summary>Options for the selected provider; the caller disposes them.</summary>
    /// <param name="selection">The provider and its GPU.</param>
    /// <returns>The options.</returns>
    public static SessionOptions Create(ProviderSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        SessionOptions options = new();
        try
        {
            switch (selection.Provider)
            {
                case ExecutionProvider.DirectML:
                    // DirectML runs neither with memory patterns nor with parallel execution.
                    options.EnableMemoryPattern = false;
                    options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                    options.AppendExecutionProvider_DML(DxgiIndex(selection.Adapter));
                    break;
                case ExecutionProvider.WindowsML:
                    WindowsMLProviders.Append(options, selection);
                    break;
            }

            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    /// <summary>
    /// DirectML numbers adapters as EnumAdapters1 does, not in the high-performance order the app
    /// lists them in, so the app's adapter is found by its LUID.
    /// </summary>
    private static int DxgiIndex(GpuAdapter? adapter)
    {
        if (adapter is null || DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            return 0;
        }

        using (factory)
        {
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? candidate).Success; i++)
            {
                using (candidate)
                {
                    if (candidate!.Description1.Luid == adapter.Luid)
                    {
                        return (int)i;
                    }
                }
            }
        }

        return 0;
    }
}

/// <summary>Tries a provider by running the probe model through a real session.</summary>
public sealed class OnnxRuntimeProbe : IProviderProbe
{
    private static readonly byte[] Model = ProbeModel.Build();

    /// <inheritdoc/>
    public bool TryRun(ProviderSelection candidate, out string? error)
    {
        // The append is inside the try: Revelei's spike saw it throw 887A0004 on a GPU that worked.
        try
        {
            using SessionOptions options = SessionOptionsFactory.Create(candidate);
            using InferenceSession session = new(Model, options);
            DenseTensor<float> input = new([1, 3, 16, 16]);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                session.Run([NamedOnnxValue.CreateFromTensor(session.InputNames[0], input)]);
            error = null;
            return results.Count == 1;
        }
        catch (Exception failure) when (failure is OnnxRuntimeException or DllNotFoundException or InvalidOperationException or EntryPointNotFoundException)
        {
            error = failure.Message;
            return false;
        }
    }
}

/// <summary>A provider Windows ML registered with ONNX Runtime, on one device.</summary>
/// <param name="EpName">Its ONNX Runtime name, e.g. NvTensorRTRTXExecutionProvider.</param>
/// <param name="VendorId">The PCI vendor id of the device it runs on.</param>
/// <param name="Luid">The device's LUID, when ONNX Runtime's metadata gives it.</param>
/// <param name="IsNpu">Whether it runs on an NPU rather than a GPU.</param>
public sealed record WindowsMLDevice(string EpName, uint VendorId, long? Luid = null, bool IsNpu = false)
{
    /// <summary>The short name, e.g. TensorRT-RTX.</summary>
    /// <param name="epName">The ONNX Runtime name.</param>
    /// <returns>The short name.</returns>
    public static string ShortName(string epName)
    {
        ArgumentNullException.ThrowIfNull(epName);
        return epName switch
        {
            "NvTensorRTRTXExecutionProvider" => "TensorRT-RTX",
            "OpenVINOExecutionProvider" => "OpenVINO",
            _ when epName.EndsWith("ExecutionProvider", StringComparison.Ordinal) => epName[..^"ExecutionProvider".Length],
            _ => epName,
        };
    }

    /// <summary>Whether this device is the given GPU: by LUID when known, else by vendor.</summary>
    /// <param name="adapter">A DXGI adapter.</param>
    /// <returns>True for it.</returns>
    public bool Matches(GpuAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return !IsNpu && (Luid is { } luid ? adapter.Luid == luid : adapter.VendorId == VendorId);
    }

    internal static WindowsMLDevice From(OrtEpDevice device)
    {
        OrtHardwareDevice hardware = device.HardwareDevice;
        long? luid = hardware.Metadata.Entries.TryGetValue("LUID", out string? text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
        return new WindowsMLDevice(device.EpName, hardware.VendorId, luid, hardware.Type == OrtHardwareDeviceType.NPU);
    }
}

/// <summary>
/// The execution providers Windows ML installs for this PC. Start only registers what is already
/// on disk; fetching through Windows Update is the model manager's (WT100). Everything here
/// answers "none" rather than throwing, so an older Windows leaves DirectML and CPU as they were.
/// </summary>
public static class WindowsMLProviders
{
    private static readonly string[] Ranked = ["NvTensorRTRTXExecutionProvider", "OpenVINOExecutionProvider", "QNNExecutionProvider"];
    private static readonly string[] BuiltIn = ["CPUExecutionProvider", "DmlExecutionProvider"];
    private static readonly Lock Gate = new();
    private static bool _registered;

    /// <summary>Registers the certified providers on disk, once per process, and lists the GPU and NPU ones, best first.</summary>
    /// <returns>The devices; empty when Windows ML has none or cannot be reached.</returns>
    public static IReadOnlyList<WindowsMLDevice> Devices()
    {
        try
        {
            lock (Gate)
            {
                if (!_registered)
                {
                    WinML.ExecutionProviderCatalog.GetDefault().RegisterCertifiedAsync().AsTask().GetAwaiter().GetResult();
                    _registered = true;
                }
            }

            return Rank(OrtEnv.Instance().GetEpDevices()
                .Where(d => d.HardwareDevice.Type is OrtHardwareDeviceType.GPU or OrtHardwareDeviceType.NPU)
                .Select(WindowsMLDevice.From));
        }
#pragma warning disable CA1031 // Windows ML missing or failing leaves DirectML and CPU; it is never a reason not to start.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    /// <summary>Drops the built-in providers and plugin variants, and orders the rest best first.</summary>
    /// <param name="devices">What ONNX Runtime lists.</param>
    /// <returns>The candidates.</returns>
    public static IReadOnlyList<WindowsMLDevice> Rank(IEnumerable<WindowsMLDevice> devices) =>
        [.. devices
            .Where(d => !BuiltIn.Contains(d.EpName, StringComparer.Ordinal) && !d.EpName.Contains('.', StringComparison.Ordinal))
            .Distinct()
            .OrderBy(d => Array.IndexOf(Ranked, d.EpName) is int rank and >= 0 ? rank : Ranked.Length)
            .ThenBy(d => d.EpName, StringComparer.Ordinal)];

    internal static void Append(SessionOptions options, ProviderSelection selection)
    {
        OrtEnv env = OrtEnv.Instance();
        List<OrtEpDevice> devices = [.. env.GetEpDevices().Where(d =>
            d.EpName == selection.Accelerator
            && (selection.Adapter is null || WindowsMLDevice.From(d).Matches(selection.Adapter)))];
        if (devices.Count == 0)
        {
            throw new InvalidOperationException($"{selection.Name} is not registered with ONNX Runtime.");
        }

        options.AppendExecutionProvider(env, devices, new Dictionary<string, string>());
    }
}
