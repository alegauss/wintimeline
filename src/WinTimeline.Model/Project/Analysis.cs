using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace WinTimeline.Model.Project;

/// <summary>
/// WT104: who a media file is, independent of where it is or what it is called, so analysis
/// follows it when it is moved or renamed: its size and a SHA-256 of three 1 MB samples (head,
/// middle and tail). Reading 3 MB of a 40 GB camera file is instant where hashing it all is not.
/// </summary>
public static class MediaSignature
{
    private const int Sample = 1 << 20;

    /// <summary>The signature of a file, e.g. "s1-118551-3fa2…".</summary>
    /// <param name="path">The file.</param>
    /// <returns>The signature.</returns>
    public static string Of(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Sample);
        long size = file.Length;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[Sample];
        foreach (long at in new[] { 0, Math.Max(0, (size / 2) - (Sample / 2)), Math.Max(0, size - Sample) }.Distinct())
        {
            file.Position = at;
            int read = file.ReadAtLeast(buffer, (int)Math.Min(Sample, size - at), throwOnEndOfStream: false);
            hash.AppendData(buffer, 0, read);
        }

        return string.Create(CultureInfo.InvariantCulture, $"s1-{size}-{Convert.ToHexStringLower(hash.GetHashAndReset())[..32]}");
    }
}

/// <summary>One analysis of one media file.</summary>
/// <param name="Kind">What it is, e.g. transcript, shots, tags, loudness.</param>
/// <param name="Media">The media's <see cref="MediaSignature"/>.</param>
/// <param name="Model">The catalog id of the model that made it, e.g. whisper-base/base.</param>
/// <param name="ModelVersion">The model's version, so an upgraded model marks this stale.</param>
/// <param name="At">When it was made.</param>
/// <param name="Data">The result itself, in the kind's own shape.</param>
public sealed record AnalysisResult(string Kind, string Media, string Model, string ModelVersion, DateTimeOffset At, JsonNode Data);

/// <summary>A result as asked for, with whether the model that made it is still the current one.</summary>
/// <param name="Result">The result.</param>
/// <param name="Stale">Whether a different model or version made it than the one asked for.</param>
public sealed record StoredAnalysis(AnalysisResult Result, bool Stale);

/// <summary>
/// WT104: every analysis result a project holds, by media signature and kind, kept in the project
/// sidecar (analysis/&lt;kind&gt;.json, each a map from signature to result) and, when a global
/// cache is given, also beside every other project on this PC, so a file analysed once is never
/// analysed again for another project.
/// </summary>
public sealed class AnalysisStore
{
    private readonly Dictionary<(string Media, string Kind), AnalysisResult> _results = [];
    private readonly AnalysisCache? _cache;
    private readonly Lock _gate = new();

    /// <summary>An empty store.</summary>
    /// <param name="cache">The PC-wide cache, or null.</param>
    public AnalysisStore(AnalysisCache? cache = null) => _cache = cache;

    /// <summary>How many results it holds.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _results.Count;
            }
        }
    }

    /// <summary>The store a sidecar's state holds.</summary>
    /// <param name="state">The project state.</param>
    /// <param name="cache">The PC-wide cache, or null.</param>
    /// <returns>The store.</returns>
    public static AnalysisStore From(ProjectState state, AnalysisCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        AnalysisStore store = new(cache);
        foreach ((string kind, JsonNode document) in state.Analysis)
        {
            if (document is not JsonObject byMedia)
            {
                continue;
            }

            foreach ((string media, JsonNode? entry) in byMedia)
            {
                if (Read(kind, media, entry) is { } result)
                {
                    store._results[(media, kind)] = result;
                }
            }
        }

        return store;
    }

    /// <summary>The state with this store's results as its analysis documents, the rest kept.</summary>
    /// <param name="state">The project state.</param>
    /// <returns>The new state.</returns>
    public ProjectState Into(ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dictionary<string, JsonNode> analysis = new(StringComparer.Ordinal);
        AnalysisResult[] results;
        lock (_gate)
        {
            results = [.. _results.Values];
        }

        foreach (IGrouping<string, AnalysisResult> kind in results.GroupBy(r => r.Kind, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            JsonObject byMedia = [];
            foreach (AnalysisResult result in kind.OrderBy(r => r.Media, StringComparer.Ordinal))
            {
                byMedia[result.Media] = Write(result);
            }

            analysis[kind.Key] = byMedia;
        }

        return state with { Analysis = analysis };
    }

    /// <summary>A result, from the project or else the PC-wide cache, marked stale when another model made it.</summary>
    /// <param name="media">The media's signature.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="model">The model that would make it now.</param>
    /// <param name="version">Its version.</param>
    /// <returns>The stored result, or null when there is none.</returns>
    public StoredAnalysis? Get(string media, string kind, string model, string version)
    {
        AnalysisResult? result;
        lock (_gate)
        {
            _results.TryGetValue((media, kind), out result);
        }

        if (result is null && _cache?.Get(media, kind) is { } cached)
        {
            lock (_gate)
            {
                _results[(media, kind)] = result = cached;
            }
        }

        return result is null ? null : new StoredAnalysis(result, result.Model != model || result.ModelVersion != version);
    }

    /// <summary>Keeps a result, replacing the one of its kind for its media, here and in the PC-wide cache.</summary>
    /// <param name="result">The result.</param>
    public void Put(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            _results[(result.Media, result.Kind)] = result;
        }

        _cache?.Put(result);
    }

    internal static JsonObject Write(AnalysisResult result) => new()
    {
        ["model"] = result.Model,
        ["version"] = result.ModelVersion,
        ["at"] = result.At.ToString("o", CultureInfo.InvariantCulture),
        ["data"] = result.Data.DeepClone(),
    };

    internal static AnalysisResult? Read(string kind, string media, JsonNode? entry) =>
        entry is JsonObject o && o["model"]?.GetValue<string>() is { } model && o["data"] is { } data
            ? new AnalysisResult(kind, media, model, o["version"]?.GetValue<string>() ?? string.Empty,
                DateTimeOffset.Parse(o["at"]?.GetValue<string>() ?? "2000-01-01T00:00:00Z", CultureInfo.InvariantCulture), data.DeepClone())
            : null;
}

/// <summary>
/// WT104: analysis results shared by every project on this PC, one folder per media signature
/// under the data folder, so moving a file between projects does not analyse it again.
/// </summary>
/// <param name="Folder">The cache folder, e.g. %LOCALAPPDATA%\WinTimeline\analysis.</param>
public sealed record AnalysisCache(string Folder)
{
    /// <summary>A cached result, or null.</summary>
    /// <param name="media">The media's signature.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The result.</returns>
    public AnalysisResult? Get(string media, string kind)
    {
        string file = File(media, kind);
        try
        {
            return System.IO.File.Exists(file) ? AnalysisStore.Read(kind, media, JsonNode.Parse(System.IO.File.ReadAllBytes(file))) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // A cache that cannot be read is a miss; the analysis runs again.
            return null;
        }
    }

    /// <summary>Caches a result, replacing the file whole.</summary>
    /// <param name="result">The result.</param>
    public void Put(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string file = File(result.Media, result.Kind);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string fresh = file + ".new";
        System.IO.File.WriteAllBytes(fresh, Encoding.UTF8.GetBytes(AnalysisStore.Write(result).ToJsonString()));
        System.IO.File.Move(fresh, file, overwrite: true);
    }

    private string File(string media, string kind)
    {
        if (media.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || kind.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{media}' / '{kind}' cannot name a cache file");
        }

        return Path.Combine(Folder, media, kind + ".json");
    }
}
