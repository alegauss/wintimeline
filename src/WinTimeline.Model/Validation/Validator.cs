using System.Globalization;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Validation;

/// <summary>How bad a problem is.</summary>
public enum Severity
{
    /// <summary>Worth knowing; nothing breaks.</summary>
    Info,

    /// <summary>Plays, but probably not as meant.</summary>
    Warning,

    /// <summary>Cannot play or export correctly.</summary>
    Error,
}

/// <summary>Something wrong in a timeline.</summary>
/// <param name="Severity">How bad.</param>
/// <param name="Code">A stable name for the kind, e.g. "transition-too-long".</param>
/// <param name="Target">What it is about.</param>
/// <param name="Message">One sentence for the Problems panel.</param>
/// <param name="Fix">The command that fixes it, or null when only a person can.</param>
public sealed record Problem(Severity Severity, string Code, Composable Target, string Message, IEditCommand? Fix = null);

/// <summary>
/// Finds what is wrong with a timeline a tool handed over (WT19): transitions longer than the
/// items they overlap or with no item on one side, items of zero or negative duration, source
/// ranges outside what the media has, clips at another rate than the timeline, nesting deeper
/// than the renderer goes, ranges that cannot be computed, and markers sharing a name. Each
/// problem names its target; where a fix is mechanical it is an ordinary undoable command.
/// </summary>
public static class Validator
{
    /// <summary>How deep stacks and tracks may nest before the validator warns: the depth the compositor is to be built for.</summary>
    public const int MaxNesting = 8;

    /// <summary>Every problem in a timeline, in timeline order.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The problems.</returns>
    public static IReadOnlyList<Problem> Check(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        List<Problem> problems = [];
        double? rate = TimelineRate(timeline);
        Walk(timeline.Tracks, 0, rate, problems);
        return problems;
    }

    /// <summary>
    /// The rate the timeline runs at: its global start time's, else the most common rate among
    /// its clips' source ranges; null for a timeline with neither.
    /// </summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The rate, or null.</returns>
    public static double? TimelineRate(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.GlobalStartTime is { } start)
        {
            return start.Rate;
        }

        return timeline.FindClips()
            .Select(c => c.SourceRange?.Duration.Rate)
            .OfType<double>()
            .GroupBy(r => r)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => (double?)g.Key)
            .FirstOrDefault();
    }

    private static void Walk(Composition composition, int depth, double? rate, List<Problem> problems)
    {
        if (depth > MaxNesting)
        {
            problems.Add(new Problem(Severity.Warning, "nesting-too-deep", composition,
                $"{Describe(composition)} is nested {depth} deep; the compositor goes {MaxNesting} deep."));
        }

        for (int i = 0; i < composition.Children.Count; i++)
        {
            Composable child = composition.Children[i];
            if (child is Transition transition && composition is Track track)
            {
                CheckTransition(track, i, transition, problems);
            }
            else if (child is Item item)
            {
                CheckItem(item, rate, problems);
            }

            if (child is Composition inner)
            {
                Walk(inner, depth + 1, rate, problems);
            }
        }
    }

    private static void CheckTransition(Track track, int index, Transition transition, List<Problem> problems)
    {
        Item? before = index > 0 ? track.Children[index - 1] as Item : null;
        Item? after = index + 1 < track.Children.Count ? track.Children[index + 1] as Item : null;
        if (before is null && after is null)
        {
            problems.Add(new Problem(Severity.Error, "transition-without-neighbour", transition,
                $"{Describe(transition)} has no item on either side to overlap."));
            return;
        }

        // At a track's edge OpenTimelineIO lets a transition overhang it (the reference's own
        // transition_test.otio does): legal, and usually not what an editor meant.
        if (before is null || after is null)
        {
            problems.Add(new Problem(Severity.Warning, "transition-without-neighbour", transition,
                $"{Describe(transition)} has no item {(before is null ? "before" : "after")} it, so it overhangs the track's {(before is null ? "start" : "end")}."));
            return;
        }

        RationalTime? beforeDuration = Try(() => before.Duration);
        RationalTime? afterDuration = Try(() => after.Duration);
        RationalTime inOffset = transition.InOffset;
        RationalTime outOffset = transition.OutOffset;
        bool tooLong = false;
        if (beforeDuration is { } b && inOffset > b)
        {
            inOffset = b;
            tooLong = true;
        }

        if (afterDuration is { } a && outOffset > a)
        {
            outOffset = a;
            tooLong = true;
        }

        if (tooLong)
        {
            problems.Add(new Problem(Severity.Error, "transition-too-long", transition,
                $"{Describe(transition)} reaches further than {before.Name} and {after.Name} last.",
                new SetTransitionOffsets(transition, inOffset, outOffset)));
        }
    }

    private static void CheckItem(Item item, double? rate, List<Problem> problems)
    {
        RationalTime? duration = Try(() => item.Duration);

        // A composition's duration is its children's: a bad child is reported where it is, once,
        // not again on every track and stack above it.
        if (item is Composition && item.SourceRange is null)
        {
        }
        else if (duration is null)
        {
            problems.Add(new Problem(Severity.Error, "range-uncomputable", item,
                $"{Describe(item)} has no range to play: no source range and nothing available."));
        }
        else if (duration.Value.Value <= 0)
        {
            problems.Add(new Problem(Severity.Error, "non-positive-duration", item,
                $"{Describe(item)} lasts {Frames(duration.Value)}, so it shows nothing."));
        }

        if (item is Clip clip && clip.SourceRange is { } source && clip.MediaReference?.AvailableRange is { } available && duration is { Value: > 0 })
        {
            bool before = source.StartTime < available.StartTime;
            bool after = source.EndTimeExclusive > available.EndTimeExclusive;
            if (before || after)
            {
                RationalTime start = RationalTime.Max(source.StartTime, available.StartTime);
                RationalTime end = RationalTime.Min(source.EndTimeExclusive, available.EndTimeExclusive);
                IEditCommand? fix = end > start ? new SetSourceRange(clip, TimeRange.FromStartEndTime(start, end)) : null;
                problems.Add(new Problem(Severity.Warning, "source-outside-media", clip,
                    $"{Describe(clip)} plays {(before && after ? "before and after" : before ? "before" : "after")} the media it uses.",
                    fix));
            }
        }

        if (item is Clip rated && rate is { } timelineRate && rated.SourceRange is { } range && range.Duration.Rate != timelineRate)
        {
            problems.Add(new Problem(Severity.Info, "rate-mismatch", rated,
                $"{Describe(rated)} is at {Rate(range.Duration.Rate)} fps in a {Rate(timelineRate)} fps timeline."));
        }

        foreach (IGrouping<string, Marker> named in item.Markers.Where(m => m.Name.Length > 0).GroupBy(m => m.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add(new Problem(Severity.Info, "duplicate-marker-name", item,
                $"{Describe(item)} has {named.Count()} markers named \"{named.Key}\"."));
        }
    }

    private static RationalTime? Try(Func<RationalTime> compute)
    {
        try
        {
            return compute();
        }
        catch (TimelineRangeException)
        {
            return null;
        }
    }

    private static string Describe(Composable target) =>
        $"{target.Schema} \"{(target.Name.Length > 0 ? target.Name : "unnamed")}\"";

    private static string Frames(RationalTime time) =>
        string.Create(CultureInfo.InvariantCulture, $"{time.Value:0.###} frames");

    private static string Rate(double rate) => rate.ToString("0.###", CultureInfo.InvariantCulture);
}
