using System.Globalization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Compare;

/// <summary>What happened to a clip between two versions of a cut.</summary>
public enum ChangeKind
{
    /// <summary>Taken out of the cut.</summary>
    Removed,

    /// <summary>Put into the cut.</summary>
    Added,

    /// <summary>Taken out, with other media put in its place.</summary>
    Replaced,

    /// <summary>Put elsewhere: out of order with its neighbours, or onto another track.</summary>
    Moved,

    /// <summary>Kept in place with a different head or tail.</summary>
    Trimmed,
}

/// <summary>
/// One change between two versions. <see cref="Before"/> is null for an added clip and
/// <see cref="After"/> for a removed one; trims are in frames at the clip's own rate, positive
/// where the later version plays later media.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Track">The track it is on in the later version, or was on, e.g. "V1".</param>
/// <param name="Before">The clip in the earlier version.</param>
/// <param name="After">The clip in the later version.</param>
/// <param name="At">Where it is in the later version, or was in the earlier, as timecode.</param>
/// <param name="From">Where a moved clip was, as timecode, or null.</param>
/// <param name="HeadFrames">How much the head moved.</param>
/// <param name="TailFrames">How much the tail moved.</param>
public sealed record ClipChange(ChangeKind Kind, string Track, Clip? Before, Clip? After, string At, string? From, int HeadFrames, int TailFrames)
{
    /// <summary>The change as a sentence, e.g. "Trimmed c2 on V1 at 01:00:03:00: head +5, tail -3 frames."</summary>
    public string Text => Kind switch
    {
        ChangeKind.Removed => $"Removed {Before!.Name} from {Track} at {At}.",
        ChangeKind.Added => $"Added {After!.Name} to {Track} at {At}.",
        ChangeKind.Replaced => $"Replaced {Before!.Name} with {After!.Name} on {Track} at {At}.",
        ChangeKind.Moved => $"Moved {After!.Name} to {Track} at {At} from {From}.{Trims(" Trimmed")}",
        _ => $"Trimmed {After!.Name} on {Track} at {At}:{Trims(string.Empty)}",
    };

    /// <summary>Seconds into its track, for ordering the list.</summary>
    internal double Sort { get; init; }

    private string Trims(string lead) =>
        HeadFrames == 0 && TailFrames == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{lead} head {HeadFrames:+0;-0;0}, tail {TailFrames:+0;-0;0} frames.");
}

/// <summary>
/// WT21: what changed between two versions of a cut. Clips are matched by their media and by
/// how much of it they share; a matched clip is unchanged when it keeps its order among its
/// track's matched neighbours, even if earlier edits ripple it along.
/// </summary>
public sealed class TimelineDiff
{
    private TimelineDiff(IReadOnlyList<ClipChange> changes, int unchanged)
    {
        Changes = changes;
        Unchanged = unchanged;
    }

    /// <summary>The changes, track by track in the later version's order.</summary>
    public IReadOnlyList<ClipChange> Changes { get; }

    /// <summary>How many clips are the same in both.</summary>
    public int Unchanged { get; }

    /// <summary>A line saying how much changed, e.g. "5 changes; 3 clips unchanged."</summary>
    public string Summary => $"{(Changes.Count == 0 ? "No changes" : Count(Changes.Count, "change", "changes"))}; {Count(Unchanged, "clip", "clips")} unchanged.";

    /// <summary>Compares two versions of a cut.</summary>
    /// <param name="before">The earlier version.</param>
    /// <param name="after">The later version.</param>
    /// <returns>What changed.</returns>
    public static TimelineDiff Compare(Timeline before, Timeline after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        List<Placed> a = Place(before);
        List<Placed> b = Place(after);
        Dictionary<Placed, Placed> partner = Match(a, b);

        HashSet<Placed> stable = new(ReferenceEqualityComparer.Instance);
        foreach (IGrouping<string, Placed> track in b.GroupBy(p => p.Track))
        {
            List<Placed> kept = [.. track.Where(p => partner.TryGetValue(p, out Placed? q) && q.Track == p.Track)];
            foreach (Placed p in LongestInOrder(kept, p => partner[p].Order))
            {
                stable.Add(p);
                stable.Add(partner[p]);
            }
        }

        List<ClipChange> changes = [];
        int unchanged = 0;
        foreach (Placed p in b.Where(partner.ContainsKey))
        {
            Placed q = partner[p];
            int head = Frames(p.Clip.TrimmedRange().StartTime - q.Clip.TrimmedRange().StartTime, q);
            int tail = Frames(p.Clip.TrimmedRange().EndTimeExclusive - q.Clip.TrimmedRange().EndTimeExclusive, q);
            if (!stable.Contains(p))
            {
                changes.Add(new ClipChange(ChangeKind.Moved, p.Track, q.Clip, p.Clip, p.At, q.At, head, tail) { Sort = p.Sort });
            }
            else if (head != 0 || tail != 0)
            {
                changes.Add(new ClipChange(ChangeKind.Trimmed, p.Track, q.Clip, p.Clip, p.At, null, head, tail) { Sort = p.Sort });
            }
            else
            {
                unchanged++;
            }
        }

        // What is left unmatched between the same stable neighbours was swapped, one for one.
        Dictionary<(string, Placed?), Queue<Placed>> added = [];
        foreach (Placed p in b.Where(p => !partner.ContainsKey(p)))
        {
            Placed? key = Previous(b, p, stable) is { } s ? partner[s] : null;
            if (!added.TryGetValue((p.Track, key), out Queue<Placed>? queue))
            {
                added[(p.Track, key)] = queue = new Queue<Placed>();
            }

            queue.Enqueue(p);
        }

        foreach (Placed q in a.Where(q => !partner.ContainsKey(q)))
        {
            if (added.TryGetValue((q.Track, Previous(a, q, stable)), out Queue<Placed>? queue) && queue.TryDequeue(out Placed? p))
            {
                changes.Add(new ClipChange(ChangeKind.Replaced, p.Track, q.Clip, p.Clip, p.At, null, 0, 0) { Sort = p.Sort });
            }
            else
            {
                changes.Add(new ClipChange(ChangeKind.Removed, q.Track, q.Clip, null, q.At, null, 0, 0) { Sort = q.Sort });
            }
        }

        changes.AddRange(added.Values.SelectMany(q => q).Select(p => new ClipChange(ChangeKind.Added, p.Track, null, p.Clip, p.At, null, 0, 0) { Sort = p.Sort }));
        return new TimelineDiff([.. changes.OrderBy(c => c.Track, StringComparer.Ordinal).ThenBy(c => c.Sort).ThenBy(c => c.Kind)], unchanged);
    }

