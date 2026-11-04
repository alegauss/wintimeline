using System.IO;
using System.Text.Json.Nodes;
using Json.Schema;

namespace WinTimeline.Inference.Tests;

/// <summary>WT100: the shipped catalog is valid, pinned, and readable by an older app.</summary>
public sealed class CatalogTests
{
    private static readonly string Folder = Path.Combine(Repository.Root, "src", "WinTimeline.Inference", "Catalog");

    [Fact]
    public void TheShippedCatalogValidatesAgainstItsSchema()
    {
        JsonSchema schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(Folder, "models.schema.json")));
        JsonNode catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "models.json")))!;

        EvaluationResults results = schema.Evaluate(catalog, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid, string.Join(Environment.NewLine, results.Details.Where(d => d.HasErrors).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))));
    }

    [Fact]
    public void TheSchemaRefusesAnUnpinnedUrlOrABadHash()
    {
        JsonSchema schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(Folder, "models.schema.json")));
        JsonNode catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "models.json")))!;
        JsonNode file = catalog["models"]![0]!["variants"]![0]!["files"]![0]!;
        file["url"] = "https://huggingface.co/onnx-community/silero-vad/resolve/main/onnx/model_int8.onnx";
        file["sha256"] = "not a hash";

        Assert.False(schema.Evaluate(catalog).IsValid);
    }

    [Fact]
    public void EveryShippedModelHasALicenceADefaultVariantAndAPinnedFile()
    {
        ModelCatalog catalog = ModelCatalog.Shipped();

        Assert.Equal(["silero-vad", "clip-vit-b32", "whisper-base", "qwen2.5-1.5b-instruct"], catalog.Models.Select(m => m.Id));
        Assert.All(catalog.Models, m => Assert.False(string.IsNullOrEmpty(m.Licence.Spdx)));
        Assert.Equal("int8", catalog.Find("silero-vad")!.DefaultVariant.Id);
        Assert.Equal(303420453, catalog.Find("clip-vit-b32")!.DefaultVariant.DownloadBytes);
        Assert.Equal(0, catalog.Skipped);
    }

    [Fact]
    public void AnEntryOfANewerSchemaIsSkippedNotMisread()
    {
        JsonNode catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "models.json")))!;
        catalog["models"]!.AsArray().Add(new JsonObject { ["schema"] = 2, ["id"] = "future", ["shape"] = "unknown" });

        ModelCatalog read = ModelCatalog.Parse(catalog.ToJsonString());

        Assert.Equal(4, read.Models.Count);
        Assert.Equal(1, read.Skipped);
    }

    [Fact]
    public void AFileNotPinnedToItsEntrysCommitIsRefused()
    {
        JsonNode catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "models.json")))!;
        catalog["models"]![1]!["source"]!["revision"] = new string('0', 40);

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(catalog.ToJsonString()));
        Assert.Contains("is not pinned to Xenova/clip-vit-base-patch32@0000", refused.Message, StringComparison.Ordinal);
    }
}
