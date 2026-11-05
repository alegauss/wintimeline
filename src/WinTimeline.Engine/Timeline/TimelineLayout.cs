using WinTimeline.Model;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Timeline;

/// <summary>What a lane item is drawn as.</summary>
public enum LaneItemKind
{
    /// <summary>A clip.</summary>
    Clip,

    /// <summary>Empty time.</summary>
    Gap,

    /// <summary>A transition, overlapping its neighbours.</summary>
    Transition,

    /// <summary>A nested track or stack, drawn as one block.</summary>
    Nested,
}

/// <summary>One thing on a lane, with where it plays in seconds from the timeline's start.</summary>
/// <param name="Item">The model object.</param>
/// <param name="Lane">The lane's index, top to bottom.</param>
/// <param name="Start">When it starts.</param>
/// <param name="End">When it ends.</param>
/// <param name="Kind">What it is drawn as.</param>
public sealed record LaneItem(Composable Item, int Lane, double Start, double End, LaneItemKind Kind)
{
    /// <summary>Its name, as drawn.</summary>
    public string Name => Item.Name;
}

/// <summary>A row of the timeline: one top-level track.</summary>
/// <param name="Label">What its header says, e.g. "V2".</param>
/// <param name="Track">The track.</param>
/// <param name="Items">What is on it, by start time.</param>
public sealed record Lane(string Label, Track Track, IReadOnlyList<LaneItem> Items)
{
    /// <summary>Whether it carries sound.</summary>
    public bool IsAudio => Track.Kind == TrackKind.Audio;
}

/// <summary>
/// WT47: where everything on a timeline is drawn, as lanes of items in seconds, with a query for
/// what a viewport shows that costs a binary search per lane rather than a walk of every clip.
/// Video lanes are stacked top to bottom from the highest track down, audio below them, as
/// editors read a timeline.
/// </summary>
public sealed class TimelineLayout
{
    private readonly double[][] _starts;
    private readonly double[] _longest;
    private Dictionary<LaneItem, string>? _idOf;
    private Dictionary<string, LaneItem>? _byId;

    private TimelineLayout(IReadOnlyList<Lane> lanes, double duration)
    {
        Lanes = lanes;
        Duration = duration;
        _starts = [.. lanes.Select(l => l.Items.Select(i => i.Start).ToArray())];
        _longest = [.. lanes.Select(l => l.Items.Count == 0 ? 0 : l.Items.Max(i => i.End - i.Start))];
    }

    /// <summary>WT54: the timeline's stack, whose markers are the timeline's own.</summary>
    public Stack? Tracks { get; private init; }

    /// <summary>The lanes, top to bottom.</summary>
    public IReadOnlyList<Lane> Lanes { get; }

    /// <summary>How long the timeline is, in seconds.</summary>
    public double Duration { get; }

    /// <summary>WT52: an item's id, its lane's label and its index in its track, e.g. "V1-0".</summary>
    /// <param name="item">One of this layout's items.</param>
    /// <returns>The id.</returns>
    public string IdOf(LaneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Index();
        return _idOf!.TryGetValue(item, out string? id) ? id : string.Empty;
    }

    /// <summary>WT52: the item an id names, or null when the layout has none.</summary>
    /// <param name="id">e.g. "V1-0".</param>
    /// <returns>The item, or null.</returns>
    public LaneItem? Find(string id)
    {
        Index();
        return _byId!.GetValueOrDefault(id);
    }

    private void Index()
    {
        if (_idOf is not null)
        {
            return;
        }

        Dictionary<LaneItem, string> idOf = new(ReferenceEqualityComparer.Instance);
        Dictionary<string, LaneItem> byId = new(StringComparer.Ordinal);
        foreach (Lane lane in Lanes)
        {
            Dictionary<Composable, int> index = new(ReferenceEqualityComparer.Instance);
            int i = 0;
            foreach (Composable child in lane.Track.Children)
            {
                index.TryAdd(child, i++);
            }

            foreach (LaneItem item in lane.Items)
            {
                string id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lane.Label}-{index.GetValueOrDefault(item.Item, -1)}");
                idOf[item] = id;
                byId.TryAdd(id, item);
            }
        }

