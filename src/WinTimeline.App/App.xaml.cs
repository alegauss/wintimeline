using System.Windows;
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
            MessageBox.Show(parsed.Error + "\n\nSee docs/COMMAND-LINE.md.", "WinTimeline", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
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
            MessageBox.Show(choice.Error + "\n\nSee docs/COMMAND-LINE.md.", "WinTimeline", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
        }

        if (!choice.Ok)
        {
            notices.Add($"The saved GPU \"{settings.Current.Adapter}\" is not on this PC; using the default one.");
            choice = AdapterChoice.Pick(adapters, null);
        }

        _gpu = GpuContext.Create(choice);
        MainWindow window = new(parsed.Options!, _gpu, settings, notices);
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _gpu?.Dispose();
        base.OnExit(e);
    }

    private GpuContext? _gpu;
}
