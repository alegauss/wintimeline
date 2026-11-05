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
    /// <param name="settings">The persisted settings.</param>
    /// <param name="notices">What went wrong at start and was recovered from, for the status bar.</param>
    /// <param name="previous">The last run, when it crashed.</param>
    public MainWindow(AppOptions options, GpuContext gpu, SettingsStore settings, IReadOnlyList<string> notices, PreviousCrash? previous = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notices);
        Options = options;
        Gpu = gpu;
        Settings = settings;
        InitializeComponent();
        if (notices.Count > 0)
        {
            StatusText.Text = string.Join(" ", notices);
        }

        ShowGpu();
        BuildSurfaces();
        StartJobs();
        StartBridge();
        ShowRecovery(previous);
        BindPalette();
        BindZoom();
        BindRuler();
        BindPlayhead();
        BindSelection();
        BindClipboard();
        BindMarkers();
        BindCompound();
        BindDocumentCommands();
        ShowWelcome();
    }

    /// <summary>What this run was started with.</summary>
    public AppOptions Options { get; }

    /// <summary>The persisted settings, which panels follow through <see cref="SettingsStore.Changed"/>.</summary>
    public SettingsStore Settings { get; }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
