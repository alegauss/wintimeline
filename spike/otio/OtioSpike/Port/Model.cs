namespace OtioSpike.Port;

/// <summary>
/// SerializableObject: a schema name and version, and the fields no class claimed, which the
/// reference keeps as "dynamic fields" and writes first, sorted.
/// </summary>
internal abstract class OtioObject
{
    public AnyDictionary Dynamic { get; } = new();

    public abstract string Schema { get; }

    public virtual int Version => 1;

    /// <summary>Writes the class's own fields in the reference's order (OTIO_SCHEMA and the dynamic fields are the writer's).</summary>
    public abstract void WriteFields(OtioWriter writer);

    protected static double Number(object? value, double fallback = 0) => value switch
    {
        long l => l,
        double d => d,
        _ => fallback,
    };

    protected static string Text(object? value, string fallback = "") => value as string ?? fallback;

    protected static bool Flag(object? value, bool fallback = true) => value is bool b ? b : fallback;

    protected static List<T> ListOf<T>(object? value) =>
        value is List<object?> list ? [.. list.OfType<T>()] : [];

    /// <summary>Called last by every reader: what no field took becomes a dynamic field.</summary>
    protected void Keep(AnyDictionary rest)
    {
        foreach ((string key, object? value) in rest)
        {
            Dynamic[key] = value;
        }
    }
}

/// <summary>A schema the port does not know, kept whole so it writes back as it was read.</summary>
internal sealed class UnknownSchema(string schema, int version, AnyDictionary data) : OtioObject
{
    public override string Schema { get; } = schema;

    public override int Version { get; } = version;

    public AnyDictionary Data { get; } = data;

    public override void WriteFields(OtioWriter writer)
    {
        foreach ((string key, object? value) in Data)
        {
            writer.Field(key, value);
        }
    }
}

/// <summary>SerializableObjectWithMetadata.</summary>
internal abstract class WithMetadata : OtioObject
{
    protected WithMetadata(AnyDictionary d)
    {
        Metadata = d.Take("metadata") as AnyDictionary ?? new();
        Name = Text(d.Take("name"));
    }

    public AnyDictionary Metadata { get; }

    public string Name { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        writer.Field("metadata", Metadata);
        writer.Field("name", Name);
    }
}

/// <summary>Item: anything with a range on a track.</summary>
internal abstract class Item : WithMetadata
{
    protected Item(AnyDictionary d)
        : base(d)
    {
        SourceRange = d.Take("source_range") as TimeRange?;
        Effects = ListOf<Effect>(d.Take("effects"));
        Markers = ListOf<Marker>(d.Take("markers"));
        Enabled = Flag(d.Take("enabled"));
        Color = d.Take("color") as Color;
    }

    public TimeRange? SourceRange { get; set; }

    public List<Effect> Effects { get; }

    public List<Marker> Markers { get; }

    public bool Enabled { get; set; }

    public Color? Color { get; set; }

    /// <summary>What the item plays: its source range, or else what it has available.</summary>
    public RationalTime Duration => (SourceRange ?? AvailableRange()).Duration;

    public abstract TimeRange AvailableRange();

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("source_range", SourceRange);
        writer.Field("effects", Effects);
        writer.Field("markers", Markers);
        writer.Field("enabled", Enabled);
        writer.Field("color", Color);
    }
}

/// <summary>Composition: an item holding children (Track and Stack).</summary>
internal abstract class Composition : Item
{
    protected Composition(AnyDictionary d)
        : base(d)
    {
        Children = ListOf<OtioObject>(d.Take("children"));
    }

    public List<OtioObject> Children { get; }

    public IEnumerable<Clip> FindClips()
    {
        foreach (OtioObject child in Children)
        {
            if (child is Clip clip)
            {
                yield return clip;
            }
            else if (child is Composition composition)
            {
                foreach (Clip inner in composition.FindClips())
                {
                    yield return inner;
                }
            }
        }
    }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("children", Children);
    }
}

internal sealed class Track : Composition
{
    public Track(AnyDictionary d)
        : base(d)
    {
        Kind = Text(d.Take("kind"), "Video");
        Keep(d);
    }

    public override string Schema => "Track";

    public string Kind { get; set; }

    /// <summary>The reference's: the children's durations in sequence, plus a leading or trailing transition's overhang.</summary>
    public override TimeRange AvailableRange()
    {
        RationalTime total = new(0, 1);
        foreach (OtioObject child in Children)
        {
            if (child is Item item)
            {
                total += item.Duration;
            }
        }

        if (Children.Count > 0 && Children[0] is Transition first)
        {
            total += first.InOffset;
        }

        if (Children.Count > 0 && Children[^1] is Transition last)
        {
            total += last.OutOffset;
        }

        return new TimeRange(new RationalTime(0, total.Rate), total);
    }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("kind", Kind);
    }
}

internal sealed class Stack : Composition
{
    public Stack(AnyDictionary d)
        : base(d)
    {
        Keep(d);
    }

    public override string Schema => "Stack";

    /// <summary>The reference's: the longest child.</summary>
    public override TimeRange AvailableRange()
    {
        RationalTime longest = new(0, 1);
        foreach (Item item in Children.OfType<Item>())
        {
            RationalTime duration = item.Duration;
            if (duration.Seconds > longest.Seconds)
            {
                longest = duration;
            }
        }

        return new TimeRange(new RationalTime(0, longest.Rate), longest);
    }
}

internal sealed class Clip : Item
{
    public const string DefaultMedia = "DEFAULT_MEDIA";

    public Clip(AnyDictionary d)
        : base(d)
    {
        MediaReferences = d.Take("media_references") as AnyDictionary ?? new();
        ActiveMediaReferenceKey = Text(d.Take("active_media_reference_key"), DefaultMedia);
        Keep(d);
    }

