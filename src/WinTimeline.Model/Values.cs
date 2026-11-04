using WinTimeline.Model.Time;

namespace WinTimeline.Model;

// The value types OpenTimelineIO carries inside objects and metadata. A metadata value is one of:
// null, bool, long, double, string, IReadOnlyList<object?>, Metadata, one of the records below,
// a RationalTime or TimeRange, or a ModelObject.

/// <summary>opentime's TimeTransform: an offset, a scale and a rate.</summary>
/// <param name="Offset">The offset.</param>
/// <param name="Scale">The scale.</param>
/// <param name="Rate">The rate, or -1 for none.</param>
public readonly record struct TimeTransform(RationalTime Offset, double Scale, double Rate);

/// <summary>An item's colour (Color.1).</summary>
/// <param name="R">Red, 0 to 1.</param>
/// <param name="G">Green, 0 to 1.</param>
/// <param name="B">Blue, 0 to 1.</param>
/// <param name="A">Alpha, 0 to 1.</param>
/// <param name="Name">Its name, or empty.</param>
public sealed record Color(double R, double G, double B, double A, string Name);

/// <summary>Imath's V2d (V2d.1).</summary>
/// <param name="X">Across.</param>
/// <param name="Y">Down.</param>
public readonly record struct V2d(double X, double Y);

/// <summary>Imath's Box2d (Box2d.1): an image's bounds.</summary>
/// <param name="Min">The lower corner.</param>
/// <param name="Max">The upper corner.</param>
public readonly record struct Box2d(V2d Min, V2d Max);

/// <summary>
/// OpenTimelineIO's AnyDictionary: a std::map, so its keys are always in ordinal order, and a
/// file's metadata comes back out in the order the reference writes it. Read-only to everything
/// outside the model; a change goes through a command.
/// </summary>
public sealed class Metadata : IReadOnlyDictionary<string, object?>
{
    private readonly SortedDictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int Count => _values.Count;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _values.Keys;

    /// <inheritdoc/>
    public IEnumerable<object?> Values => _values.Values;

    /// <inheritdoc/>
    public object? this[string key] => _values[key];

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Set(string key, object? value) => _values[key] = value;

    internal bool Remove(string key) => _values.Remove(key);

    /// <summary>Takes a key out, so what is left at the end of a read is what no field claimed.</summary>
    internal object? Take(string key) => _values.Remove(key, out object? value) ? value : null;
}
