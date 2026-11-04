using WinTimeline.Model.Time;

namespace WinTimeline.Model;

/// <summary>A range the timeline cannot compute, where the reference library raises an error.</summary>
public sealed class TimelineRangeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TimelineRangeException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What could not be computed.</param>
    public TimelineRangeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What could not be computed.</param>
    /// <param name="inner">The cause.</param>
    public TimelineRangeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Composable: anything a composition holds, items and transitions alike. Its parent is computed
/// from what holds it, and its ranges in that parent are the parent's to answer (WT12).
/// </summary>
public abstract class Composable : NamedObject
{
    private protected Composable(Metadata fields)
        : base(fields)
    {
    }

    /// <summary>The composition holding it, or null.</summary>
    public Composition? Parent => Owner as Composition;

    /// <summary>Whether it overlaps its neighbours instead of following them; true of transitions.</summary>
    public virtual bool Overlapping => false;

    /// <summary>How long it lasts.</summary>
    /// <exception cref="TimelineRangeException">The duration cannot be computed.</exception>
    public abstract RationalTime Duration { get; }

    /// <summary>Its range in its parent's time, before the parent's own trim.</summary>
    /// <returns>The range.</returns>
    /// <exception cref="TimelineRangeException">It has no parent, or a range cannot be computed.</exception>
    public TimeRange RangeInParent() =>
        (Parent ?? throw new TimelineRangeException($"{Name} has no parent")).RangeOfChild(this);

    /// <summary>Its range in its parent's time after the parent's trim, or null when the trim leaves none of it.</summary>
    /// <returns>The range, or null.</returns>
    /// <exception cref="TimelineRangeException">It has no parent, or a range cannot be computed.</exception>
    public TimeRange? TrimmedRangeInParent() =>
        (Parent ?? throw new TimelineRangeException($"{Name} has no parent")).TrimmedRangeOfChild(this);
}

/// <summary>Item: anything with its own range on a track — clips, gaps, tracks and stacks.</summary>
public abstract class Item : Composable
{
    private readonly List<Effect> _effects;
    private readonly List<Marker> _markers;

    private protected Item(Metadata fields)
        : base(fields)
    {
        SourceRange = fields.Take("source_range") as TimeRange?;
        _effects = ListOf<Effect>(fields.Take("effects"));
        _markers = ListOf<Marker>(fields.Take("markers"));
        Enabled = Flag(fields.Take("enabled"));
        Color = fields.Take("color") as Color;
        foreach (ModelObject held in _effects.Cast<ModelObject>().Concat(_markers))
        {
            held.Owner = this;
        }
    }

    /// <summary>The part of what is available that plays, or null for all of it.</summary>
    public TimeRange? SourceRange { get; internal set; }

    /// <summary>The effects, in order.</summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <summary>The markers.</summary>
    public IReadOnlyList<Marker> Markers => _markers;

    /// <summary>Whether it plays; a disabled item keeps its place and shows nothing.</summary>
    public bool Enabled { get; internal set; }

    /// <summary>Its colour in the timeline, or null.</summary>
    public Color? Color { get; internal set; }

    /// <summary>Whether it shows anything: enabled, and not a gap.</summary>
    public virtual bool Visible => Enabled;

    /// <inheritdoc/>
    public override RationalTime Duration => TrimmedRange().Duration;

    /// <summary>What it could play: its media, or its children laid out.</summary>
    /// <returns>The range.</returns>
    /// <exception cref="TimelineRangeException">It has no range to offer.</exception>
    public virtual TimeRange AvailableRange() => throw new TimelineRangeException($"{Schema} {Name} has no available range");

    /// <summary>What it plays: its source range, or else everything available.</summary>
    /// <returns>The range.</returns>
    /// <exception cref="TimelineRangeException">It has neither.</exception>
    public TimeRange TrimmedRange() => SourceRange ?? AvailableRange();

    internal void SetSourceRange(TimeRange? range)
    {
        SourceRange = range;
        Changed("source_range");
    }

    internal void AddEffect(Effect effect)
    {
        effect.Owner = this;
        _effects.Add(effect);
        Changed("effects");
    }

    internal void AddMarker(Marker marker) => InsertMarker(_markers.Count, marker);

    internal void InsertMarker(int index, Marker marker)
    {
        marker.Owner = this;
        _markers.Insert(index, marker);
        Changed("markers");
    }

    internal Marker RemoveMarkerAt(int index)
    {
        Marker marker = _markers[index];
        _markers.RemoveAt(index);
        marker.Owner = null;
        Changed("markers");
        return marker;
    }
}

/// <summary>Composition: an item holding children, laid out in sequence (Track) or stacked (Stack).</summary>
public abstract class Composition : Item
{
    private readonly List<Composable> _children;
    private readonly List<ModelObject> _unknownChildren = [];

    private protected Composition(Metadata fields)
        : base(fields)
    {
        _children = [];
        foreach (object? child in ListOf<object>(fields.Take("children")))
        {
            if (child is Composable composable)
            {
                composable.Owner = this;
                _children.Add(composable);
            }
            else if (child is ModelObject other)
            {
                // A schema this build does not know: kept, so it is never lost, but not laid out.
                other.Owner = this;
                _unknownChildren.Add(other);
            }
        }
    }

