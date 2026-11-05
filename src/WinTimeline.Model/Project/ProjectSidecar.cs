using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinTimeline.Model.Project;

/// <summary>A bin: a named list of what it gathers, as media URLs or timeline paths.</summary>
/// <param name="Name">What the bin is called.</param>
/// <param name="Items">What it holds, in order.</param>
public sealed record Bin(string Name, IReadOnlyList<string> Items);

/// <summary>How the window was laid out over this project.</summary>
/// <param name="ViewerShare">The viewer's share of the height it splits with the timeline, 0 to 1.</param>
/// <param name="ActiveTimeline">The name of the timeline shown, or null for the first.</param>
/// <param name="RulerUnit">WT49: what the ruler labels time in, e.g. "Frames", or null for timecode.</param>
public sealed record WorkspaceLayout(double ViewerShare, string? ActiveTimeline, string? RulerUnit = null)
{
    /// <summary>The layout a project opens with when it has none.</summary>
    public static WorkspaceLayout Default { get; } = new(0.6, null);
}

/// <summary>
/// Vectors computed from the media, one per key, kept as raw float32 so tens of thousands load
/// in tens of milliseconds (WT22's measurement, spike/sidecar).
/// </summary>
/// <param name="Model">What computed them, e.g. "clip-vit-b32@1"; a different model means recomputing.</param>
/// <param name="Dimensions">How many floats each vector has.</param>
/// <param name="Keys">What each vector describes, e.g. a media URL and a frame.</param>
/// <param name="Values">Every vector, one after another.</param>
public sealed record EmbeddingIndex(string Model, int Dimensions, IReadOnlyList<string> Keys, ReadOnlyMemory<float> Values)
{
    /// <summary>The vector for the nth key.</summary>
    /// <param name="index">Which.</param>
    /// <returns>Its values.</returns>
    public ReadOnlySpan<float> Vector(int index) => Values.Span.Slice(index * Dimensions, Dimensions);
}

/// <summary>
/// The editor's own state about a project, which another OTIO tool should not see: bins, proxy
/// mappings, analysis results and the workspace layout. What another tool should see goes into
/// the timeline's metadata["wintimeline"] instead.
/// </summary>
public sealed record ProjectState
{
    /// <summary>A project with nothing worth keeping.</summary>
    public static ProjectState Empty { get; } = new();

    /// <summary>The bins, in order.</summary>
    public IReadOnlyList<Bin> Bins { get; init; } = [];

    /// <summary>Media URL to the proxy that plays in its place.</summary>
    public IReadOnlyDictionary<string, string> Proxies { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The layout, or null when it is the default.</summary>
    public WorkspaceLayout? Layout { get; init; }

    /// <summary>Analysis results by kind, e.g. "transcript" or "shots", each a JSON document.</summary>
    public IReadOnlyDictionary<string, JsonNode> Analysis { get; init; } = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

    /// <summary>The embedding index, or null.</summary>
    public EmbeddingIndex? Embeddings { get; init; }

    /// <summary>The background jobs left unfinished, as the job scheduler saved them (WT40), or null.</summary>
    public JsonArray? Jobs { get; init; }

    /// <summary>Whether there is nothing a sidecar would need to keep.</summary>
    public bool IsEmpty => Bins.Count == 0 && Proxies.Count == 0 && (Layout is null || Layout == WorkspaceLayout.Default) && Analysis.Count == 0 && Embeddings is null && (Jobs is null || Jobs.Count == 0);
}

/// <summary>
/// WT22: the folder beside a document that keeps its <see cref="ProjectState"/>, e.g.
/// <c>cut.otio.wtproj/</c>. It is made on the first save with something worth keeping, and each
/// file in it is replaced whole, so a crash mid-save leaves the previous file and never half a
/// new one. Format: project.json (bins, proxies, layout), jobs.json (unfinished background
/// jobs), analysis/&lt;kind&gt;.json, and
/// analysis/embeddings.json with analysis/embeddings.f32 beside it.
/// </summary>
public static class ProjectSidecar
{
    /// <summary>The format this build writes; a newer one is refused rather than misread.</summary>
    public const int Format = 1;

    private const string ProjectFile = "project.json";
    private const string FormatKey = "format";
    private const string JobsFile = "jobs.json";
    private const string AnalysisFolder = "analysis";
    private const string Embeddings = "embeddings";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The sidecar folder for a document.</summary>
    /// <param name="documentPath">The document's path.</param>
    /// <returns>The folder's path.</returns>
    public static string FolderFor(string documentPath)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        return Path.GetFullPath(documentPath) + ".wtproj";
    }

