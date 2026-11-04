using System.Windows;
using WinTimeline.Diagnostics;
using WinTimeline.Render;

namespace WinTimeline.App;

/// <summary>The application: parses the command line and opens the main window with it.</summary>
public partial class App : Application
{
    /// <summary>The exit code for a command line that was refused.</summary>
    public const int BadCommandLine = 2;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        ParsedCommandLine parsed = CommandLine.Parse(e.Args);
        if (!parsed.Ok)
        {
            MessageBox.Show(UiText.CommandLineRefused(parsed.Error), UiText.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
        }

        // WT6: the log first, so everything after it can be recorded.
        Log.Shared = new Log(LogFolder) { MinimumLevel = parsed.Options!.LogLevel };
        Log.Write(LogLevel.Info, LogCategory.App, "WinTimeline started.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["version"] = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
                ["os"] = Environment.OSVersion.VersionString,
                ["runtime"] = Environment.Version.ToString(),
            });

        // WT7: every failure path is caught or dumped, and a crash of the last run is noticed.
        CrashHandling.Install(this, CrashFolder);
        _session = SessionMarker.Begin(SettingsStore.DataFolder, CrashFolder, out PreviousCrash? previous);
        if (previous is not null)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "The last run did not close cleanly.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["started"] = previous.Started.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ["dump"] = previous.Dump ?? "none",
                });
        }

        // WT5: the settings, which a damaged file never stops; the window says when it happened.
        SettingsStore settings = SettingsStore.Load(SettingsStore.DefaultPath);
        List<string> notices = settings.Notice is { } notice ? [notice] : [];

        // WT3: the one GPU device, on the adapter --adapter names, else the saved one, else the
        // high-performance one. A bad --adapter is refused; a saved GPU that is gone is said and passed over.
        IReadOnlyList<GpuAdapter> adapters = GpuAdapters.Enumerate();
        AdapterChoice choice = AdapterChoice.Pick(adapters, parsed.Options!.Adapter ?? settings.Current.Adapter);
        if (!choice.Ok && parsed.Options.Adapter is not null)
        {
            MessageBox.Show(UiText.CommandLineRefused(choice.Error), UiText.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
        }

        if (!choice.Ok)
        {
            notices.Add(UiText.SavedGpuGone(settings.Current.Adapter));
            choice = AdapterChoice.Pick(adapters, null);
        }

        _gpu = GpuContext.Create(choice);
        foreach (string recovered in notices)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, recovered);
        }

        Log.Write(LogLevel.Info, LogCategory.Render, "GPU device created.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["adapter"] = _gpu.Adapter.Name,
                ["vendor"] = _gpu.Adapter.Vendor,
                ["driver"] = _gpu.Adapter.DriverVersion,
                ["feature_level"] = _gpu.FeatureLevel.ToString(),
                ["warp"] = _gpu.IsWarp ? "true" : "false",
            });

        MainWindow window = new(parsed.Options!, _gpu, settings, notices, previous);
        MainWindow = window;
        window.Show();

        if (parsed.Options.CrashTest is { } crash)
        {
            Dispatcher.BeginInvoke(() => CrashHandling.Crash(crash), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>logs in the data folder.</summary>
    public static string LogFolder => System.IO.Path.Combine(SettingsStore.DataFolder, "logs");

    /// <summary>crashes in the data folder, where the crash handler (WT7) writes dumps.</summary>
    public static string CrashFolder => System.IO.Path.Combine(SettingsStore.DataFolder, "crashes");

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Write(LogLevel.Info, LogCategory.App, "WinTimeline closed.");
        _gpu?.Dispose();
        _session?.End();
        base.OnExit(e);
    }

    private GpuContext? _gpu;
    private SessionMarker? _session;
}
