using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>
/// WT70: edits of a track in time rather than by index: split at a time, clear a range, overwrite,
/// insert, lift, extract and rearrange. Each runs its commands through a history one at a time,
/// reading the track as the last one left it, so the caller wraps a whole gesture in one
/// transaction and one undo takes it back exactly. Times are seconds from the track's start,
/// rounded to its frames. A transition in the way is refused rather than broken.
/// </summary>
public static class TrackEdits
{
    /// <summary>Where each child of a track starts and ends, transitions overlapping their neighbours.</summary>
    /// <param name="track">The track.</param>
    /// <returns>Each child's index, start and end in seconds.</returns>
    public static IReadOnlyList<(int Index, double Start, double End)> Placed(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        List<(int, double, double)> placed = [];
        for (int i = 0; i < track.Children.Count; i++)
        {
            TimeRange range = track.RangeOfChildAtIndex(i);
            placed.Add((i, range.StartTime.Seconds, range.StartTime.Seconds + range.Duration.Seconds));
        }

        return placed;
    }

    /// <summary>Where a track ends: the end of its last child that does not overlap.</summary>
    /// <param name="track">The track.</param>
    /// <returns>Seconds.</returns>
    public static double End(Track track) =>
        Placed(track).Where(p => track.Children[p.Index] is not Transition).Select(p => p.End).DefaultIfEmpty(0).Max();

    /// <summary>The frame rate a track's times are rounded to: its first item's.</summary>
    /// <param name="track">The track.</param>
    /// <returns>Frames a second.</returns>
    public static double Rate(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Children.OfType<Item>().Select(i => i.Duration.Rate).FirstOrDefault(r => r > 0) is var rate and > 0 ? rate : 24;
    }

