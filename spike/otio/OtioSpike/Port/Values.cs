namespace OtioSpike.Port;

// The value types the OTIO schema carries inside objects and dictionaries. A dictionary value is
// one of: null, bool, long, double, string, List<object?>, AnyDictionary, one of the records
// below, or an OtioObject.

/// <summary>opentime's RationalTime: a value at a rate.</summary>
internal readonly record struct RationalTime(double Value, double Rate)
{
    public double Seconds => Value / Rate;

    /// <summary>The reference's addition: the lower-rate side is rescaled to the higher rate.</summary>
    public static RationalTime operator +(RationalTime a, RationalTime b) =>
        a.Rate < b.Rate ? new(a.Value * b.Rate / a.Rate + b.Value, b.Rate)
        : new(a.Value + b.Value * a.Rate / b.Rate, a.Rate);

    public RationalTime Add(RationalTime other) => this + other;
}

/// <summary>opentime's TimeRange.</summary>
internal readonly record struct TimeRange(RationalTime StartTime, RationalTime Duration);

/// <summary>opentime's TimeTransform.</summary>
internal readonly record struct TimeTransform(RationalTime Offset, double Scale, double Rate);

/// <summary>An item's colour (Color.1).</summary>
internal sealed record Color(double R, double G, double B, double A, string Name);

/// <summary>Imath's V2d (V2d.1).</summary>
internal readonly record struct V2d(double X, double Y);

/// <summary>Imath's Box2d (Box2d.1).</summary>
internal readonly record struct Box2d(V2d Min, V2d Max);

/// <summary>The reference's AnyDictionary: a std::map, so its keys are always written sorted.</summary>
internal sealed class AnyDictionary : SortedDictionary<string, object?>
{
    public AnyDictionary()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Takes a key out, so what is left at the end is what no field claimed.</summary>
    public object? Take(string key)
    {
        if (TryGetValue(key, out object? value))
        {
            Remove(key);
            return value;
        }

        return null;
    }

    public bool Has(string key) => ContainsKey(key);
}
