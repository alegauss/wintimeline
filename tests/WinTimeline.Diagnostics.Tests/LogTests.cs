using System.IO;
using System.IO.Compression;

namespace WinTimeline.Diagnostics.Tests;

/// <summary>WT6: the log keeps what matters, drops what it must, never grows without bound, and bundles.</summary>
public sealed class LogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt6-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ARecordIsWrittenAsOneJsonLineAndReadBack()
    {
        Log log = new(_folder);
        Dictionary<string, string> fields = new(StringComparer.Ordinal) { ["codec"] = "hevc", ["file"] = "clip.mov" };

        Assert.True(log.Append(LogLevel.Warning, LogCategory.Decode, "Fell back to software decode.", fields, new InvalidDataException("no DXVA profile")));

        LogRecord read = Assert.Single(log.ReadCurrent());
        Assert.Equal((LogLevel.Warning, LogCategory.Decode, "Fell back to software decode."), (read.Level, read.Category, read.Message));
        Assert.Equal("hevc", read.Fields!["codec"]);
        Assert.StartsWith("System.IO.InvalidDataException: no DXVA profile", read.Error, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"decode\"", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsBelowTheMinimumLevelAreDropped()
    {
        Log log = new(_folder) { MinimumLevel = LogLevel.Warning };

        Assert.False(log.Append(LogLevel.Info, LogCategory.App, "routine"));
        Assert.True(log.Append(LogLevel.Error, LogCategory.App, "serious"));

        Assert.Equal(["serious"], log.ReadCurrent().Select(r => r.Message));
    }

    [Fact]
    public void ASensitiveRecordIsDroppedUnlessThePersonOptedIn()
    {
        Log log = new(_folder);

        Assert.False(log.Append(LogLevel.Info, LogCategory.Ai, "transcript: hello", sensitive: true));
        log.IncludeSensitive = true;
        Assert.True(log.Append(LogLevel.Info, LogCategory.Ai, "transcript: hello", sensitive: true));

        Assert.Single(log.ReadCurrent());
    }

    [Fact]
    public void AFullFileRotatesAndOnlyKeepFilesSurvive()
    {
        Log log = new(_folder, maxBytes: 400, keep: 2);

        for (int i = 0; i < 40; i++)
        {
            log.Append(LogLevel.Info, LogCategory.Playback, $"frame {i:000} late by 4 ms");
        }

        string[] files = [.. Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal(["wintimeline.1.jsonl", "wintimeline.2.jsonl", "wintimeline.jsonl"], files);
        Assert.All(Directory.GetFiles(_folder), f => Assert.True(new FileInfo(f).Length <= 400));
        Assert.Equal("frame 039 late by 4 ms", log.ReadCurrent()[^1].Message);
    }

    [Fact]
    public void AnUnwritableFolderNeverThrows()
    {
        // A file where the folder should be: the log reports it did not write, and the app goes on.
        File.WriteAllText(_folder, "in the way");
        try
        {
            Assert.False(new Log(_folder).Append(LogLevel.Error, LogCategory.App, "nowhere to go"));
        }
        finally
        {
            File.Delete(_folder);
        }
    }

    [Fact]
    public void WithoutASharedLogAWriteGoesNowhere()
    {
        Log.Shared = null;

        Log.Write(LogLevel.Error, LogCategory.App, "nobody listens");

        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void TheBundleHoldsLogsSectionsTheNewestDumpAndSaysWhatWasMissing()
    {
        Log log = new(Path.Combine(_folder, "logs"));
        log.Append(LogLevel.Error, LogCategory.Decode, "decode failed");
        string crashes = Path.Combine(_folder, "crashes");
        Directory.CreateDirectory(crashes);
        File.WriteAllText(Path.Combine(crashes, "old.dmp"), "old");
        File.SetLastWriteTimeUtc(Path.Combine(crashes, "old.dmp"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(crashes, "new.dmp"), "new");
        string zip = Path.Combine(_folder, "diag.zip");

        // The log stays open for writing while it is bundled, as it is in the running app.
        using FileStream held = new(log.CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        IReadOnlyList<string> written = new DiagnosticsBundle()
            .Section("gpu.json", "{\"adapter\":\"WARP\"}")
            .File("settings.json", Path.Combine(_folder, "settings.json"))
            .Logs(log)
            .NewestDump(crashes)
            .Save(zip);

        Assert.Equal(["gpu.json", "logs/wintimeline.jsonl", "crash.dmp", "manifest.txt"], written);
        using ZipArchive archive = ZipFile.OpenRead(zip);
        Assert.Equal("new", Read(archive, "crash.dmp"));
        Assert.Contains("decode failed", Read(archive, "logs/wintimeline.jsonl"), StringComparison.Ordinal);
        Assert.Contains("settings.json: not present", Read(archive, "manifest.txt"), StringComparison.Ordinal);
    }

    private static string Read(ZipArchive archive, string name)
    {
        using StreamReader reader = new(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
