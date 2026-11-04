using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WinTimeline.App.Tests;

/// <summary>WT7: a crashed run is noticed by the next one, with its dump, and a dump can be written.</summary>
public sealed class CrashHandlingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt7-" + Guid.NewGuid().ToString("N"));

    private string Crashes => Path.Combine(_folder, "crashes");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AFirstStartFindsNothingAndACleanExitLeavesNothing()
    {
        SessionMarker marker = SessionMarker.Begin(_folder, Crashes, out PreviousCrash? previous);
        Assert.Null(previous);
        Assert.True(File.Exists(marker.Path));

        marker.End();

        SessionMarker.Begin(_folder, Crashes, out PreviousCrash? next).End();
        Assert.Null(next);
    }

    [Fact]
    public void AMarkerWhoseProcessIsGoneIsACrashWithTheDumpItLeft()
    {
        DateTimeOffset started = DateTimeOffset.Now.AddMinutes(-2);
        LeaveMarker(started, DeadProcessId());
        Directory.CreateDirectory(Crashes);
        string old = Path.Combine(Crashes, "older.dmp");
        File.WriteAllText(old, "from a run before that one");
        File.SetLastWriteTime(old, started.LocalDateTime.AddHours(-1));
        string dump = Path.Combine(Crashes, "wintimeline-1-x.dmp");
        File.WriteAllText(dump, "dump");

        SessionMarker.Begin(_folder, Crashes, out PreviousCrash? previous).End();

        Assert.NotNull(previous);
        Assert.Equal(dump, previous.Dump);
        Assert.Equal(started.ToUnixTimeSeconds(), previous.Started.ToUnixTimeSeconds());
    }

    [Fact]
    public void AMarkerTornByTheCrashIsStillACrash()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "session.json"), "{\"Started\":\"2026-");

        SessionMarker.Begin(_folder, Crashes, out PreviousCrash? previous).End();

        Assert.NotNull(previous);
        Assert.Null(previous.Dump);
    }

    [Fact]
    public void TheRecoveryBarNamesTheDumpWhenThereIsOne()
    {
        PreviousCrash withDump = new(DateTimeOffset.Now, 1, @"C:\x\wintimeline-7-1.dmp");
        PreviousCrash without = new(DateTimeOffset.Now, 1, null);

        Assert.Contains("A crash dump was kept (wintimeline-7-1.dmp); the diagnostics bundle includes it.", MainWindow.RecoveryMessage(withDump), StringComparison.Ordinal);
        Assert.EndsWith("The diagnostics bundle holds its log.", MainWindow.RecoveryMessage(without), StringComparison.Ordinal);
        Assert.StartsWith("WinTimeline closed unexpectedly last time", MainWindow.RecoveryMessage(without), StringComparison.Ordinal);
    }

    [Fact]
    public void AMinidumpOfThisProcessIsWritten()
    {
        string? dump = MiniDump.Write(Crashes);

        Assert.NotNull(dump);
        // "MDMP", the minidump signature.
        Assert.Equal("MDMP"u8.ToArray(), File.ReadAllBytes(dump).AsSpan(0, 4).ToArray());
    }

    private void LeaveMarker(DateTimeOffset started, int processId)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "session.json"), JsonSerializer.Serialize(new { Started = started, ProcessId = processId }));
    }

    // A process that ran and is gone, so its id names nothing alive.
    private static int DeadProcessId()
    {
        using Process done = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        done.WaitForExit();
        return done.Id;
    }
}
