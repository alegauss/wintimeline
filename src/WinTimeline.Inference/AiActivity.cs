using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTimeline.Inference;

/// <summary>One inference the app ran, as the AI activity log shows it.</summary>
/// <param name="At">When it started.</param>
/// <param name="Feature">e.g. transcription.</param>
/// <param name="Model">The catalog id and variant, e.g. whisper-base/base.</param>
/// <param name="Provider">Where it ran, e.g. "DirectML · NVIDIA GeForce RTX 4060, 8 GB".</param>
/// <param name="Media">The media's file name, never its content.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="Outcome">"ok", "cancelled", or why it failed.</param>
public sealed record AiActivityRecord(DateTimeOffset At, string Feature, string Model, string Provider, string? Media, TimeSpan Duration, string Outcome);

/// <summary>
/// WT103: what ran, so a person can see it: every inference appends one line to ai-activity.jsonl
/// in the data folder. It names the media file and never holds its content, a transcript or a
/// prompt. The file is kept to its last few thousand lines.
/// </summary>
public sealed class AiActivity
{
    /// <summary>How many records the file keeps.</summary>
    public const int Kept = 5000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();

    /// <summary>Keeps the log at a path.</summary>
    /// <param name="path">The JSONL file.</param>
    public AiActivity(string path) => Path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The JSONL file.</summary>
    public string Path { get; }

    /// <summary>Appends a record, trimming the oldest past <see cref="Kept"/>.</summary>
    /// <param name="record">What ran.</param>
    public void Record(AiActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string line = JsonSerializer.Serialize(record, Json);
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            File.AppendAllText(Path, line + "\n");
            string[] lines = File.ReadAllLines(Path);
            if (lines.Length > Kept)
            {
                File.WriteAllLines(Path, lines[^Kept..]);
            }
        }
    }

    /// <summary>Times an inference and records it, whatever its outcome.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="model">The model and variant.</param>
    /// <param name="provider">Where it runs.</param>
    /// <param name="media">The media's file name, or null.</param>
    /// <param name="run">The inference.</param>
    /// <typeparam name="T">What it returns.</typeparam>
    /// <returns>What it returned.</returns>
    public async Task<T> TrackAsync<T>(string feature, string model, string provider, string? media, Func<Task<T>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        DateTimeOffset at = DateTimeOffset.Now;
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string outcome = "ok";
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            outcome = ex.Message;
            throw;
        }
        finally
        {
            Record(new AiActivityRecord(at, feature, model, provider, media is null ? null : System.IO.Path.GetFileName(media), watch.Elapsed, outcome));
        }
    }

    /// <summary>The records, newest first.</summary>
    /// <returns>The records; empty before anything ran.</returns>
    public IReadOnlyList<AiActivityRecord> Read()
    {
        lock (_gate)
        {
            return !File.Exists(Path)
                ? []
                : [.. File.ReadAllLines(Path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<AiActivityRecord>(l, Json)!).Reverse()];
        }
    }
}