    /// <summary>Cuts the item playing at a time in two there; nothing when the time is already a cut or past the end.</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">The track.</param>
    /// <param name="seconds">Where.</param>
    /// <exception cref="EditRefusedException">A transition overlaps the time.</exception>
    public static void SplitAt(History history, Track track, double seconds)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(track);
        double rate = Rate(track);
        double at = Math.Round(seconds * rate) / rate;
        foreach ((int index, double start, double end) in Placed(track))
        {
            if (at <= start + Half(rate) || at >= end - Half(rate))
            {
                continue;
            }

            if (track.Children[index] is not Item item)
            {
                throw new EditRefusedException($"A transition on {track.Name} covers {Clock(at)}; edit around it.");
            }

            TimeRange trimmed = item.TrimmedRange();
            double itemRate = trimmed.Duration.Rate > 0 ? trimmed.Duration.Rate : rate;
            RationalTime head = new(Math.Round((at - start) * itemRate), itemRate);
            RationalTime start0 = trimmed.StartTime.RescaledTo(itemRate);
            RationalTime whole = trimmed.Duration.RescaledTo(itemRate);
            Item tail = Copy(item);
            history.Execute(new SetSourceRange(item, new TimeRange(start0, head)));
            history.Execute(new InsertChild(track, index + 1, tail));
            history.Execute(new SetSourceRange(tail, new TimeRange(start0 + head, whole - head)));
            return;
        }
    }

    /// <summary>Takes out everything between two times, closing the hole (a ripple delete of the range).</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">The track.</param>
    /// <param name="from">The first second.</param>
    /// <param name="to">The last.</param>
    public static void Extract(History history, Track track, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(track);
        if (to <= from || from >= End(track))
        {
            return;
        }

        SplitAt(history, track, from);
        SplitAt(history, track, to);
        double half = Half(Rate(track));
        foreach ((int index, double start, double end) in Placed(track).Reverse())
        {
            if (start >= from - half && end <= to + half)
            {
                if (track.Children[index] is Transition)
                {
                    throw new EditRefusedException($"A transition on {track.Name} lies in the range; edit around it.");
                }

                history.Execute(new RemoveChild(track, index));
            }
        }
    }

    /// <summary>Puts items over a track from a time, replacing what they cover; a track that ends before the time is padded with a gap.</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">The track.</param>
    /// <param name="seconds">Where they start.</param>
    /// <param name="items">What goes there, held by nothing, in order.</param>
    public static void Overwrite(History history, Track track, double seconds, IReadOnlyList<Composable> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        double length = items.Where(i => !i.Overlapping).Sum(i => i.Duration.Seconds);
        Pad(history, track, seconds);
        Extract(history, track, seconds, seconds + length);
        Place(history, track, seconds, items);
        Tidy(history, track);
    }

    /// <summary>Puts items into a track at a time, pushing everything after it later; a track that ends before the time is padded.</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">The track.</param>
    /// <param name="seconds">Where they start.</param>
    /// <param name="items">What goes there, held by nothing, in order.</param>
    public static void Insert(History history, Track track, double seconds, IReadOnlyList<Composable> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Pad(history, track, seconds);
        SplitAt(history, track, seconds);
        Place(history, track, seconds, items);
        Tidy(history, track);
    }

    /// <summary>Takes a child out and leaves a gap as long as it was, so nothing else moves.</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">Its track.</param>
    /// <param name="child">The child.</param>
    /// <returns>The child, held by nothing now.</returns>
    public static Composable Lift(History history, Track track, Composable child)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(child);
        int index = Index(track, child);
        Refuse(track, index);
        RationalTime length = child.Duration;
        history.Execute(new RemoveChild(track, index));
        history.Execute(new InsertChild(track, index, Gap(length)));
        Tidy(history, track);
        return child;
    }

    /// <summary>Takes a child out and closes the hole it leaves.</summary>
    /// <param name="history">Where the commands run.</param>
    /// <param name="track">Its track.</param>
    /// <param name="child">The child.</param>
    /// <returns>The child, held by nothing now.</returns>
    public static Composable ExtractChild(History history, Track track, Composable child)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(track);
        int index = Index(track, child);
        Refuse(track, index);
        history.Execute(new RemoveChild(track, index));
        Tidy(history, track);
        return child;
    }

    /// <summary>A copy of a composable, held by nothing, through the reference's own format.</summary>
    /// <param name="child">What to copy.</param>
    /// <returns>The copy.</returns>
    public static Composable Duplicate(Composable child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return (Composable)OtioReader.ReadString(OtioWriter.Write(child));
    }

    private static Item Copy(Item item) => (Item)Duplicate(item);

    private static double Half(double rate) => 0.5 / rate;

    private static string Clock(double seconds) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds:0.###} s");

    private static int Index(Track track, Composable child)
    {
        int index = track.Children.ToList().IndexOf(child);
        return index >= 0 ? index : throw new ArgumentException($"{child.Name} is not on {track.Name}", nameof(child));
    }

    private static void Refuse(Track track, int index)
    {
        if ((index > 0 && track.Children[index - 1] is Transition) || (index < track.Children.Count - 1 && track.Children[index + 1] is Transition))
        {
            throw new EditRefusedException($"{track.Children[index].Name} has a transition beside it; edit around it.");
        }
    }

    private static Gap Gap(RationalTime length) =>
        (Gap)OtioReader.ReadString(
            "{\"OTIO_SCHEMA\":\"Gap.1\",\"name\":\"\",\"source_range\":{\"OTIO_SCHEMA\":\"TimeRange.1\",\"start_time\":{\"OTIO_SCHEMA\":\"RationalTime.1\",\"value\":0,\"rate\":"
            + length.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"duration\":{\"OTIO_SCHEMA\":\"RationalTime.1\",\"value\":"
            + length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"rate\":"
            + length.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}}");

    /// <summary>A gap up to a time on a track that ends before it.</summary>
    private static void Pad(History history, Track track, double seconds)
    {
        double rate = Rate(track);
        double short0 = Math.Round((seconds - End(track)) * rate);
        if (short0 > 0)
        {
            history.Execute(new InsertChild(track, track.Children.Count, Gap(new RationalTime(short0, rate))));
        }
    }

    /// <summary>Items in at the first cut at a time, which the callers have made one.</summary>
    private static void Place(History history, Track track, double seconds, IReadOnlyList<Composable> items)
    {
        double half = Half(Rate(track));
        int index = Placed(track).Where(p => track.Children[p.Index] is not Transition && p.Start >= seconds - half).Select(p => p.Index).DefaultIfEmpty(track.Children.Count).First();
        foreach (Composable item in items)
        {
            history.Execute(new InsertChild(track, index++, item));
        }
    }

    /// <summary>Neighbouring gaps become one and a gap at the end goes, so a track is never longer than what it plays.</summary>
    private static void Tidy(History history, Track track)
    {
        for (int i = track.Children.Count - 1; i > 0; i--)
        {
            if (track.Children[i] is Gap later && track.Children[i - 1] is Gap earlier)
            {
                RationalTime both = earlier.Duration + later.Duration.RescaledTo(earlier.Duration.Rate);
                history.Execute(new SetSourceRange(earlier, new TimeRange(new RationalTime(0, both.Rate), both)));
                history.Execute(new RemoveChild(track, i));
            }
        }

        while (track.Children.Count > 0 && track.Children[^1] is Gap)
        {
            history.Execute(new RemoveChild(track, track.Children.Count - 1));
        }
    }
}
