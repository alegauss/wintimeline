using System.Globalization;
using System.Text;
using System.Text.Json;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Serialization;

/// <summary>
/// Reads OpenTimelineIO JSON into the model (WT12, from the WT11 spike's port, which matched the
/// reference library's output on the whole corpus). It knows the reference's schema registry
/// (0.18.1), its four aliases and its two upgrade functions (Clip 1 to 2, Marker 1 to 2); a
/// schema it does not know becomes an <see cref="UnknownSchema"/> with every field kept. Numbers
/// keep full precision, which the reference's own parser does not. Writing is WT13's.
/// </summary>
public static class OtioReader
{
    // A bare NaN, Inf, Infinity or their negatives, which rapidjson reads (kParseNanAndInfFlag)
    // and System.Text.Json refuses, crosses as a string carrying this prefix.
    private const string NonFinite = "\u0001otio-nonfinite:";

    private static readonly string[] NonFiniteLiterals = ["NaN", "Infinity", "-Infinity", "Inf", "-Inf"];

    /// <summary>Reads a .otio file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its top object: usually a <see cref="Timeline"/>.</returns>
    /// <exception cref="InvalidDataException">The file is not OpenTimelineIO JSON.</exception>
    public static ModelObject ReadFile(string path) => Read(File.ReadAllBytes(path), path);

    /// <summary>Reads OpenTimelineIO JSON text.</summary>
    /// <param name="json">The text.</param>
    /// <returns>Its top object.</returns>
    /// <exception cref="InvalidDataException">The text is not OpenTimelineIO JSON.</exception>
    public static ModelObject ReadString(string json) => Read(Encoding.UTF8.GetBytes(json), "the text");

    private static ModelObject Read(byte[] bytes, string source)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(QuoteNonFinite(bytes), new JsonDocumentOptions { MaxDepth = 512 });
            return Value(document.RootElement) as ModelObject
                   ?? throw new InvalidDataException($"{source} does not hold an OpenTimelineIO object");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => String(element.GetString()!),
        JsonValueKind.Number => Number(element),
        JsonValueKind.Array => element.EnumerateArray().Select(Value).ToList().AsReadOnly(),
        JsonValueKind.Object => Object(element),
        _ => throw new InvalidDataException($"unexpected {element.ValueKind}"),
    };

    private static object String(string text) =>
        !text.StartsWith(NonFinite, StringComparison.Ordinal) ? text : text[NonFinite.Length..] switch
        {
            "NaN" => double.NaN,
            "Infinity" or "Inf" => double.PositiveInfinity,
            _ => double.NegativeInfinity,
        };

    /// <summary>rapidjson's distinction: a number written without a point or exponent is an integer.</summary>
    private static object Number(JsonElement element)
    {
        string raw = element.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0 && element.TryGetInt64(out long whole))
        {
            return whole;
        }

        return double.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static object? Object(JsonElement element)
    {
        Metadata fields = new();
        string? schema = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("OTIO_SCHEMA"))
            {
                schema = property.Value.GetString();
            }
            else
            {
                fields.Set(property.Name, Value(property.Value));
            }
        }

        return schema is null ? fields : Build(schema, fields);
    }

    private static object? Build(string schema, Metadata fields)
    {
        int dot = schema.LastIndexOf('.');
        string name = dot < 0 ? schema : schema[..dot];
        int version = dot >= 0 && int.TryParse(schema[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 1;

        switch (name)
        {
            case "RationalTime":
                return new RationalTime(D(fields, "value"), D(fields, "rate", 1));
            case "TimeRange":
                return new TimeRange(T(fields, "start_time"), T(fields, "duration"));
            case "TimeTransform":
                return new TimeTransform(T(fields, "offset"), D(fields, "scale", 1), D(fields, "rate", -1));
            case "Color":
                return new Color(D(fields, "r", 1), D(fields, "g", 1), D(fields, "b", 1), D(fields, "a", 1), fields.TryGetValue("name", out object? n) ? n as string ?? string.Empty : string.Empty);
            case "V2d":
                return new V2d(D(fields, "x"), D(fields, "y"));
            case "Box2d":
                return new Box2d(fields.TryGetValue("min", out object? min) ? min as V2d? ?? default : default, fields.TryGetValue("max", out object? max) ? max as V2d? ?? default : default);
        }

        if (name == "Clip" && version < 2)
        {
            object? reference = fields.Take("media_reference");
            Metadata references = new();
            references.Set(Clip.DefaultMedia, reference ?? new MissingReference(new Metadata()));
            fields.Set("media_references", references);
            fields.Set("active_media_reference_key", Clip.DefaultMedia);
        }

        if (name == "Marker" && version < 2 && fields.ContainsKey("range"))
        {
            fields.Set("marked_range", fields.Take("range"));
        }

        return name switch
        {
            "Timeline" => new Timeline(fields),
            "Stack" => new Stack(fields),
            "Track" or "Sequence" => new Track(fields),
            "Clip" => new Clip(fields),
            "Gap" or "Filler" => new Gap(fields),
            "Transition" => new Transition(fields),
            "Effect" => new Effect(fields),
            "LinearTimeWarp" or "FreezeFrame" => new LinearTimeWarp(fields, name),
            "Marker" => new Marker(fields),
            "ExternalReference" => new ExternalReference(fields),
            "MissingReference" => new MissingReference(fields),
            "GeneratorReference" => new GeneratorReference(fields),
            "ImageSequenceReference" => new ImageSequenceReference(fields),
            "SerializableCollection" or "SerializeableCollection" => new SerializableCollection(fields),
            _ => new UnknownSchema(name, version, fields),
        };
    }

    private static double D(Metadata fields, string key, double fallback = 0) =>
        fields.TryGetValue(key, out object? value) ? ModelObject.Number(value, fallback) : fallback;

    private static RationalTime T(Metadata fields, string key) =>
        fields.TryGetValue(key, out object? value) && value is RationalTime time ? time : new RationalTime();

    /// <summary>Rewrites the bare non-finite literals outside strings; returns the input when there are none.</summary>
    private static byte[] QuoteNonFinite(byte[] json)
    {
        List<(int Start, int Length)>? found = null;
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            byte b = json[i];
            if (inString)
            {
                if (b == (byte)'\\')
                {
                    i++;
                }
                else if (b == (byte)'"')
                {
                    inString = false;
                }

                continue;
            }

            if (b == (byte)'"')
            {
                inString = true;
            }
            else if (b is (byte)'N' or (byte)'I' or (byte)'-')
            {
                foreach (string literal in NonFiniteLiterals)
                {
                    if (json.AsSpan(i).StartsWith(Encoding.ASCII.GetBytes(literal)))
                    {
                        (found ??= []).Add((i, literal.Length));
                        i += literal.Length - 1;
                        break;
                    }
                }
            }
        }

        if (found is null)
        {
            return json;
        }

        using MemoryStream rewritten = new(json.Length + (found.Count * 32));
        int at = 0;
        byte[] prefix = Encoding.UTF8.GetBytes("\"\\u0001otio-nonfinite:");
        foreach ((int start, int length) in found)
        {
            rewritten.Write(json, at, start - at);
            rewritten.Write(prefix);
            rewritten.Write(json, start, length);
            rewritten.WriteByte((byte)'"');
            at = start + length;
        }

        rewritten.Write(json, at, json.Length - at);
        return rewritten.ToArray();
    }
}
