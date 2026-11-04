using WinTimeline.Model;

namespace WinTimeline.Engine.Timeline;

/// <summary>WT54: one marker where it falls on the timeline.</summary>
/// <param name="Marker">The marker.</param>
/// <param name="Owner">What it is on: the timeline's stack, a track or an item.</param>
/// <param name="Lane">The lane it is drawn on, or null for one of the timeline's own (drawn on the ruler).</param>
/// <param name="Start">Where it starts, in seconds from the timeline's start.</param>
/// <param name="Duration">How long it is, in seconds; 0 for a point.</param>
/// <param name="Id">A stable identity for UI Automation: "M" and where it is, e.g. "M-V1-3-0".</param>
public sealed record PlacedMarker(Marker Marker, Item Owner, int? Lane, double Start, double Duration, string Id)
{
    /// <summary>Where it ends.</summary>
    public double End => Start + Duration;
}

/// <summary>
/// WT54: every marker on a timeline, on its stack, its tracks and its items, placed in timeline
/// seconds. A marker on an item is in the item's own time, so it lands at the item's start plus
/// its offset from the item's trimmed start; one outside what the item plays is not shown, as the
/// reference's viewers do not show it.
/// </summary>
public static class TimelineMarkers
{
    /// <summary>The markers, sorted by where they start.</summary>
    /// <param name="layout">The timeline's layout.</param>
    /// <returns>The placed markers.</returns>
    public static IReadOnlyList<PlacedMarker> Of(TimelineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        List<PlacedMarker> placed = [];
        if (layout.Tracks is { } stack)
        {
            Add(placed, stack, null, 0, Start(stack), double.PositiveInfinity, "M-T");
        }

        foreach (Lane lane in layout.Lanes)
        {
            int index = layout.Lanes.ToList().IndexOf(lane);
            Add(placed, lane.Track, index, 0, Start(lane.Track), double.PositiveInfinity, $"M-{lane.Label}");
            foreach (LaneItem item in lane.Items)
            {
                if (item.Item is Item { Markers.Count: > 0 } marked && Start(marked) is { } trimmed)
                {
                    Add(placed, marked, index, item.Start, trimmed, item.End, "M-" + layout.IdOf(item));
                }
            }
        }

        placed.Sort((a, b) => a.Start.CompareTo(b.Start));
        return placed;
    }

    /// <summary>The next marker after a time, or the last one before it; null when there is none that way.</summary>
    /// <param name="markers">The placed markers, sorted.</param>
    /// <param name="seconds">The playhead.</param>
    /// <param name="rate">The frame rate, so a marker on the playhead's own frame is not "next".</param>
    /// <param name="forward">Which way.</param>
    /// <returns>The marker, or null.</returns>
    public static PlacedMarker? Beside(IReadOnlyList<PlacedMarker> markers, double seconds, double rate, bool forward)
    {
        ArgumentNullException.ThrowIfNull(markers);
        double half = 0.5 / rate;
        return forward
            ? markers.FirstOrDefault(m => m.Start > seconds + half)
            : markers.LastOrDefault(m => m.Start < seconds - half);
    }

    private static double? Start(Item item)
    {
        try
        {
            return item.TrimmedRange().StartTime.Seconds;
        }
        catch (TimelineRangeException)
        {
            return null;
        }
    }

    private static void Add(List<PlacedMarker> placed, Item owner, int? lane, double at, double? trimmed, double end, string id)
    {
        for (int i = 0; i < owner.Markers.Count; i++)
        {
            Marker marker = owner.Markers[i];
            double start = at + marker.MarkedRange.StartTime.Seconds - (trimmed ?? 0);
            if (start < at - 1e-9 || start > end + 1e-9)
            {
                continue;
            }

            placed.Add(new PlacedMarker(marker, owner, lane, start, Math.Max(0, marker.MarkedRange.Duration.Seconds), $"{id}-{i}"));
        }
    }
}

/// <summary>
/// WT54: the reference's marker colours as RGB, as its own viewers draw them: PINK, RED, ORANGE,
/// YELLOW, GREEN, CYAN, BLUE, PURPLE, MAGENTA, BLACK and WHITE. Any other name draws as green,
/// the reference's default.
/// </summary>
public static class MarkerColors
{
    /// <summary>The names, in the order the editor offers them.</summary>
    public static IReadOnlyList<string> Names { get; } = ["RED", "PINK", "ORANGE", "YELLOW", "GREEN", "CYAN", "BLUE", "PURPLE", "MAGENTA", "WHITE", "BLACK"];

    /// <summary>A colour name as red, green and blue from 0 to 1.</summary>
    /// <param name="name">e.g. "PURPLE"; case is ignored.</param>
    /// <returns>The colour.</returns>
    public static (float R, float G, float B) Rgb(string? name) => (name ?? string.Empty).ToUpperInvariant() switch
    {
        "PINK" => (1f, 0.75f, 0.8f),
        "RED" => (1f, 0f, 0f),
        "ORANGE" => (1f, 0.65f, 0f),
        "YELLOW" => (1f, 1f, 0f),
        "CYAN" => (0f, 1f, 1f),
        "BLUE" => (0f, 0f, 1f),
        "PURPLE" => (0.5f, 0f, 0.5f),
        "MAGENTA" => (1f, 0f, 1f),
        "BLACK" => (0f, 0f, 0f),
        "WHITE" => (1f, 1f, 1f),
        _ => (0f, 1f, 0f),
    };
}