    /// <summary>The state kept beside a document, or <see cref="ProjectState.Empty"/> when it has no sidecar.</summary>
    /// <param name="documentPath">The document's path.</param>
    /// <returns>The state.</returns>
    /// <exception cref="InvalidDataException">The sidecar is damaged or from a newer WinTimeline.</exception>
    public static ProjectState Load(string documentPath)
    {
        string folder = FolderFor(documentPath);
        string project = Path.Combine(folder, ProjectFile);
        if (!File.Exists(project))
        {
            return ProjectState.Empty;
        }

        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllBytes(project)) as JsonObject ?? throw new InvalidDataException($"{project} is not a JSON object");
            int format = root[FormatKey]?.GetValue<int>() ?? 0;
            if (format > Format)
            {
                throw new InvalidDataException($"{project} is format {format}; this WinTimeline reads up to {Format}");
            }

            return new ProjectState
            {
                Bins = [.. (root["bins"] as JsonArray ?? []).OfType<JsonObject>().Select(b => new Bin(
                    b["name"]?.GetValue<string>() ?? string.Empty,
                    [.. (b["items"] as JsonArray ?? []).Select(i => i?.GetValue<string>() ?? string.Empty)]))],
                Proxies = (root["proxies"] as JsonObject ?? []).ToDictionary(p => p.Key, p => p.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal),
                Layout = root["layout"] is JsonObject layout
                    ? new WorkspaceLayout(layout["viewer_share"]?.GetValue<double>() ?? WorkspaceLayout.Default.ViewerShare, layout["active_timeline"]?.GetValue<string>(), layout["ruler_unit"]?.GetValue<string>())
                    : null,
                Analysis = LoadAnalysis(Path.Combine(folder, AnalysisFolder)),
                Embeddings = LoadEmbeddings(Path.Combine(folder, AnalysisFolder)),
                Jobs = File.Exists(Path.Combine(folder, JobsFile)) ? JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder, JobsFile))) as JsonArray : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"{folder} could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes the state beside a document. Nothing is written for an empty state where no sidecar
    /// exists yet; where one exists, what the state no longer holds is removed.
    /// </summary>
    /// <param name="documentPath">The document's path.</param>
    /// <param name="state">What to keep.</param>
    /// <returns>Whether a sidecar exists afterwards.</returns>
    public static bool Save(string documentPath, ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string folder = FolderFor(documentPath);
        if (state.IsEmpty && !Directory.Exists(folder))
        {
            return false;
        }

        string analysis = Path.Combine(folder, AnalysisFolder);
        Directory.CreateDirectory(analysis);
        JsonObject root = new()
        {
            [FormatKey] = Format,
            ["bins"] = new JsonArray([.. state.Bins.Select(b => new JsonObject
            {
                ["name"] = b.Name,
                ["items"] = new JsonArray([.. b.Items.Select(i => (JsonNode?)JsonValue.Create(i))]),
            })]),
            ["proxies"] = new JsonObject(state.Proxies.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
        };
        if (LayoutJson(state.Layout) is { } layout)
        {
            root["layout"] = layout;
        }

        Replace(Path.Combine(folder, ProjectFile), Encoding.UTF8.GetBytes(root.ToJsonString(Indented)));
        string jobs = Path.Combine(folder, JobsFile);
        if (state.Jobs is { Count: > 0 } unfinished)
        {
            Replace(jobs, Encoding.UTF8.GetBytes(unfinished.ToJsonString(Indented)));
        }
        else
        {
            File.Delete(jobs);
        }

        HashSet<string> kept = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string kind, JsonNode document) in state.Analysis)
        {
            if (kind == Embeddings || kind.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"'{kind}' cannot name an analysis file", nameof(state));
            }

            Replace(Path.Combine(analysis, kind + ".json"), Encoding.UTF8.GetBytes(document.ToJsonString(Indented)));
            kept.Add(kind + ".json");
        }

        if (state.Embeddings is { } index)
        {
            SaveEmbeddings(analysis, index);
            kept.Add(Embeddings + ".json");
            kept.Add(Embeddings + ".f32");
        }

        foreach (string stale in Directory.EnumerateFiles(analysis).Where(f => !kept.Contains(Path.GetFileName(f))))
        {
            File.Delete(stale);
        }

        return true;
    }

    /// <summary>
    /// WT164: writes the layout alone beside a document, leaving everything else the sidecar
    /// holds as it is on disk, so a document closed without saving still reopens as it was laid
    /// out. Nothing is written for the default layout where no sidecar exists yet.
    /// </summary>
    /// <param name="documentPath">The document's path.</param>
    /// <param name="layout">The layout, or null for the default.</param>
    /// <returns>Whether the sidecar was written.</returns>
    /// <exception cref="InvalidDataException">The sidecar is damaged or from a newer WinTimeline, so it is left alone.</exception>
    public static bool SaveLayout(string documentPath, WorkspaceLayout? layout)
    {
        string folder = FolderFor(documentPath);
        string project = Path.Combine(folder, ProjectFile);
        JsonNode? written = LayoutJson(layout);
        JsonObject root;
        if (File.Exists(project))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllBytes(project)) as JsonObject ?? throw new InvalidDataException($"{project} is not a JSON object");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{project} could not be read: {ex.Message}", ex);
            }

            if ((root[FormatKey]?.GetValue<int>() ?? 0) > Format)
            {
                throw new InvalidDataException($"{project} is from a newer WinTimeline; its layout is left alone");
            }
        }
        else if (written is null)
        {
            return false;
        }
        else
        {
            Directory.CreateDirectory(folder);
            root = new JsonObject { [FormatKey] = Format, ["bins"] = new JsonArray(), ["proxies"] = new JsonObject() };
        }

        if (JsonNode.DeepEquals(root["layout"], written))
        {
            return false;
        }

        root.Remove("layout");
        if (written is not null)
        {
            root["layout"] = written;
        }

        Replace(project, Encoding.UTF8.GetBytes(root.ToJsonString(Indented)));
        return true;
    }

    private static JsonObject? LayoutJson(WorkspaceLayout? layout) => layout is null || layout == WorkspaceLayout.Default
        ? null
        : new JsonObject
        {
            ["viewer_share"] = Math.Round(layout.ViewerShare, 4),
            ["active_timeline"] = layout.ActiveTimeline,
            ["ruler_unit"] = layout.RulerUnit,
        };

    private static Dictionary<string, JsonNode> LoadAnalysis(string folder)
    {
        Dictionary<string, JsonNode> found = new(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return found;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            string kind = Path.GetFileNameWithoutExtension(file);
            if (kind != Embeddings && JsonNode.Parse(File.ReadAllBytes(file)) is { } document)
            {
                found[kind] = document;
            }
        }

        return found;
    }

    private static EmbeddingIndex? LoadEmbeddings(string folder)
    {
        string index = Path.Combine(folder, Embeddings + ".json");
        string values = Path.Combine(folder, Embeddings + ".f32");
        if (!File.Exists(index) || !File.Exists(values))
        {
            return null;
        }

        JsonObject header = JsonNode.Parse(File.ReadAllBytes(index)) as JsonObject ?? throw new InvalidDataException($"{index} is not a JSON object");
        int dims = header["dimensions"]?.GetValue<int>() ?? 0;
        string[] keys = [.. (header["keys"] as JsonArray ?? []).Select(k => k?.GetValue<string>() ?? string.Empty)];
        byte[] raw = File.ReadAllBytes(values);
        if (dims <= 0 || raw.Length != (long)keys.Length * dims * sizeof(float))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"{values} holds {raw.Length} bytes, not {keys.Length} vectors of {dims} floats"));
        }

        return new EmbeddingIndex(header["model"]?.GetValue<string>() ?? string.Empty, dims, keys, MemoryMarshal.Cast<byte, float>(raw).ToArray());
    }

    private static void SaveEmbeddings(string folder, EmbeddingIndex index)
    {
        if (index.Values.Length != index.Keys.Count * index.Dimensions)
        {
            throw new ArgumentException($"the index has {index.Keys.Count} keys of {index.Dimensions} but {index.Values.Length} values", nameof(index));
        }

        // The vectors first: a crash between the two leaves an index that names the old count, and Load refuses the mismatch.
        Replace(Path.Combine(folder, Embeddings + ".f32"), MemoryMarshal.AsBytes(index.Values.Span).ToArray());
        JsonObject header = new()
        {
            ["model"] = index.Model,
            ["dimensions"] = index.Dimensions,
            ["keys"] = new JsonArray([.. index.Keys.Select(k => (JsonNode?)JsonValue.Create(k))]),
        };
        Replace(Path.Combine(folder, Embeddings + ".json"), Encoding.UTF8.GetBytes(header.ToJsonString()));
    }

    /// <summary>Writes a file whole beside its target and then moves it over, so no reader sees half of it.</summary>
    private static void Replace(string path, byte[] bytes)
    {
        string fresh = path + ".new";
        File.WriteAllBytes(fresh, bytes);
        File.Move(fresh, path, overwrite: true);
    }
}
