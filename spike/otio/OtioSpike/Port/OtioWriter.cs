using System.Globalization;
using System.Text;

namespace OtioSpike.Port;

/// <summary>
/// Writes the model the way the reference's rapidjson PrettyWriter does, byte for byte where it
/// can: four-space indent, "key": value, [] and {} when empty, doubles in rapidjson's shortest
/// form with ".0" on whole numbers, and only quotes, backslashes and control characters escaped.
/// </summary>
internal sealed class OtioWriter
{
    private readonly StringBuilder _out = new();
    private readonly Stack<bool> _first = new();
    private int _depth;

    public static string Write(OtioObject root)
    {
        OtioWriter writer = new();
        writer.WriteValue(root);
        return writer._out.ToString();
    }

    public void Field(string key, object? value)
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
                Object("RationalTime.1", () => { Field("rate", t.Rate); Field("value", t.Value); });
                break;
            case TimeRange r:
                Object("TimeRange.1", () => { Field("duration", r.Duration); Field("start_time", r.StartTime); });
                break;
            case TimeTransform x:
                Object("TimeTransform.1", () => { Field("offset", x.Offset); Field("rate", x.Rate); Field("scale", x.Scale); });
                break;
            case Color c:
                Object("Color.1", () => { Field("r", c.R); Field("g", c.G); Field("b", c.B); Field("a", c.A); Field("name", c.Name); });
                break;
            case V2d v:
                Object("V2d.1", () => { Field("x", v.X); Field("y", v.Y); });
                break;
            case Box2d box:
                Object("Box2d.1", () => { Field("min", box.Min); Field("max", box.Max); });
                break;
            case OtioObject o:
                Object($"{o.Schema}.{o.Version}", () =>
                {
                    foreach ((string key, object? dynamic) in o.Dynamic)
                    {
                        Field(key, dynamic);
                    }

                    o.WriteFields(this);
                });
                break;
            case AnyDictionary dictionary:
                Object(null, () =>
                {
                    foreach ((string key, object? item) in dictionary)
                    {
                        Field(key, item);
                    }
                });
                break;
            case System.Collections.IEnumerable list:
                Array(list);
                break;
            default:
                throw new InvalidOperationException($"cannot write {value.GetType()}");
        }
    }

    private void Object(string? schema, Action body)
    {
        int before = _out.Length;
        _out.Append('{');
        _first.Push(true);
        _depth++;
        if (schema is not null)
        {
            Field("OTIO_SCHEMA", schema);
        }

        body();
        _depth--;
        bool empty = _first.Pop();
        if (empty)
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

    /// <summary>rapidjson's dtoa: shortest digits, then Prettify's choice of fixed or exponent form.</summary>
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

        string sign = value < 0 ? "-" : "";
        // Shortest round-trip digits, as Grisu2 produces them.
        string shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int e = shortest.IndexOfAny(['E', 'e']);
        string mantissa = e < 0 ? shortest : shortest[..e];
        int exponent = e < 0 ? 0 : int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture);
        int point = mantissa.IndexOf('.');
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

        return sign + body;
    }
}
