using WinTimeline.Model.Time;

namespace WinTimeline.Model;

/// <summary>An effect on an item: a name the renderer looks up, and whether it is on.</summary>
public class Effect : NamedObject
{
    internal Effect(Metadata fields)
        : base(fields)
    {
        EffectName = Text(fields.Take("effect_name"));
        Enabled = Flag(fields.Take("enabled"));
        if (GetType() == typeof(Effect))
        {
            Keep(fields);
        }
    }

    /// <inheritdoc/>
    public override string Schema => "Effect";

    /// <summary>What the effect is, e.g. "Blur".</summary>
    public string EffectName { get; internal set; }

    /// <summary>Whether it applies.</summary>
    public bool Enabled { get; internal set; }
}

/// <summary>A constant speed change; a scalar of 0 is a freeze frame, written as FreezeFrame.</summary>
public sealed class LinearTimeWarp : Effect
{
    internal LinearTimeWarp(Metadata fields, string schema)
        : base(fields)
    {
        Schema = schema;
        TimeScalar = Number(fields.Take("time_scalar"), 1);
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema { get; }

    /// <summary>The speed: 2 is twice as fast, 0.5 half, negative plays backwards.</summary>
    public double TimeScalar { get; internal set; }
}

/// <summary>Marker colours the reference names.</summary>
public static class MarkerColor
{
    /// <summary>The reference's default.</summary>
    public const string Green = "GREEN";

    /// <summary>Red.</summary>
    public const string Red = "RED";
}

/// <summary>A marker: a range on an item with a colour and a comment.</summary>
public sealed class Marker : NamedObject
{
    internal Marker(Metadata fields)
        : base(fields)
    {
        Color = Text(fields.Take("color"), MarkerColor.Green);
        MarkedRange = fields.Take("marked_range") as TimeRange? ?? new TimeRange();
        Comment = Text(fields.Take("comment"));
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Marker";

    /// <inheritdoc/>
    public override int Version => 2;

    /// <summary>RED, GREEN, or another name.</summary>
    public string Color { get; internal set; }

    /// <summary>What it marks, in the item's own time.</summary>
    public TimeRange MarkedRange { get; internal set; }

    /// <summary>The note.</summary>
    public string Comment { get; internal set; }
}

/// <summary>Where a clip's media is: the family of references.</summary>
public abstract class MediaReference : NamedObject
{
    private protected MediaReference(Metadata fields)
        : base(fields)
    {
        AvailableRange = fields.Take("available_range") as TimeRange?;
        AvailableImageBounds = fields.Take("available_image_bounds") as Box2d?;
    }

    /// <summary>The media's own range, or null when unknown.</summary>
    public TimeRange? AvailableRange { get; internal set; }

    /// <summary>The image's bounds, or null when unknown.</summary>
    public Box2d? AvailableImageBounds { get; internal set; }

    /// <summary>Whether the reference names no media.</summary>
    public virtual bool IsMissing => false;
}

/// <summary>Media at a URL, usually a file.</summary>
public sealed class ExternalReference : MediaReference
{
    internal ExternalReference(Metadata fields)
        : base(fields)
    {
        TargetUrl = Text(fields.Take("target_url"));
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "ExternalReference";

    /// <summary>The URL, e.g. file:///D:/footage/A001.mov.</summary>
    public string TargetUrl { get; internal set; }
}

/// <summary>A reference to media nobody has: the clip keeps its place.</summary>
public sealed class MissingReference : MediaReference
{
    internal MissingReference(Metadata fields)
        : base(fields)
    {
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "MissingReference";

    /// <inheritdoc/>
    public override bool IsMissing => true;
}

/// <summary>Media a generator makes: bars, a solid, a title.</summary>
public sealed class GeneratorReference : MediaReference
{
    internal GeneratorReference(Metadata fields)
        : base(fields)
    {
        GeneratorKind = Text(fields.Take("generator_kind"));
        Parameters = fields.Take("parameters") as Metadata ?? new Metadata();
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "GeneratorReference";

    /// <summary>What generates it, e.g. "SMPTEBars".</summary>
    public string GeneratorKind { get; internal set; }

    /// <summary>The generator's parameters.</summary>
    public Metadata Parameters { get; }
}

/// <summary>What an image sequence does where a frame is missing.</summary>
public static class MissingFramePolicy
{
    /// <summary>Fail.</summary>
    public const string Error = "error";

    /// <summary>Hold the frame before.</summary>
    public const string Hold = "hold";

