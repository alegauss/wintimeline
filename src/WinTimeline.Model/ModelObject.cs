namespace WinTimeline.Model;

/// <summary>What changed in a document: the object, and the name of what changed on it.</summary>
/// <param name="Target">The object that changed.</param>
/// <param name="What">The field or collection, e.g. "source_range" or "children".</param>
public sealed record ModelChange(ModelObject Target, string What);

/// <summary>
/// OpenTimelineIO's SerializableObject (WT12): a schema name and version, and the fields no class
/// claimed, which the reference keeps as dynamic fields and writes back first. Every object knows
/// what holds it, so a change is announced once, by the timeline at the top, at the granularity
/// of the object that changed.
/// </summary>
public abstract class ModelObject
{
    /// <summary>The fields this build does not know, kept so a save writes them back.</summary>
    public Metadata DynamicFields { get; } = new();

    /// <summary>The schema name, e.g. "Clip".</summary>
    public abstract string Schema { get; }

    /// <summary>The schema version this build writes.</summary>
    public virtual int Version => 1;

    /// <summary>What holds this object: a composition, a clip, an item, a timeline; null at the top.</summary>
    public ModelObject? Owner { get; internal set; }

    /// <summary>Announces a change to whatever timeline holds this object.</summary>
    internal void Changed(string what)
    {
        // The nearest timeline above, not the topmost owner: in a collection the topmost is the
        // collection, and an edit to one of its reels must still be announced by that reel (WT160).
        ModelObject? at = this;
        while (at is not null and not Timeline)
        {
            at = at.Owner;
        }

        (at as Timeline)?.Announce(new ModelChange(this, what));
    }

    /// <summary>Moves the fields no class claimed into <see cref="DynamicFields"/>.</summary>
    internal void Keep(Metadata rest)
    {
        foreach ((string key, object? value) in rest)
        {
            DynamicFields.Set(key, value);
        }
    }

    internal static double Number(object? value, double fallback = 0) => value switch
    {
        long l => l,
        double d => d,
        _ => fallback,
    };

    internal static string Text(object? value, string fallback = "") => value as string ?? fallback;

    internal static bool Flag(object? value, bool fallback = true) => value is bool b ? b : fallback;

    internal static List<T> ListOf<T>(object? value) =>
        value is IEnumerable<object?> list ? [.. list.OfType<T>()] : [];
}

/// <summary>A schema this build does not know, kept whole so a save writes it back as it was read.</summary>
public sealed class UnknownSchema : ModelObject
{
    internal UnknownSchema(string schema, int version, Metadata data)
    {
        Schema = schema;
        Version = version;
        Data = data;
    }

    /// <inheritdoc/>
    public override string Schema { get; }

    /// <inheritdoc/>
    public override int Version { get; }

    /// <summary>Every field the file had.</summary>
    public Metadata Data { get; }
}

/// <summary>SerializableObjectWithMetadata: a name and a free metadata dictionary other tools store state in.</summary>
public abstract class NamedObject : ModelObject
{
    private protected NamedObject(Metadata fields)
    {
        Metadata = fields.Take("metadata") as Metadata ?? new Metadata();
        Name = Text(fields.Take("name"));
    }

    /// <summary>Metadata, preserved verbatim.</summary>
    public Metadata Metadata { get; }

    /// <summary>The name.</summary>
    public string Name { get; internal set; }
}
