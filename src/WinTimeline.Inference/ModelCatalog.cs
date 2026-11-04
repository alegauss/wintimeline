using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTimeline.Inference;

/// <summary>A model's licence, as the catalog states it.</summary>
/// <param name="Spdx">Its SPDX id, e.g. Apache-2.0.</param>
/// <param name="CommercialUse">Whether it allows commercial use.</param>
/// <param name="Url">Where the full text is.</param>
public sealed record ModelLicence(string Spdx, [property: JsonPropertyName("commercial_use")] bool CommercialUse, string Url);

/// <summary>Where a model's files come from: a Hugging Face repository at one commit.</summary>
/// <param name="Repo">e.g. Qwen/Qwen2.5-1.5B-Instruct-GGUF.</param>
/// <param name="Revision">The commit every URL is pinned to.</param>
public sealed record ModelSource(string Repo, string Revision);

/// <summary>One file of a variant, with what it must hash to.</summary>
/// <param name="Path">Its path in the repository, and under the model's folder once downloaded.</param>
/// <param name="Url">Its resolve URL, pinned to the source's commit.</param>
/// <param name="Sha256">Its SHA-256, lowercase hex.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record ModelFile(string Path, string Url, string Sha256, long Size);

/// <summary>A measured speed, to compare variants and machines.</summary>
/// <param name="MeasuredOn">The machine and provider, e.g. "RTX 4060, DirectML".</param>
/// <param name="Value">The number.</param>
/// <param name="Unit">Its unit, e.g. "ms per frame".</param>
public sealed record ModelSpeed([property: JsonPropertyName("measured_on")] string MeasuredOn, double Value, string Unit);

/// <summary>One way to run a model: a quantisation or size, its files and what it needs.</summary>
/// <param name="Id">e.g. fp16, int8, q4_k_m.</param>
/// <param name="Default">Whether it is the one offered first.</param>
/// <param name="Files">What to download.</param>
/// <param name="MinVramMb">The video memory it needs, 0 for a CPU-only variant.</param>
/// <param name="MinRamMb">The system memory it needs.</param>
/// <param name="Providers">Where it runs, best first.</param>
/// <param name="Speed">A measured speed, or null until one is measured.</param>
public sealed record ModelVariant(
    string Id,
    bool Default,
    IReadOnlyList<ModelFile> Files,
    [property: JsonPropertyName("min_vram_mb")] int MinVramMb,
    [property: JsonPropertyName("min_ram_mb")] int MinRamMb,
    IReadOnlyList<string> Providers,
    ModelSpeed? Speed)
{
    /// <summary>The bytes it downloads.</summary>
    public long DownloadBytes => Files.Sum(f => f.Size);
}

/// <summary>A model the app can download and run.</summary>
/// <param name="Schema">The entry's schema version.</param>
/// <param name="Id">e.g. clip-vit-b32.</param>
/// <param name="Name">What the model manager calls it.</param>
/// <param name="Feature">The feature it serves, e.g. transcription.</param>
/// <param name="Engine">onnx, llama.cpp or whisper.cpp.</param>
/// <param name="Description">One line on what it does for the editor.</param>
/// <param name="Licence">Its licence.</param>
/// <param name="Source">Where its files come from.</param>
/// <param name="Variants">The ways to run it.</param>
public sealed record ModelEntry(
    int Schema,
    string Id,
    string Name,
    string Feature,
    string Engine,
    string? Description,
    ModelLicence Licence,
    ModelSource Source,
    IReadOnlyList<ModelVariant> Variants)
{
    /// <summary>The variant offered first: the one marked default, else the first.</summary>
    public ModelVariant DefaultVariant => Variants.FirstOrDefault(v => v.Default) ?? Variants[0];
}

/// <summary>
/// WT100: the models WinTimeline knows, with their licence, size, needs and pinned download
/// source. The catalog ships inside the app; a newer one replaces it when published. An entry
/// whose schema this build does not know is skipped, not misread, so an older app keeps working
/// with a newer catalog.
/// </summary>
/// <param name="CatalogVersion">Raised on every published change.</param>
/// <param name="Updated">When it was last changed.</param>
/// <param name="Models">The entries this build understands.</param>
/// <param name="Skipped">How many entries were of a newer schema.</param>
public sealed record ModelCatalog(int CatalogVersion, string Updated, IReadOnlyList<ModelEntry> Models, int Skipped)
{
    /// <summary>The entry schema this build reads.</summary>
    public const int KnownSchema = 1;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The catalog shipped inside the app.</summary>
    /// <returns>The catalog.</returns>
    public static ModelCatalog Shipped()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinTimeline.Inference.models.json")
            ?? throw new InvalidOperationException("the shipped model catalog is missing from the build");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Reads a catalog.</summary>
    /// <param name="json">Its text.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidDataException">It is not a catalog, or an entry it knows is malformed.</exception>
    public static ModelCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<ModelEntry> models = [];
            int skipped = 0;
            foreach (JsonElement entry in root.GetProperty("models").EnumerateArray())
            {
                if (entry.GetProperty("schema").GetInt32() > KnownSchema)
                {
                    skipped++;
                    continue;
                }

                ModelEntry model = entry.Deserialize<ModelEntry>(Json) ?? throw new InvalidDataException("a model entry is null");
                Check(model);
                models.Add(model);
            }

            return new ModelCatalog(root.GetProperty("catalog_version").GetInt32(), root.GetProperty("updated").GetString() ?? string.Empty, models, skipped);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"the model catalog could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>The entry for an id, or null.</summary>
    /// <param name="id">e.g. silero-vad.</param>
    /// <returns>The entry.</returns>
    public ModelEntry? Find(string id) => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>What the schema cannot say: every URL is pinned to its entry's commit, ids are unique.</summary>
    private static void Check(ModelEntry model)
    {
        foreach (ModelFile file in model.Variants.SelectMany(v => v.Files))
        {
            string pinned = $"https://huggingface.co/{model.Source.Repo}/resolve/{model.Source.Revision}/{file.Path}";
            if (!string.Equals(file.Url, pinned, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{model.Id}'s {file.Path} is not pinned to {model.Source.Repo}@{model.Source.Revision}");
            }

            if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || file.Sha256.Any(char.IsUpper) || file.Size <= 0)
            {
                throw new InvalidDataException($"{model.Id}'s {file.Path} has no valid SHA-256 and size");
            }
        }

        if (model.Variants.Count == 0 || model.Variants.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() != model.Variants.Count)
        {
            throw new InvalidDataException($"{model.Id} needs at least one variant, each with its own id");
        }
    }
}