    public override string Schema => "Clip";

    public override int Version => 2;

    public AnyDictionary MediaReferences { get; }

    public string ActiveMediaReferenceKey { get; set; }

    public MediaReference? Media => MediaReferences.GetValueOrDefault(ActiveMediaReferenceKey) as MediaReference;

    public override TimeRange AvailableRange() =>
        Media?.AvailableRange ?? throw new InvalidDataException($"clip {Name} has no available range");

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("media_references", MediaReferences);
        writer.Field("active_media_reference_key", ActiveMediaReferenceKey);
    }
}

internal sealed class Gap : Item
{
    public Gap(AnyDictionary d)
        : base(d)
    {
        Keep(d);
    }

    public override string Schema => "Gap";

    public override TimeRange AvailableRange() => SourceRange ?? default;
}

internal sealed class Transition : WithMetadata
{
    public Transition(AnyDictionary d)
        : base(d)
    {
        InOffset = d.Take("in_offset") as RationalTime? ?? default;
        OutOffset = d.Take("out_offset") as RationalTime? ?? default;
        TransitionType = Text(d.Take("transition_type"));
        Keep(d);
    }

    public override string Schema => "Transition";

    public RationalTime InOffset { get; set; }

    public RationalTime OutOffset { get; set; }

    public string TransitionType { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("in_offset", InOffset);
        writer.Field("out_offset", OutOffset);
        writer.Field("transition_type", TransitionType);
    }
}

internal class Effect : WithMetadata
{
    public Effect(AnyDictionary d)
        : base(d)
    {
        EffectName = Text(d.Take("effect_name"));
        Enabled = Flag(d.Take("enabled"));
        if (GetType() == typeof(Effect))
        {
            Keep(d);
        }
    }

    public override string Schema => "Effect";

    public string EffectName { get; set; }

    public bool Enabled { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("effect_name", EffectName);
        writer.Field("enabled", Enabled);
    }
}

/// <summary>LinearTimeWarp, and FreezeFrame, which is one with a scalar of zero.</summary>
internal sealed class LinearTimeWarp : Effect
{
    public LinearTimeWarp(AnyDictionary d, string schema)
        : base(d)
    {
        Schema = schema;
        TimeScalar = Number(d.Take("time_scalar"), 1);
        Keep(d);
    }

    public override string Schema { get; }

    public double TimeScalar { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("time_scalar", TimeScalar);
    }
}

internal sealed class Marker : WithMetadata
{
    public Marker(AnyDictionary d)
        : base(d)
    {
        Color = Text(d.Take("color"), "GREEN");
        MarkedRange = d.Take("marked_range") as TimeRange? ?? default;
        Comment = Text(d.Take("comment"));
        Keep(d);
    }

    public override string Schema => "Marker";

    public override int Version => 2;

    public string Color { get; set; }

    public TimeRange MarkedRange { get; set; }

    public string Comment { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("color", Color);
        writer.Field("marked_range", MarkedRange);
        writer.Field("comment", Comment);
    }
}

/// <summary>MediaReference and its family.</summary>
internal abstract class MediaReference : WithMetadata
{
    protected MediaReference(AnyDictionary d)
        : base(d)
    {
        AvailableRange = d.Take("available_range") as TimeRange?;
        AvailableImageBounds = d.Take("available_image_bounds") as Box2d?;
    }

    public TimeRange? AvailableRange { get; set; }

    public Box2d? AvailableImageBounds { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("available_range", AvailableRange);
        writer.Field("available_image_bounds", AvailableImageBounds);
    }
}

internal sealed class ExternalReference : MediaReference
{
    public ExternalReference(AnyDictionary d)
        : base(d)
    {
        TargetUrl = Text(d.Take("target_url"));
        Keep(d);
    }

    public override string Schema => "ExternalReference";

    public string TargetUrl { get; set; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("target_url", TargetUrl);
    }
}

internal sealed class MissingReference : MediaReference
{
    public MissingReference(AnyDictionary d)
        : base(d)
    {
        Keep(d);
    }

    public override string Schema => "MissingReference";
}

internal sealed class GeneratorReference : MediaReference
{
    public GeneratorReference(AnyDictionary d)
        : base(d)
    {
        GeneratorKind = Text(d.Take("generator_kind"));
        Parameters = d.Take("parameters") as AnyDictionary ?? new();
        Keep(d);
    }

    public override string Schema => "GeneratorReference";

    public string GeneratorKind { get; set; }

    public AnyDictionary Parameters { get; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("generator_kind", GeneratorKind);
        writer.Field("parameters", Parameters);
    }
}

internal sealed class SerializableCollection : WithMetadata
{
    public SerializableCollection(AnyDictionary d)
        : base(d)
    {
        Children = ListOf<OtioObject>(d.Take("children"));
        Keep(d);
    }

    public override string Schema => "SerializableCollection";

    public List<OtioObject> Children { get; }

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("children", Children);
    }
}

internal sealed class Timeline : WithMetadata
{
    public Timeline(AnyDictionary d)
        : base(d)
    {
        GlobalStartTime = d.Take("global_start_time") as RationalTime?;
        Tracks = d.Take("tracks") as Stack ?? new Stack(new AnyDictionary { ["name"] = "tracks" });
        Keep(d);
    }

    public override string Schema => "Timeline";

    public RationalTime? GlobalStartTime { get; set; }

    public Stack Tracks { get; }

    public RationalTime Duration => Tracks.Duration;

    public override void WriteFields(OtioWriter writer)
    {
        base.WriteFields(writer);
        writer.Field("global_start_time", GlobalStartTime);
        writer.Field("tracks", Tracks);
    }
}