    /// <summary>The children, in order.</summary>
    public IReadOnlyList<Composable> Children => _children;

    /// <summary>Children of a schema this build does not know, kept but not laid out.</summary>
    public IReadOnlyList<ModelObject> UnknownChildren => _unknownChildren;

    /// <summary>A child's range in this composition's time, before this composition's trim.</summary>
    /// <param name="index">The child's position.</param>
    /// <returns>The range.</returns>
    public abstract TimeRange RangeOfChildAtIndex(int index);

    /// <summary>A child's range in this composition's time, trimmed by this composition's source range.</summary>
    /// <param name="index">The child's position.</param>
    /// <returns>The range.</returns>
    /// <exception cref="TimelineRangeException">The trim leaves none of the child.</exception>
    public abstract TimeRange TrimmedRangeOfChildAtIndex(int index);

    /// <summary>A child's range in this composition's time.</summary>
    /// <param name="child">A direct child.</param>
    /// <returns>The range.</returns>
    public TimeRange RangeOfChild(Composable child) => RangeOfChildAtIndex(IndexOf(child));

    /// <summary>
    /// A child's range after this composition's trim, as the reference computes it: the per-index
    /// trim, then clipped once more to the source range; null when nothing of it is left.
    /// </summary>
    /// <param name="child">A direct child.</param>
    /// <returns>The range, or null.</returns>
    public TimeRange? TrimmedRangeOfChild(Composable child)
    {
        TimeRange result = TrimmedRangeOfChildAtIndex(IndexOf(child));
        if (SourceRange is not { } source)
        {
            return result;
        }

        RationalTime start = RationalTime.Max(source.StartTime, result.StartTime);
        if (start > result.EndTimeExclusive)
        {
            return null;
        }

        RationalTime duration = RationalTime.Min(result.EndTimeExclusive, source.EndTimeExclusive) - start;
        return duration.Value < 0 ? null : new TimeRange(start, duration);
    }

    /// <summary>Every clip under this composition, at any depth, in order.</summary>
    /// <returns>The clips.</returns>
    public IEnumerable<Clip> FindClips()
    {
        foreach (Composable child in _children)
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

    /// <summary>A child's range clipped to this composition's source range, or null when none of it is left.</summary>
    internal TimeRange? TrimChildRange(TimeRange childRange)
    {
        if (SourceRange is not { } source)
        {
            return childRange;
        }

        bool pastEnd = source.StartTime >= childRange.EndTimeExclusive;
        bool beforeStart = source.EndTimeExclusive <= childRange.StartTime;
        if (pastEnd || beforeStart)
        {
            return null;
        }

        if (childRange.StartTime < source.StartTime)
        {
            childRange = TimeRange.FromStartEndTime(source.StartTime, childRange.EndTimeExclusive);
        }

        if (childRange.EndTimeExclusive > source.EndTimeExclusive)
        {
            childRange = TimeRange.FromStartEndTime(childRange.StartTime, source.EndTimeExclusive);
        }

        return childRange;
    }

    internal void Insert(int index, Composable child)
    {
        child.Owner = this;
        _children.Insert(index, child);
        Changed("children");
    }

    internal void RemoveAt(int index)
    {
        _children[index].Owner = null;
        _children.RemoveAt(index);
        Changed("children");
    }

    private int IndexOf(Composable child)
    {
        int index = _children.IndexOf(child);
        return index >= 0 ? index : throw new TimelineRangeException($"{child.Name} is not a child of {Name}");
    }
}

/// <summary>Kinds of track; a file may carry others, kept as strings.</summary>
public static class TrackKind
{
    /// <summary>Pictures.</summary>
    public const string Video = "Video";

