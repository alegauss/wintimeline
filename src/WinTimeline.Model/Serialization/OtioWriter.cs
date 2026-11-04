using System.Globalization;
using System.Text;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Serialization;

/// <summary>
/// Writes the model as OpenTimelineIO JSON, laid out the way the reference's rapidjson writer lays
/// it out (WT13, from the WT11 spike, which matched the reference byte for byte): four-space
/// indent, <c>"key": value</c>, <c>[]</c> and <c>{}</c> when empty, doubles in the shortest form
/// with ".0" on whole numbers, and only quotes, backslashes and control characters escaped.
///
/// <para>Each schema's fields come out in the order its C++ <c>write_to</c> writes them, after the
/// dynamic fields. An object written at an older schema version (<see cref="Write"/>'s
/// <c>targetVersions</c>) goes through the reference's downgrade function and is written, as the
/// reference does, from a dictionary, so its keys are sorted.</para>
/// </summary>
public sealed class OtioWriter
{
    private readonly StringBuilder _out = new();
    private readonly Stack<bool> _first = new();
    private readonly IReadOnlyDictionary<string, int> _targets;
    private int _depth;

    private OtioWriter(IReadOnlyDictionary<string, int>? targets) =>
        _targets = targets ?? new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The object as OpenTimelineIO JSON text.</summary>
    /// <param name="root">The object, usually a <see cref="Timeline"/>.</param>
    /// <param name="targetVersions">Schema versions to write older objects at, e.g. Clip = 1, as <c>otio.versioning</c> names them.</param>
    /// <returns>The text, with LF line ends and no final newline, as the reference writes it.</returns>
    /// <exception cref="NotSupportedException">A target version has no downgrade.</exception>
    public static string Write(ModelObject root, IReadOnlyDictionary<string, int>? targetVersions = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        OtioWriter writer = new(targetVersions);
        writer.WriteValue(root);
        return writer._out.ToString();
    }

    /// <summary>The fields of an object, in the order the reference writes them.</summary>
    internal static IEnumerable<(string Key, object? Value)> Fields(ModelObject o)
    {
        foreach ((string key, object? value) in o.DynamicFields)
        {
            yield return (key, value);
        }

        if (o is UnknownSchema unknown)
        {
            foreach ((string key, object? value) in unknown.Data)
            {
                yield return (key, value);
            }

            yield break;
        }

        if (o is NamedObject named)
        {
            yield return ("metadata", named.Metadata);
            yield return ("name", named.Name);
        }

        if (o is Item item)
        {
            yield return ("source_range", item.SourceRange);
            yield return ("effects", item.Effects);
            yield return ("markers", item.Markers);
            yield return ("enabled", item.Enabled);
            yield return ("color", item.Color);
        }

        IEnumerable<(string, object?)> own = o switch
        {
            Track t => [("children", Held(t)), ("kind", t.Kind)],
            Composition c => [("children", Held(c))],
            Clip clip => [("media_references", clip.MediaReferences), ("active_media_reference_key", clip.ActiveMediaReferenceKey)],
            Transition tr => [("in_offset", tr.InOffset), ("out_offset", tr.OutOffset), ("transition_type", tr.TransitionType)],
            LinearTimeWarp w => [("effect_name", w.EffectName), ("enabled", w.Enabled), ("time_scalar", w.TimeScalar)],
            Effect e => [("effect_name", e.EffectName), ("enabled", e.Enabled)],
            Marker m => [("color", m.Color), ("marked_range", m.MarkedRange), ("comment", m.Comment)],
            MediaReference r => [("available_range", r.AvailableRange), ("available_image_bounds", r.AvailableImageBounds), .. ReferenceFields(r)],
            Timeline tl => [("global_start_time", tl.GlobalStartTime), ("tracks", tl.Tracks)],
            SerializableCollection sc => [("children", sc.Children)],
            _ => [],
        };

        foreach ((string, object?) field in own)
        {
            yield return field;
        }
    }

    internal static string Double(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return double.IsNegative(value) ? "-0.0" : "0.0";
        }

