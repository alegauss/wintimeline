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

        // WT3: the one GPU device, on the adapter --adapter names or the high-performance one.
        AdapterChoice choice = AdapterChoice.Pick(GpuAdapters.Enumerate(), parsed.Options!.Adapter);
        if (!choice.Ok)
        {
            MessageBox.Show(choice.Error + "\n\nSee docs/COMMAND-LINE.md.", "WinTimeline", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
        }

        _gpu = GpuContext.Create(choice);
        MainWindow window = new(parsed.Options!, _gpu);
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