    private static int Frames(RationalTime delta, Placed at) =>
        (int)Math.Round(delta.ValueRescaledTo(at.Clip.TrimmedRange().StartTime.Rate));

    private static Placed? Previous(List<Placed> all, Placed of, HashSet<Placed> stable) =>
        all.Where(p => p.Track == of.Track && p.Order < of.Order && stable.Contains(p)).MaxBy(p => p.Order);

    /// <summary>
    /// Pairs each clip with the one in the other version that shares the most of the same media,
    /// biggest overlaps first, preferring the same track.
    /// </summary>
    private static Dictionary<Placed, Placed> Match(List<Placed> a, List<Placed> b)
    {
        var candidates =
            from p in b
            from q in a
            where p.MediaKey == q.MediaKey
            let overlap = Overlap(p.Clip.TrimmedRange(), q.Clip.TrimmedRange())
            where overlap > 0
            orderby overlap descending, p.Track == q.Track descending, Math.Abs(p.Order - q.Order), p.Sort
            select (p, q);

        Dictionary<Placed, Placed> partner = new(ReferenceEqualityComparer.Instance);
        foreach ((Placed p, Placed q) in candidates)
        {
            if (!partner.ContainsKey(p) && !partner.ContainsKey(q))
            {
                partner[p] = q;
                partner[q] = p;
            }
        }

        return partner;
    }

    private static double Overlap(TimeRange x, TimeRange y)
    {
        double rate = x.StartTime.Rate;
        double start = Math.Max(x.StartTime.Value, y.StartTime.ValueRescaledTo(rate));
        double end = Math.Min(x.EndTimeExclusive.ValueRescaledTo(rate), y.EndTimeExclusive.ValueRescaledTo(rate));
        return end - start;
    }

    /// <summary>The longest run of items whose keys rise, keeping the earliest such run.</summary>
    private static List<T> LongestInOrder<T>(List<T> items, Func<T, int> key)
    {
        int n = items.Count;
        int[] length = new int[n];
        int[] previous = new int[n];
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            length[i] = 1;
            previous[i] = -1;
            for (int j = 0; j < i; j++)
            {
                if (key(items[j]) < key(items[i]) && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }

            if (best < 0 || length[i] > length[best])
            {
                best = i;
            }
        }

        List<T> run = [];
        for (int i = best; i >= 0; i = previous[i])
        {
            run.Add(items[i]);
        }

        run.Reverse();
        return run;
    }

    /// <summary>Every clip with its track, its order on it and where it plays.</summary>
    private static List<Placed> Place(Timeline timeline)
    {
        List<Placed> placed = [];
        RationalTime origin = timeline.GlobalStartTime ?? new RationalTime(0, 24);
        Dictionary<string, int> perKind = new(StringComparer.Ordinal);
        foreach (Track track in timeline.Tracks.Children.OfType<Track>())
        {
            perKind[track.Kind] = perKind.GetValueOrDefault(track.Kind) + 1;
            string label = Label(track.Kind, perKind[track.Kind]);
            int order = 0;
            foreach (Clip clip in track.FindClips())
            {
                RationalTime start = StartInTrack(clip, track);
                RationalTime at = origin + start.RescaledTo(origin);
                placed.Add(new Placed(clip, label, order++, start.ValueRescaledTo(1), Media(clip), at.ToTimecode(origin.Rate)));
            }
        }

        return placed;
    }

    private static RationalTime StartInTrack(Composable item, Track track)
    {
        RationalTime start = item.RangeInParent().StartTime;
        for (Composition? parent = item.Parent; parent is not null && !ReferenceEquals(parent, track); parent = parent.Parent)
        {
            if (parent.SourceRange is { } trim)
            {
                start -= trim.StartTime;
            }

            start += parent.RangeInParent().StartTime;
        }

        return start;
    }

    private static string Media(Clip clip) => clip.MediaReference switch
    {
        ExternalReference external => "url:" + external.TargetUrl,
        ImageSequenceReference sequence => "seq:" + sequence.AbstractTargetUrl("#"),
        GeneratorReference generator => "gen:" + generator.GeneratorKind,
        _ => "clip:" + clip.Name,
    };

    private static string Label(string kind, int number)
    {
        char letter = kind switch
        {
            TrackKind.Audio => 'A',
            TrackKind.Video => 'V',
            _ => kind.FirstOrDefault('?'),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{letter}{number}");
    }

    private static string Count(int n, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{n} {(n == 1 ? one : many)}");

    private sealed record Placed(Clip Clip, string Track, int Order, double Sort, string MediaKey, string At);
}