        (_idOf, _byId) = (idOf, byId);
    }

    /// <summary>How many items there are on all lanes.</summary>
    public int Count => Lanes.Sum(l => l.Items.Count);

    /// <summary>Lays a timeline out.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The layout.</returns>
    public static TimelineLayout Build(ModelTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return Build(timeline.Tracks);
    }

    /// <summary>
    /// WT56: lays out a stack's tracks, the timeline's own or a compound clip's opened in the
    /// panel, in the stack's own time. A track the stack plays only part of is laid out whole.
    /// </summary>
    /// <param name="stack">The stack.</param>
    /// <returns>The layout.</returns>
    public static TimelineLayout Build(Stack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        List<Track> video = [.. stack.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Video)];
        List<Track> audio = [.. stack.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Audio)];
        List<(Track Track, string Label)> order =
        [
            .. video.Select((t, i) => (t, $"V{i + 1}")).Reverse(),
            .. audio.Select((t, i) => (t, $"A{i + 1}")),
        ];

        List<Lane> lanes = [];
        double duration = 0;
        foreach ((Track track, string label) in order)
        {
            List<LaneItem> items = Place(track, lanes.Count);
            duration = Math.Max(duration, items.Count == 0 ? 0 : items.Max(i => i.End));
            lanes.Add(new Lane(label, track, items));
        }

        return new TimelineLayout(lanes, duration) { Tracks = stack };
    }

    /// <summary>What a viewport shows: every item on the lanes in range that overlaps the time range.</summary>
    /// <param name="from">The viewport's first second.</param>
    /// <param name="to">Its last.</param>
    /// <param name="firstLane">The first lane in view.</param>
    /// <param name="lastLane">The last lane in view.</param>
    /// <returns>The items, lane by lane, each lane by start.</returns>
    public IEnumerable<LaneItem> Visible(double from, double to, int firstLane, int lastLane)
    {
        for (int lane = Math.Max(0, firstLane); lane <= Math.Min(lastLane, Lanes.Count - 1); lane++)
        {
            double[] starts = _starts[lane];
            int index = Array.BinarySearch(starts, from - _longest[lane]);
            if (index < 0)
            {
                index = ~index;
            }

            // Equal starts (a transition beside a clip) may leave the search past the first of them.
            while (index > 0 && starts[index - 1] >= from - _longest[lane])
            {
                index--;
            }

            IReadOnlyList<LaneItem> items = Lanes[lane].Items;
            for (; index < items.Count && items[index].Start < to; index++)
            {
                if (items[index].End > from)
                {
                    yield return items[index];
                }
            }
        }
    }

    /// <summary>The item at a point, the topmost (a transition over a clip) where two overlap.</summary>
    /// <param name="seconds">The time.</param>
    /// <param name="lane">The lane.</param>
    /// <returns>The item, or null.</returns>
    public LaneItem? At(double seconds, int lane) =>
        Visible(seconds, seconds + 1e-9, lane, lane).OrderBy(i => i.Kind == LaneItemKind.Transition ? 0 : 1).FirstOrDefault();

    /// <summary>A track's children in sequence, a transition straddling the cut between its neighbours.</summary>
    private static List<LaneItem> Place(Track track, int lane)
    {
        List<LaneItem> items = [];
        double at = 0;
        foreach (Composable child in track.Children)
        {
            if (child is Transition transition)
            {
                items.Add(new LaneItem(child, lane, at - transition.InOffset.ValueRescaledTo(1), at + transition.OutOffset.ValueRescaledTo(1), LaneItemKind.Transition));
                continue;
            }

            double length = Seconds(child);
            LaneItemKind kind = child switch
            {
                Clip => LaneItemKind.Clip,
                Gap => LaneItemKind.Gap,
                _ => LaneItemKind.Nested,
            };
            items.Add(new LaneItem(child, lane, at, at + length, kind));
            at += length;
        }

        // Stable, so a transition keeps its place before the clip that starts where it does.
        return [.. items.OrderBy(i => i.Start)];
    }

    /// <summary>How long an item lasts; one whose length cannot be known (no range, no media) takes none.</summary>
    private static double Seconds(Composable item)
    {
        try
        {
            return Math.Max(0, item.Duration.ValueRescaledTo(1));
        }
        catch (TimelineRangeException)
        {
            return 0;
        }
    }
}