        // Shortest round-trip digits, as Grisu2 produces them, then rapidjson's Prettify.
        string shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int e = shortest.IndexOfAny(['E', 'e']);
        string mantissa = e < 0 ? shortest : shortest[..e];
        int exponent = e < 0 ? 0 : int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture);
        int point = mantissa.IndexOf('.', StringComparison.Ordinal);
        string digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        int k = exponent - (point < 0 ? 0 : mantissa.Length - point - 1);
        string trimmed = digits.TrimStart('0');
        string stripped = trimmed.TrimEnd('0');
        k += trimmed.Length - stripped.Length;
        digits = stripped.Length == 0 ? "0" : stripped;

        int length = digits.Length;
        int kk = length + k;
        string body;
        if (k >= 0 && kk <= 21)
        {
            body = digits + new string('0', k) + ".0";
        }
        else if (kk > 0 && kk <= 21)
        {
            body = digits[..kk] + "." + digits[kk..];
        }
        else if (kk > -6 && kk <= 0)
        {
            body = "0." + new string('0', -kk) + digits;
        }
        else if (length == 1)
        {
            body = digits + "e" + (kk - 1).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            body = digits[0] + "." + digits[1..] + "e" + (kk - 1).ToString(CultureInfo.InvariantCulture);
        }

        return (value < 0 ? "-" : string.Empty) + body;
    }

    // A composition's children, with any of a schema this build does not know kept at the end.
    private static List<ModelObject> Held(Composition composition) =>
        [.. composition.Children.Cast<ModelObject>(), .. composition.UnknownChildren];

    private static IEnumerable<(string, object?)> ReferenceFields(MediaReference reference) => reference switch
    {
        ExternalReference x => [("target_url", x.TargetUrl)],
        GeneratorReference g => [("generator_kind", g.GeneratorKind), ("parameters", g.Parameters)],
        ImageSequenceReference s =>
        [
            ("target_url_base", s.TargetUrlBase), ("name_prefix", s.NamePrefix), ("name_suffix", s.NameSuffix),
            ("start_frame", (long)s.StartFrame), ("frame_step", (long)s.FrameStep), ("rate", s.Rate),
            ("frame_zero_padding", (long)s.FrameZeroPadding), ("missing_frame_policy", s.MissingFramePolicy),
        ],
        _ => [],
    };

    private void Field(string key, object? value)
    {
        Separator();
        String(key);
        _out.Append(": ");
        WriteValue(value);
    }

    private void WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                _out.Append("null");
                break;
            case bool b:
                _out.Append(b ? "true" : "false");
                break;
            case long l:
                _out.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case int i:
                _out.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case double d:
                _out.Append(Double(d));
                break;
            case string s:
                String(s);
                break;
            case RationalTime t:
                Object("RationalTime.1", [("rate", t.Rate), ("value", t.Value)]);
                break;
            case TimeRange r:
                Object("TimeRange.1", [("duration", r.Duration), ("start_time", r.StartTime)]);
                break;
            case TimeTransform x:
                Object("TimeTransform.1", [("offset", x.Offset), ("rate", x.Rate), ("scale", x.Scale)]);
                break;
            case Color c:
                Object("Color.1", [("r", c.R), ("g", c.G), ("b", c.B), ("a", c.A), ("name", c.Name)]);
                break;
            case V2d v:
                Object("V2d.1", [("x", v.X), ("y", v.Y)]);
                break;
            case Box2d box:
                Object("Box2d.1", [("min", box.Min), ("max", box.Max)]);
                break;
            case ModelObject o:
                WriteObject(o);
                break;
            case Metadata dictionary:
                Object(null, dictionary.Select(p => (p.Key, p.Value)));
                break;
            case System.Collections.IEnumerable list:
                Array(list);
                break;
            default:
                throw new InvalidOperationException($"cannot write {value.GetType()}");
        }
    }

    private void WriteObject(ModelObject o)
    {
        bool downgrade = _targets.TryGetValue(o.Schema, out int target) && target < o.Version;
        if (!downgrade && _asDictionary == 0)
        {
            Object($"{o.Schema}.{o.Version}", Fields(o));
            return;
        }

        // Down to an older version, or inside an object that was: the reference turns the object
        // into a dictionary, all the way down, so every key under it comes out sorted.
        SortedDictionary<string, object?> fields = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in Fields(o))
        {
            fields[key] = value;
        }

        if (downgrade)
        {
            switch (o.Schema, target)
            {
                case ("Clip", 1):
                    Clip clip = (Clip)o;
                    fields["media_reference"] = clip.MediaReference;
                    fields.Remove("media_references");
                    fields.Remove("active_media_reference_key");
                    break;
                default:
                    throw new NotSupportedException($"OpenTimelineIO has no downgrade of {o.Schema}.{o.Version} to version {target}");
            }
        }

        _asDictionary++;
        Object($"{o.Schema}.{(downgrade ? target : o.Version)}", fields.Select(p => (p.Key, p.Value)));
        _asDictionary--;
    }

    private int _asDictionary;

    private void Object(string? schema, IEnumerable<(string Key, object? Value)> fields)
    {
        int before = _out.Length;
        _out.Append('{');
        _first.Push(true);
        _depth++;
        if (schema is not null)
        {
            Field("OTIO_SCHEMA", schema);
        }

        foreach ((string key, object? value) in fields)
        {
            Field(key, value);
        }

        _depth--;
        if (_first.Pop())
        {
            _out.Length = before;
            _out.Append("{}");
            return;
        }

        Newline();
        _out.Append('}');
    }

    private void Array(System.Collections.IEnumerable list)
    {
        _out.Append('[');
        _first.Push(true);
        _depth++;
        foreach (object? item in list)
        {
            Separator();
            WriteValue(item);
        }

        _depth--;
        if (_first.Pop())
        {
            _out.Append(']');
            return;
        }

        Newline();
        _out.Append(']');
    }

    private void Separator()
    {
        if (!_first.Pop())
        {
            _out.Append(',');
        }

        _first.Push(false);
        Newline();
    }

    private void Newline()
    {
        _out.Append('\n');
        _out.Append(' ', _depth * 4);
    }

    private void String(string text)
    {
        _out.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': _out.Append("\\\""); break;
                case '\\': _out.Append("\\\\"); break;
                case '\b': _out.Append("\\b"); break;
                case '\f': _out.Append("\\f"); break;
                case '\n': _out.Append("\\n"); break;
                case '\r': _out.Append("\\r"); break;
                case '\t': _out.Append("\\t"); break;
                case < ' ': _out.Append("\\u00").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)); break;
                default: _out.Append(c); break;
            }
        }

        _out.Append('"');
    }
}
