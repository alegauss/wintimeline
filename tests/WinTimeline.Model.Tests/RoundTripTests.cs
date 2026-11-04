using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT13: what the model writes is what the reference library writes, byte for byte, for every file
/// of the corpus; nothing a file holds is lost; and a save replaces the file in one step.
/// </summary>
public sealed class RoundTripTests : IDisposable
{
    // The reference parses doubles without rapidjson's full-precision flag, so these two come back
    // from it an ulp or so away from what the file says (WT11). The model keeps the file's value;
    // for them, equality allowing a few ulps is the claim.
    private static readonly string[] ReferenceLosesPrecision = ["big_int", "effects"];

    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "otio");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt13-" + Guid.NewGuid().ToString("N"));

    public static TheoryData<string> CorpusFiles => CorpusTests.CorpusFiles;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EveryCorpusFileIsWrittenAsTheReferenceWritesIt(string name)
    {
        string reference = File.ReadAllText(Path.Combine(Fixtures, "written", name + ".otio"), Encoding.UTF8);

        string written = OtioWriter.Write(OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", name + ".otio")));

        if (ReferenceLosesPrecision.Contains(name, StringComparer.Ordinal))
        {
            Assert.True(Near(Parse(reference), Parse(written)), $"{name} differs from the reference by more than a few ulps");
        }
        else
        {
            Assert.Equal(reference, written);
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void WritingWhatWasWrittenChangesNothing(string name)
    {
        string once = OtioWriter.Write(OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", name + ".otio")));

        Assert.Equal(once, OtioWriter.Write(OtioReader.ReadString(once)));
    }

    [Fact]
    public void AnUnknownSchemaSurvivesUntouched()
    {
        // Written by the reference with MyStudio.Note.3 and a MyStudio.Reply.1 inside it.
        string file = File.ReadAllText(Path.Combine(Fixtures, "unknown-schema.otio"), Encoding.UTF8);

        Assert.Equal(file, OtioWriter.Write(OtioReader.ReadString(file)));
        Assert.Contains("\"OTIO_SCHEMA\": \"MyStudio.Note.3\"", file, StringComparison.Ordinal);
    }

    [Fact]
    public void AClipIsWrittenAtVersionOneForAnOlderReaderAsTheReferenceDowngradesIt()
    {
        string reference = File.ReadAllText(Path.Combine(Fixtures, "written", "simple_cut.clip1.otio"), Encoding.UTF8);
        ModelObject timeline = OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", "simple_cut.otio"));

        Assert.Equal(reference, OtioWriter.Write(timeline, new Dictionary<string, int> { ["Clip"] = 1 }));
    }

    [Fact]
    public void AVersionWithNoDowngradeIsRefused()
    {
        ModelObject timeline = OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", "simple_cut.otio"));

        Assert.Throws<NotSupportedException>(() => OtioWriter.Write(timeline, new Dictionary<string, int> { ["Clip"] = 0 }));
    }

    [Fact]
    public void ASaveReplacesTheFileInOneStepAndKeepsThePreviousAsBak()
    {
        string path = Path.Combine(_folder, "cut.otio");
        ModelObject first = OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", "simple_cut.otio"));
        ModelObject second = OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", "transition.otio"));

        OtioFile.Save(first, path);
        Assert.False(File.Exists(path + ".bak"));
        OtioFile.Save(second, path);

        Assert.Equal(OtioWriter.Write(second), File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(OtioWriter.Write(first), File.ReadAllText(path + ".bak", Encoding.UTF8));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("Timeline", OtioFile.Open(path).Schema);
    }

    // The reference writes bare NaN and Infinity, which System.Text.Json refuses: quote them to compare.
    private static JsonNode? Parse(string json) =>
        JsonNode.Parse(System.Text.RegularExpressions.Regex.Replace(json, @"(?<=[:\[,]\s*)(-?Infinity|NaN)", "\"$1\""));

    private static bool Near(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(p => y.TryGetPropertyValue(p.Key, out JsonNode? o) && Near(p.Value, o)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(p => Near(p.First, p.Second)),
        (JsonValue x, JsonValue y) when x.TryGetValue(out double p) && y.TryGetValue(out double q) =>
            Math.Abs(BitConverter.DoubleToInt64Bits(p) - BitConverter.DoubleToInt64Bits(q)) <= 4,
        _ => JsonNode.DeepEquals(a, b),
    };
}
