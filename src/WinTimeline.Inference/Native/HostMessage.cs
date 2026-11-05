using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTimeline.Inference.Native;

/// <summary>
/// WT166: one line of the protocol between the app and WinTimeline.Inference.Host, the process
/// that owns a native engine (llama.cpp, whisper.cpp). The app sends <c>load</c>, then <c>run</c>
/// and <c>cancel</c>; the host answers <c>loaded</c>, a <c>token</c> per piece of output,
/// then <c>done</c> or <c>failed</c>. One JSON object a line, as Revelei's generation host speaks.
/// </summary>
/// <param name="Type">load, run, cancel, loaded, token, done or failed.</param>
/// <param name="Model">For load: the model file the engine opens.</param>
/// <param name="Prompt">For run: what the engine is given.</param>
/// <param name="Text">For token: the piece produced; for done: the whole output.</param>
/// <param name="Message">For failed: why, in a sentence.</param>
/// <param name="Cancelled">For failed: whether it stopped because it was asked to.</param>
public sealed record HostMessage(
    string Type,
    string? Model = null,
    string? Prompt = null,
    string? Text = null,
    string? Message = null,
    bool Cancelled = false)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>The message as one line, with no line break in it.</summary>
    /// <returns>The JSON.</returns>
    public string ToLine() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a line, or null for one that is not a message (an engine's own chatter).</summary>
    /// <param name="line">The line.</param>
    /// <returns>The message, or null.</returns>
    public static HostMessage? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HostMessage>(line, Options) is { Type.Length: > 0 } message ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The native engine failed, or its host process stopped; the app goes on.</summary>
public sealed class NativeEngineException : Exception
{
    /// <summary>Creates the exception.</summary>
    public NativeEngineException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public NativeEngineException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">What caused it.</param>
    public NativeEngineException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
