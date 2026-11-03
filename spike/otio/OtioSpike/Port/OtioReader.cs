using System.Globalization;
using System.Text.Json;

namespace OtioSpike.Port;

/// <summary>
/// Reads .otio JSON into the port's model: the schema registry, the aliases and the two upgrade
/// functions the reference registers (Clip 1 to 2, Marker 1 to 2), written out by hand.
/// </summary>
internal static class OtioReader
{
    // A bare NaN, Inf, Infinity or their negatives, which rapidjson reads (kParseNanAndInfFlag)
    // and System.Text.Json refuses, crosses as a string carrying this prefix.
    private const string NonFinite = "\u0001otio-nonfinite:";

    public static OtioObject ReadFile(string path)
    {
        byte[] bytes = QuoteNonFinite(File.ReadAllBytes(path));
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 512 });
        return Value(document.RootElement) as OtioObject
               ?? throw new InvalidDataException($"{path} does not hold an OTIO object");
    }

    /// <summary>JSON text System.Text.Json can parse, for comparing two outputs.</summary>
    internal static string Parseable(string json) =>
        System.Text.Encoding.UTF8.GetString(QuoteNonFinite(System.Text.Encoding.UTF8.GetBytes(json)));

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
                foreach (string literal in (ReadOnlySpan<string>)["NaN", "Infinity", "-Infinity", "Inf", "-Inf"])
                {
                    if (json.AsSpan(i).StartsWith(System.Text.Encoding.ASCII.GetBytes(literal)))
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
        byte[] prefix = System.Text.Encoding.UTF8.GetBytes("\"\\u0001otio-nonfinite:");
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

    private static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString() is { } s && s.StartsWith(NonFinite, StringComparison.Ordinal)
            ? s[NonFinite.Length..] switch
            {
                "NaN" => double.NaN,
                "Infinity" or "Inf" => double.PositiveInfinity,
                _ => double.NegativeInfinity,
            }
            : element.GetString(),
        JsonValueKind.Number => Number(element),
        JsonValueKind.Array => element.EnumerateArray().Select(Value).ToList(),
        JsonValueKind.Object => Object(element),
        _ => throw new InvalidDataException($"unexpected {element.ValueKind}"),
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
        AnyDictionary d = new();
        string? schema = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("OTIO_SCHEMA"))
            {
                schema = property.Value.GetString();
            }
            else
            {
                d[property.Name] = Value(property.Value);
            }
        }

        return schema is null ? d : Build(schema, d);
    }

    private static object? Build(string schema, AnyDictionary d)
    {
        int dot = schema.LastIndexOf('.');
        string name = dot < 0 ? schema : schema[..dot];
        int version = dot < 0 ? 1 : int.Parse(schema[(dot + 1)..], CultureInfo.InvariantCulture);

        switch (name)
        {
            case "RationalTime":
                return new RationalTime(D(d, "value"), D(d, "rate", 1));
            case "TimeRange":
                return new TimeRange(T(d, "start_time"), T(d, "duration"));
            case "TimeTransform":
                return new TimeTransform(T(d, "offset"), D(d, "scale", 1), D(d, "rate", -1));
            case "Color":
                return new Color(D(d, "r", 1), D(d, "g", 1), D(d, "b", 1), D(d, "a", 1), d.GetValueOrDefault("name") as string ?? "");
            case "V2d":
                return new V2d(D(d, "x"), D(d, "y"));
            case "Box2d":
                return new Box2d(d.GetValueOrDefault("min") as V2d? ?? default, d.GetValueOrDefault("max") as V2d? ?? default);
        }

        if (name == "Clip" && version < 2)
        {
            object? reference = d.Take("media_reference");
            d["media_references"] = new AnyDictionary { [Clip.DefaultMedia] = reference ?? new MissingReference(new()) };
            d["active_media_reference_key"] = Clip.DefaultMedia;
        }

        if (name == "Marker" && version < 2 && d.Has("range"))
        {
            d["marked_range"] = d.Take("range");
        }

        return name switch
        {
            "Timeline" => new Timeline(d),
            "Stack" => new Stack(d),
            "Track" or "Sequence" => new Track(d),
            "Clip" => new Clip(d),
            "Gap" or "Filler" => new Gap(d),
            "Transition" => new Transition(d),
            "Effect" => new Effect(d),
            "LinearTimeWarp" or "FreezeFrame" => new LinearTimeWarp(d, name),
            "Marker" => new Marker(d),
            "ExternalReference" => new ExternalReference(d),
            "MissingReference" => new MissingReference(d),
            "GeneratorReference" => new GeneratorReference(d),
            "SerializableCollection" or "SerializeableCollection" => new SerializableCollection(d),
            _ => new UnknownSchema(name, version, d),
        };
    }

    private static double D(AnyDictionary d, string key, double fallback = 0) => d.GetValueOrDefault(key) switch
    {
        long l => l,
        double v => v,
        _ => fallback,
    };

    private static RationalTime T(AnyDictionary d, string key) => d.GetValueOrDefault(key) as RationalTime? ?? default;
}
