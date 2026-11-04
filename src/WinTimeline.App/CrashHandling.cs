using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WinTimeline.Diagnostics;

namespace WinTimeline.App;

/// <summary>What the last run left behind when it did not close cleanly.</summary>
/// <param name="Started">When that run started.</param>
/// <param name="ProcessId">Its process id.</param>
/// <param name="Dump">The newest crash dump written since it started, or null when there is none.</param>
public sealed record PreviousCrash(DateTimeOffset Started, int ProcessId, string? Dump);

/// <summary>
/// A file in the data folder that exists while WinTimeline runs (WT7). A clean exit deletes it,
/// so finding one whose process is gone means the last run crashed or was killed.
/// </summary>
public sealed class SessionMarker
{
    private SessionMarker(string path) => Path = path;

    /// <summary>The marker file.</summary>
    public string Path { get; }

    /// <summary>Reads what an earlier run left, then marks this one as running.</summary>
    /// <param name="folder">The data folder.</param>
    /// <param name="crashFolder">Where dumps are written.</param>
    /// <param name="previous">The crashed run, or null when the last one closed cleanly or another is still running.</param>
    /// <returns>This run's marker.</returns>
    public static SessionMarker Begin(string folder, string crashFolder, out PreviousCrash? previous)
    {
        string path = System.IO.Path.Combine(folder, "session.json");
        previous = Read(path, crashFolder);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(new Marker(DateTimeOffset.Now, Environment.ProcessId)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "The session marker could not be written; a crash will not be noticed next time.", error: ex);
        }

        return new SessionMarker(path);
    }

    /// <summary>Marks the run as closed cleanly.</summary>
    public void End()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "The session marker could not be removed.", error: ex);
        }
    }

    private static PreviousCrash? Read(string path, string crashFolder)
    {
        Marker? marker;
        try
        {
            marker = File.Exists(path) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A marker torn by the crash itself is still a crash.
            marker = new Marker(File.GetLastWriteTime(path), 0);
        }

        if (marker is null || IsRunning(marker.ProcessId))
        {
            return null;
        }

        string? dump = Directory.Exists(crashFolder)
            ? new DirectoryInfo(crashFolder).GetFiles("*.dmp")
                .Where(f => f.LastWriteTime >= marker.Started.LocalDateTime.AddSeconds(-1))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName
            : null;
        return new PreviousCrash(marker.Started, marker.ProcessId, dump);
    }

    private static bool IsRunning(int processId)
    {
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.StartsWith("WinTimeline", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record Marker(DateTimeOffset Started, int ProcessId);
}

/// <summary>
/// What happens when WinTimeline fails (WT7). A managed exception on the UI thread is logged and
/// offers to restart or keep going; one on any other thread is logged and dumped with
/// <see cref="MiniDump"/> before the runtime ends the process. A native crash cannot be caught in
/// process; the session marker makes the next start notice it, and the installer's Windows Error
/// Reporting registration (WT143) keeps its dump.
/// </summary>
public static class CrashHandling
{
    /// <summary>Hooks every unhandled-exception path; dumps go to <paramref name="crashFolder"/>.</summary>
    /// <param name="app">The application.</param>
    /// <param name="crashFolder">Where minidumps are written.</param>
    public static void Install(Application app, string crashFolder)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string? dump = e.IsTerminating ? MiniDump.Write(crashFolder) : null;
            Log.Write(LogLevel.Error, LogCategory.App, "Unhandled exception; WinTimeline is closing.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["dump"] = dump ?? "none" },
                e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write(LogLevel.Error, LogCategory.App, "A background task failed and nobody observed it.", error: e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// What <c>--crash-test</c> does, so the handling above can be proven: "managed" throws on the UI
    /// thread; "native" faults inside the C runtime, as a decoder or a driver would, past every
    /// managed handler. (A managed write through a null pointer would not do: the runtime turns a
    /// fault on the null page into a NullReferenceException.)
    /// </summary>
    /// <param name="kind">managed or native.</param>
    public static void Crash(string kind)
    {
        Log.Write(LogLevel.Warning, LogCategory.App, $"Crashing on purpose (--crash-test {kind}).");
        if (kind == "native")
        {
            MiniDump.Fault();
        }

        throw new InvalidOperationException("A managed crash asked for by --crash-test managed.");
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write(LogLevel.Error, LogCategory.App, "Unhandled exception on the UI thread.", error: e.Exception);
        MessageBoxResult answer = MessageBox.Show(
            $"WinTimeline hit an error it did not expect:\n\n{e.Exception.Message}\n\n"
            + "Restart WinTimeline? Choose No to keep working; the error is in the log, and Help > "
            + "Create diagnostics bundle collects it for a report.",
            "WinTimeline",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);
        e.Handled = true;
        if (answer == MessageBoxResult.Yes)
        {
            Restart();
        }
    }

    private static void Restart()
    {
        string[] args = [.. Environment.GetCommandLineArgs().Skip(1)];
        int crash = Array.IndexOf(args, "--crash-test");
        string[] kept = crash < 0 ? args : [.. args.Take(crash), .. args.Skip(crash + 2)];
        if (Environment.ProcessPath is { } exe)
        {
            ProcessStartInfo start = new(exe) { UseShellExecute = false };
            foreach (string arg in kept.Where(a => !a.StartsWith("--crash-test=", StringComparison.Ordinal)))
            {
                start.ArgumentList.Add(arg);
            }

            Process.Start(start)?.Dispose();
        }

        Log.Write(LogLevel.Info, LogCategory.App, string.Create(CultureInfo.InvariantCulture, $"Restarting after an error, with {kept.Length} arguments."));
        Application.Current.Shutdown(1);
    }
}