    /// <summary>Show black.</summary>
    public const string Black = "black";
}

/// <summary>A numbered sequence of image files, one per frame.</summary>
public sealed class ImageSequenceReference : MediaReference
{
    internal ImageSequenceReference(Metadata fields)
        : base(fields)
    {
        TargetUrlBase = Text(fields.Take("target_url_base"));
        NamePrefix = Text(fields.Take("name_prefix"));
        NameSuffix = Text(fields.Take("name_suffix"));
        StartFrame = (int)Number(fields.Take("start_frame"), 1);
        FrameStep = (int)Number(fields.Take("frame_step"), 1);
        Rate = Number(fields.Take("rate"), 1);
        FrameZeroPadding = (int)Number(fields.Take("frame_zero_padding"));
        MissingFramePolicy = Text(fields.Take("missing_frame_policy"), Model.MissingFramePolicy.Error);
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "ImageSequenceReference";

    /// <summary>The folder URL the frames are in, ending in a slash.</summary>
    public string TargetUrlBase { get; internal set; }

    /// <summary>What comes before the frame number, e.g. "shot_".</summary>
    public string NamePrefix { get; internal set; }

    /// <summary>What comes after it, e.g. ".exr".</summary>
    public string NameSuffix { get; internal set; }

    /// <summary>The first frame's number.</summary>
    public int StartFrame { get; internal set; }

    /// <summary>The step between numbered frames.</summary>
    public int FrameStep { get; internal set; }

    /// <summary>Frames per second.</summary>
    public double Rate { get; internal set; }

    /// <summary>How many digits the number is padded to.</summary>
    public int FrameZeroPadding { get; internal set; }

    /// <summary>error, hold or black.</summary>
    public string MissingFramePolicy { get; internal set; }

    /// <summary>The frame number shown at a time, as the reference computes it (truncated, not rounded).</summary>
    /// <param name="time">A time inside the available range.</param>
    /// <returns>The frame number.</returns>
    /// <exception cref="TimelineRangeException">There is no available range, or the time is outside it.</exception>
    public int FrameForTime(Time.RationalTime time)
    {
        if (AvailableRange is not { } available || !available.Contains(time))
        {
            throw new TimelineRangeException($"{time} is outside {Name}'s available range");
        }

        return StartFrame + (int)(time - available.StartTime).ValueRescaledTo(Rate);
    }

    /// <summary>The first and last frame numbers a range shows.</summary>
    /// <param name="range">A range inside the available range.</param>
    /// <returns>The first and last frame.</returns>
    public (int First, int Last) FrameRangeForTimeRange(Time.TimeRange range) =>
        (FrameForTime(range.StartTime), FrameForTime(range.EndTimeInclusive));

    /// <summary>The URL with a symbol in place of the frame number, e.g. "file:///shots/sh010.[1001-1020].exr".</summary>
    /// <param name="symbol">What stands for the number.</param>
    /// <returns>The URL.</returns>
    public string AbstractTargetUrl(string symbol) =>
        (TargetUrlBase.EndsWith('/') ? TargetUrlBase : TargetUrlBase + "/") + NamePrefix + symbol + NameSuffix;

    /// <summary>The URL of the nth image in the sequence: start frame plus n steps, zero-padded.</summary>
    /// <param name="imageNumber">The image's position, counting from 0.</param>
    /// <returns>The URL.</returns>
    public string TargetUrlForImageNumber(int imageNumber)
    {
        int frame = StartFrame + (imageNumber * FrameStep);
        string number = frame < 0
            ? "-" + (-frame).ToString(new string('0', Math.Max(1, FrameZeroPadding)), System.Globalization.CultureInfo.InvariantCulture)
            : frame.ToString(new string('0', Math.Max(1, FrameZeroPadding)), System.Globalization.CultureInfo.InvariantCulture);
        return TargetUrlBase + NamePrefix + number + NameSuffix;
    }
}

/// <summary>A timeline: a stack of tracks and where its clock starts.</summary>
public sealed class Timeline : NamedObject
{
    internal Timeline(Metadata fields)
        : base(fields)
    {
        GlobalStartTime = fields.Take("global_start_time") as RationalTime?;
        Tracks = fields.Take("tracks") as Stack ?? new Stack(new Metadata());
        Tracks.Owner = this;
        Keep(fields);
    }

    /// <summary>Raised for every change to anything in this timeline, naming the object that changed.</summary>
    public event EventHandler<ModelChange>? ModelChanged;

    /// <inheritdoc/>
    public override string Schema => "Timeline";

    /// <summary>The time the timeline's first frame is labelled with, or null.</summary>
    public RationalTime? GlobalStartTime { get; internal set; }

    /// <summary>The tracks, as a stack.</summary>
    public Stack Tracks { get; }

    /// <summary>How long the timeline is.</summary>
    public RationalTime Duration => Tracks.Duration;

    /// <summary>The video tracks, top level only.</summary>
    public IEnumerable<Track> VideoTracks => Tracks.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Video);

    /// <summary>The audio tracks, top level only.</summary>
    public IEnumerable<Track> AudioTracks => Tracks.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Audio);

    /// <summary>Every clip, at any depth.</summary>
    /// <returns>The clips.</returns>
    public IEnumerable<Clip> FindClips() => Tracks.FindClips();

    internal void Announce(ModelChange change) => ModelChanged?.Invoke(this, change);
}

/// <summary>A named list of objects, as a file holding several timelines has.</summary>
public sealed class SerializableCollection : NamedObject
{
    private readonly List<ModelObject> _children;

    internal SerializableCollection(Metadata fields)
        : base(fields)
    {
        _children = ListOf<ModelObject>(fields.Take("children"));
        foreach (ModelObject child in _children)
        {
            child.Owner = this;
        }

        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "SerializableCollection";

    /// <summary>What it holds.</summary>
    public IReadOnlyList<ModelObject> Children => _children;
}