    /// <summary>Sound.</summary>
    public const string Audio = "Audio";
}

/// <summary>A track: children played one after another, transitions overlapping their neighbours.</summary>
public sealed class Track : Composition
{
    internal Track(Metadata fields)
        : base(fields)
    {
        Kind = Text(fields.Take("kind"), TrackKind.Video);
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Track";

    /// <summary>Video, Audio, or a kind another tool named.</summary>
    public string Kind { get; internal set; }

    /// <summary>The children's durations in sequence, plus a leading or trailing transition's overhang.</summary>
    /// <returns>The range.</returns>
    public override TimeRange AvailableRange()
    {
        RationalTime duration = new();
        foreach (Composable child in Children)
        {
            if (child is Item item)
            {
                duration += item.Duration;
            }
        }

        if (Children.Count > 0)
        {
            if (Children[0] is Transition first)
            {
                duration += first.InOffset;
            }

            if (Children[^1] is Transition last)
            {
                duration += last.OutOffset;
            }
        }

        return new TimeRange(new RationalTime(0, duration.Rate), duration);
    }

    /// <inheritdoc/>
    public override TimeRange RangeOfChildAtIndex(int index)
    {
        if (index < 0 || index >= Children.Count)
        {
            throw new TimelineRangeException($"{Name} has no child {index}");
        }

        Composable child = Children[index];
        RationalTime childDuration = child.Duration;
        RationalTime start = new(0, childDuration.Rate);
        for (int i = 0; i < index; i++)
        {
            if (!Children[i].Overlapping)
            {
                start += Children[i].Duration;
            }
        }

        if (child is Transition transition)
        {
            start -= transition.InOffset;
        }

        return new TimeRange(start, childDuration);
    }

    /// <inheritdoc/>
    public override TimeRange TrimmedRangeOfChildAtIndex(int index) =>
        TrimChildRange(RangeOfChildAtIndex(index)) ?? throw new TimelineRangeException($"child {index} of {Name} is trimmed away");
}

/// <summary>A stack: children layered over each other, all starting at zero.</summary>
public sealed class Stack : Composition
{
    internal Stack(Metadata fields)
        : base(fields)
    {
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Stack";

    /// <summary>The longest child.</summary>
    /// <returns>The range.</returns>
    public override TimeRange AvailableRange()
    {
        if (Children.Count == 0)
        {
            return new TimeRange();
        }

        RationalTime duration = Children[0].Duration;
        for (int i = 1; i < Children.Count; i++)
        {
            duration = RationalTime.Max(duration, Children[i].Duration);
        }

        return new TimeRange(new RationalTime(0, duration.Rate), duration);
    }

    /// <inheritdoc/>
    public override TimeRange RangeOfChildAtIndex(int index)
    {
        if (index < 0 || index >= Children.Count)
        {
            throw new TimelineRangeException($"{Name} has no child {index}");
        }

        RationalTime duration = Children[index].Duration;
        return new TimeRange(new RationalTime(0, duration.Rate), duration);
    }

    /// <inheritdoc/>
    public override TimeRange TrimmedRangeOfChildAtIndex(int index)
    {
        TimeRange range = RangeOfChildAtIndex(index);
        return SourceRange is { } source
            ? new TimeRange(source.StartTime, RationalTime.Min(range.Duration, source.Duration))
            : range;
    }
}

/// <summary>A clip: a range of a media reference, with alternative references kept by key.</summary>
public sealed class Clip : Item
{
    /// <summary>The key of the reference a file with one reference uses.</summary>
    public const string DefaultMedia = "DEFAULT_MEDIA";

    internal Clip(Metadata fields)
        : base(fields)
    {
        MediaReferences = fields.Take("media_references") as Metadata ?? new Metadata();
        ActiveMediaReferenceKey = Text(fields.Take("active_media_reference_key"), DefaultMedia);
        foreach (ModelObject reference in MediaReferences.Values.OfType<ModelObject>())
        {
            reference.Owner = this;
        }

        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Clip";

    /// <inheritdoc/>
    public override int Version => 2;

    /// <summary>The media references, by key.</summary>
    public Metadata MediaReferences { get; }

    /// <summary>The key of the reference in use.</summary>
    public string ActiveMediaReferenceKey { get; internal set; }

    /// <summary>The reference in use, or null when the key names none.</summary>
    public MediaReference? MediaReference => MediaReferences.TryGetValue(ActiveMediaReferenceKey, out object? reference) ? reference as MediaReference : null;

    /// <summary>The media's available range.</summary>
    /// <returns>The range.</returns>
    /// <exception cref="TimelineRangeException">The media reference has none.</exception>
    public override TimeRange AvailableRange() =>
        MediaReference?.AvailableRange ?? throw new TimelineRangeException($"clip {Name} has no media with an available range");
}

/// <summary>A gap: empty time on a track, as long as its source range.</summary>
public sealed class Gap : Item
{
    internal Gap(Metadata fields)
        : base(fields)
    {
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Gap";

    /// <inheritdoc/>
    public override bool Visible => false;
}

/// <summary>A transition between its neighbours, overlapping each by its offsets.</summary>
public sealed class Transition : Composable
{
    /// <summary>The SMPTE dissolve.</summary>
    public const string SmpteDissolve = "SMPTE_Dissolve";

    internal Transition(Metadata fields)
        : base(fields)
    {
        InOffset = fields.Take("in_offset") as RationalTime? ?? new RationalTime();
        OutOffset = fields.Take("out_offset") as RationalTime? ?? new RationalTime();
        TransitionType = Text(fields.Take("transition_type"));
        Keep(fields);
    }

    /// <inheritdoc/>
    public override string Schema => "Transition";

    /// <inheritdoc/>
    public override bool Overlapping => true;

    /// <summary>How far it reaches into the item before it.</summary>
    public RationalTime InOffset { get; internal set; }

    /// <summary>How far it reaches into the item after it.</summary>
    public RationalTime OutOffset { get; internal set; }

    /// <summary>SMPTE_Dissolve, Custom_Transition, or another tool's name.</summary>
    public string TransitionType { get; internal set; }

    /// <summary>Its in and out offsets together.</summary>
    public override RationalTime Duration => InOffset + OutOffset;
}
