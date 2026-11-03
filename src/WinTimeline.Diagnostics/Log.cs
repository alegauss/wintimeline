using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTimeline.Diagnostics;

/// <summary>How much the log records, from the most to the least.</summary>
public enum LogLevel
{
    /// <summary>Everything, frame by frame.</summary>
    Trace,

    /// <summary>What a developer needs to follow a run.</summary>
    Debug,

    /// <summary>What happened: the default.</summary>
    Info,

    /// <summary>What went wrong and was recovered from.</summary>
    Warning,

    /// <summary>What went wrong and was not.</summary>
    Error,
}

/// <summary>Which part of the app a record is about.</summary>
public enum LogCategory
{
    /// <summary>Start-up, settings, shutdown.</summary>
    App,

    /// <summary>Probing and decoding media.</summary>
    Decode,

    /// <summary>The GPU device, surfaces and compositing.</summary>
    Render,

    /// <summary>The playback clock and frame delivery.</summary>
    Playback,

    /// <summary>The audio engine.</summary>
    Audio,

    /// <summary>Local inference.</summary>
    Ai,

    /// <summary>Encoding and the export queue.</summary>
    Export,

    /// <summary>Windows, panels and input.</summary>
    Ui,
}

/// <summary>One line of the log.</summary>
/// <param name="At">When it was written.</param>
/// <param name="Level">How serious.</param>
/// <param name="Category">What part of the app.</param>
/// <param name="Message">What happened, in a sentence.</param>
/// <param name="Fields">Facts that go with it: a file name, a codec, a driver version.</param>
/// <param name="Error">The exception's type, message and stack, when there was one.</param>
public sealed record LogRecord(
    DateTimeOffset At,
    LogLevel Level,
    LogCategory Category,
    string Message,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? Error = null);

/// <summary>
/// The app-wide log (WT6, from Revelei's OperationLog): a JSON line per record in
/// wintimeline.jsonl, rotated to wintimeline.1.jsonl and on past its size, so the folder never
/// grows without bound. Records below <see cref="MinimumLevel"/> are dropped. A record marked
/// sensitive (media content, a transcript, a prompt) is dropped unless the person opted in,
/// because a log is what gets attached to a public issue.
/// </summary>
public sealed class Log
{
    private const string Name = "wintimeline";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new();

    /// <summary>A log in a folder.</summary>
    /// <param name="folder">Where the files go; created when missing.</param>
    /// <param name="maxBytes">Size at which the current file is rotated.</param>
    /// <param name="keep">Rotated files kept beside the current one.</param>
    public Log(string folder, long maxBytes = 1 << 20, int keep = 5)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        Folder = folder;
        MaxBytes = maxBytes;
        Keep = keep;
    }

    /// <summary>The log the app writes to; a log nobody configured writes nowhere.</summary>
    public static Log? Shared { get; set; }

    /// <summary>Where the files go.</summary>
    public string Folder { get; }

    /// <summary>Size at which the current file is rotated.</summary>
    public long MaxBytes { get; }

    /// <summary>Rotated files kept.</summary>
    public int Keep { get; }

    /// <summary>The least serious level written.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Whether records marked sensitive are written; off unless the person opts in.</summary>
    public bool IncludeSensitive { get; set; }

    /// <summary>The file being written.</summary>
    public string CurrentFile => Path.Combine(Folder, Name + ".jsonl");

    /// <summary>The current file and the rotated ones, newest first.</summary>
    public IEnumerable<string> Files =>
        new[] { CurrentFile }.Concat(Enumerable.Range(1, Keep).Select(Numbered)).Where(File.Exists);

    /// <summary>Writes a record to <see cref="Shared"/>, when there is one.</summary>
    /// <param name="level">How serious.</param>
    /// <param name="category">What part of the app.</param>
    /// <param name="message">What happened.</param>
    /// <param name="fields">Facts that go with it.</param>
    /// <param name="error">The exception, when there was one.</param>
    public static void Write(LogLevel level, LogCategory category, string message, IReadOnlyDictionary<string, string>? fields = null, Exception? error = null) =>
        Shared?.Append(level, category, message, fields, error);

    /// <summary>Writes a record, rotating first when the file is full.</summary>
    /// <param name="level">How serious.</param>
    /// <param name="category">What part of the app.</param>
    /// <param name="message">What happened.</param>
    /// <param name="fields">Facts that go with it.</param>
    /// <param name="error">The exception, when there was one.</param>
    /// <param name="sensitive">Whether it carries media content, a transcript or a prompt.</param>
    /// <returns>Whether it was written.</returns>
    public bool Append(LogLevel level, LogCategory category, string message, IReadOnlyDictionary<string, string>? fields = null, Exception? error = null, bool sensitive = false)
    {
        if (level < MinimumLevel || (sensitive && !IncludeSensitive))
        {
            return false;
        }

        LogRecord record = new(DateTimeOffset.Now, level, category, message, fields, error?.ToString());
        string line = JsonSerializer.Serialize(record, Json) + "\n";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                FileInfo current = new(CurrentFile);
                if (current.Exists && current.Length + line.Length > MaxBytes)
                {
                    Rotate();
                }

                File.AppendAllText(CurrentFile, line);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must never be the failure.
                return false;
            }
        }
    }

    /// <summary>The records in the current file, oldest first.</summary>
    /// <returns>The records; empty when nothing was logged.</returns>
    public IReadOnlyList<LogRecord> ReadCurrent()
    {
        lock (_gate)
        {
            return File.Exists(CurrentFile)
                ? [.. File.ReadAllLines(CurrentFile).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<LogRecord>(l, Json)!)]
                : [];
        }
    }

    private string Numbered(int n) => Path.Combine(Folder, $"{Name}.{n}.jsonl");

    private void Rotate()
    {
        if (Keep == 0)
        {
            File.Delete(CurrentFile);
            return;
        }

        File.Delete(Numbered(Keep));
        for (int n = Keep - 1; n >= 1; n--)
        {
            if (File.Exists(Numbered(n)))
            {
                File.Move(Numbered(n), Numbered(n + 1));
            }
        }

        File.Move(CurrentFile, Numbered(1));
    }
}
