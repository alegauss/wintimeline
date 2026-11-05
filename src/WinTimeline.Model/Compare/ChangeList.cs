using System.Globalization;
using System.Text;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Compare;

/// <summary>
/// WT162: a comparison handed on. As a CMX 3600 change list, each change is an event carrying
/// the clip's source timecodes and where it plays in the later version (or played, for a removed
/// clip), with a FROM CLIP NAME and a comment saying the change in words; as markers, each change
/// is a marker on the later timeline at the same place, coloured by its kind, its sentence the note.
/// </summary>
public static class ChangeList
{
    /// <summary>The colour each kind of change is marked in.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A marker colour the reference names.</returns>
    public static string ColorOf(ChangeKind kind) => kind switch
    {
        ChangeKind.Removed => "RED",
        ChangeKind.Added => "GREEN",
        ChangeKind.Replaced => "ORANGE",
        ChangeKind.Moved => "BLUE",
        _ => "YELLOW",
    };

    /// <summary>The comparison as a CMX 3600 change list.</summary>
    /// <param name="diff">The comparison.</param>
    /// <param name="after">The later version, whose timecode the events are recorded in.</param>
    /// <returns>The EDL's text.</returns>
    public static string Edl(TimelineDiff diff, Timeline after)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(after);
        RationalTime origin = after.GlobalStartTime ?? new RationalTime(0, 24);
        double rate = origin.Rate > 0 ? origin.Rate : 24;
        StringBuilder edl = new();
        edl.Append(CultureInfo.InvariantCulture, $"TITLE: {after.Name} changes\nFCM: NON-DROP FRAME\n");
        // A removed clip's place is where it was, which the later cut has filled, so events are
        // written in record order and one that would overlap the last on its track starts where
        // that one ends; its own place stays in its comment, as CMX 3600 readers refuse overlaps.
        int number = 0;
        Dictionary<string, RationalTime> ends = new(StringComparer.Ordinal);
        foreach (ClipChange change in diff.Changes.OrderBy(c => c.Track, StringComparer.Ordinal).ThenBy(c => c.Seconds))
        {
            Clip clip = change.After ?? change.Before!;
            TimeRange source = clip.TrimmedRange();
            double frames = Math.Max(1, Math.Round(source.Duration.Seconds * rate));
            RationalTime at = origin.RescaledTo(rate) + new RationalTime(Math.Round(change.Seconds * rate), rate);
            string track = change.Track.StartsWith('A') ? "A" : "V";
            RationalTime recordIn = ends.TryGetValue(track, out RationalTime end) && end > at ? end : at;
            ends[track] = recordIn + new RationalTime(frames, rate);
            RationalTime sourceIn = new(Math.Round(source.StartTime.Seconds * rate), rate);
            edl.Append(CultureInfo.InvariantCulture, $"\n{++number:000}  {Reel(clip)} {track,-5} C        ");
            edl.Append(CultureInfo.InvariantCulture, $"{Tc(sourceIn, rate)} {Tc(sourceIn + new RationalTime(frames, rate), rate)} {Tc(recordIn, rate)} {Tc(ends[track], rate)}\n");
            edl.Append(CultureInfo.InvariantCulture, $"* FROM CLIP NAME: {clip.Name}\n");
            edl.Append(CultureInfo.InvariantCulture, $"* CHANGE: {change.Text}\n");
            if (recordIn != at)
            {
                edl.Append(CultureInfo.InvariantCulture, $"* RECORD AT: {Tc(at, rate)}\n");
            }
        }

        return edl.ToString();
    }

    /// <summary>A marker per change on the later timeline, through its history, inside the caller's transaction.</summary>
    /// <param name="history">The later timeline's history.</param>
    /// <param name="diff">The comparison.</param>
    /// <param name="after">The later version.</param>
    /// <returns>How many markers it added.</returns>
    public static int Mark(History history, TimelineDiff diff, Timeline after)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(after);
        double rate = after.GlobalStartTime is { Rate: > 0 } start ? start.Rate : 24;
        foreach (ClipChange change in diff.Changes)
        {
            TimeRange at = new(new RationalTime(Math.Round(change.Seconds * rate), rate), new RationalTime(0, rate));
            string name = change.Kind.ToString();
            history.Execute(new AddMarker(after.Tracks, name, at, ColorOf(change.Kind)));
            history.Execute(new EditMarker(after.Tracks.Markers[^1], name, ColorOf(change.Kind), at, change.Text));
        }

        return diff.Changes.Count;
    }

    /// <summary>A reel name: the clip's, in the eight characters CMX 3600 allows, letters and digits only.</summary>
    private static string Reel(Clip clip)
    {
        string reel = new([.. clip.Name.Where(char.IsLetterOrDigit).Take(8)]);
        return (reel.Length == 0 ? "AX" : reel.ToUpperInvariant()).PadRight(8);
    }

    private static string Tc(RationalTime time, double rate) => time.ToTimecode(rate);
}
