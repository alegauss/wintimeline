using System.IO;
using System.Text.Json;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT11, WT12: every file of OpenTimelineIO's sample corpus loads into the model, and every range,
/// duration and timecode the model computes is the one the reference library computed for the
/// same file — to the bit, refusals and Nones included.
/// </summary>
public sealed class CorpusTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "otio");

    public static TheoryData<string> CorpusFiles =>
        [.. Directory.GetFiles(Path.Combine(Fixtures, "corpus"), "*.otio").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheWholeCorpusIsHere()
    {
        Assert.Equal(13, CorpusFiles.Count);
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EveryComputedRangeMatchesTheReference(string name)
    {
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected", name + ".json")));
        JsonElement root = expected.RootElement;
        ModelObject read = OtioReader.ReadFile(Path.Combine(Fixtures, "corpus", name + ".otio"));

        Assert.Equal(root.GetProperty("schema").GetString(), read.Schema);
        if (read is not Timeline timeline)
        {
            return;
        }

        List<string> differences = [];
        Check(differences, "timeline duration", root.GetProperty("duration"), () => Time(timeline.Duration));
        Check(differences, "clips", root.GetProperty("clips"), () => timeline.FindClips().Count());
        Check(differences, "global start timecode", root.GetProperty("global_start_timecode"),
            () => timeline.GlobalStartTime is { } start ? start.ToTimecode(start.Rate) : null);

        List<(string Path, Composable Child)> children = [];
        Walk(timeline.Tracks, string.Empty, children);
        JsonElement[] wanted = [.. root.GetProperty("children").EnumerateArray()];
        Assert.Equal(wanted.Length, children.Count);

        for (int i = 0; i < wanted.Length; i++)
        {
            JsonElement entry = wanted[i];
            (string path, Composable child) = children[i];
            string at = $"{path} {child.Schema} '{child.Name}'";
            Assert.Equal(entry.GetProperty("path").GetString(), path);
            Assert.Equal(entry.GetProperty("schema").GetString(), child.Schema);
            Assert.Equal(entry.GetProperty("name").GetString(), child.Name);

            Check(differences, at + " duration", entry.GetProperty("duration"), () => Time(child.Duration));
            Check(differences, at + " range_in_parent", entry.GetProperty("range_in_parent"), () => Range(child.RangeInParent()));
            Check(differences, at + " trimmed_range_in_parent", entry.GetProperty("trimmed_range_in_parent"), () => Range(child.TrimmedRangeInParent()));
            if (child is Item item)
            {
                Check(differences, at + " available_range", entry.GetProperty("available_range"), () => Range(item.AvailableRange()));
                Check(differences, at + " trimmed_range", entry.GetProperty("trimmed_range"), () => Range(item.TrimmedRange()));
            }

            if (entry.TryGetProperty("source_start_timecode", out JsonElement timecode) && child is Clip { SourceRange: { } source })
            {
                Check(differences, at + " source timecode", timecode, () => SafeTimecode(source.StartTime));
            }
        }

        Assert.True(differences.Count == 0, $"{name}: {differences.Count} difference(s):\n" + string.Join("\n", differences));
    }

    private static string SafeTimecode(RationalTime time)
    {
        try
        {
            return time.ToTimecode(time.Rate);
        }
        catch (TimecodeException)
        {
            return "error";
        }
    }

    private static void Walk(Composition composition, string path, List<(string, Composable)> into)
    {
        for (int i = 0; i < composition.Children.Count; i++)
        {
            Composable child = composition.Children[i];
            string here = path.Length == 0 ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{path}/{i}";
            into.Add((here, child));
            if (child is Composition inner)
            {
                Walk(inner, here, into);
            }
        }
    }

    // What the model answers, as the JSON the reference's answer was written in; a refusal is "error".
    private static void Check(List<string> differences, string what, JsonElement expected, Func<object?> actual)
    {
        object? answer;
        try
        {
            answer = actual();
        }
        catch (TimelineRangeException)
        {
            answer = "error";
        }

        string got = JsonSerializer.Serialize(answer);
        string want = JsonSerializer.Serialize(Normalise(expected));
        if (got != want)
        {
            differences.Add($"{what}: reference {want}, model {got}");
        }
    }

    private static object? Normalise(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt32(out int whole) && !element.GetRawText().Contains('.', StringComparison.Ordinal) => whole,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(Normalise).ToArray(),
        _ => element.GetRawText(),
    };

    private static double[] Time(RationalTime time) => [time.Value, time.Rate];

    private static double[][]? Range(TimeRange? range) =>
        range is { } r ? [Time(r.StartTime), Time(r.Duration)] : null;
}
