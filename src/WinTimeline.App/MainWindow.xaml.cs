using System.Windows;
using WinTimeline.Render;

namespace WinTimeline.App;

/// <summary>
/// The main window. Each feature adds its own partial file (MainWindow.&lt;Feature&gt;.cs) rather
/// than growing this one, as in the sibling apps.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Opens the window for what the command line asked for.</summary>
    /// <param name="options">The parsed command line.</param>
    /// <param name="gpu">The app's one GPU device.</param>
    public MainWindow(AppOptions options, GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gpu);
        Options = options;
        Gpu = gpu;
        InitializeComponent();
        ShowGpu();
        BuildSurfaces();
    }

    /// <summary>What this run was started with.</summary>
    public AppOptions Options { get; }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
