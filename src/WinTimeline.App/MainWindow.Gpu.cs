using WinTimeline.Render;

namespace WinTimeline.App;

/// <summary>WT3: the GPU the app runs on, named in the status bar.</summary>
public partial class MainWindow
{
    /// <summary>The app's one GPU device.</summary>
    public GpuContext Gpu { get; }

    /// <summary>What the status bar says about the device, e.g. "GPU: NVIDIA GeForce RTX 4060, 8 GB".</summary>
    /// <param name="adapter">The adapter the device runs on.</param>
    /// <returns>The text.</returns>
    public static string GpuText(GpuAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return "GPU: " + adapter.Label;
    }

    private void ShowGpu()
    {
        GpuStatus.Text = GpuText(Gpu.Adapter);
        GpuStatus.ToolTip = $"{Gpu.Adapter.Vendor}, driver {Gpu.Adapter.DriverVersion}, feature level {Gpu.FeatureLevel}"
                            + (Gpu.VideoSupport ? ", video decode" : ", no video decode");
    }
}
