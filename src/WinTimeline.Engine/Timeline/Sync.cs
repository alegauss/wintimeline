using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.Engine.Timeline;

/// <summary>
/// WT79: which items of a layout belong together, by link or group, and how far a linked item
/// has slid out of sync with its link. Items are in sync when each starts on the timeline the same
/// distance before the media frame it plays first: picture and sound from one source line up.
/// </summary>
public static class Sync
{
    /// <summary>The items an item belongs with: its group always, and its link when <paramref name="linked"/>.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="item">The item.</param>
    /// <param name="linked">Whether linked selection is on.</param>
    /// <returns>The others, not the item itself.</returns>
    public static IReadOnlyList<LaneItem> Partners(TimelineLayout layout, LaneItem item, bool linked)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(item);
        string? link = linked ? ItemLinks.LinkOf(item.Item) : null, group = ItemLinks.GroupOf(item.Item);
        if (link is null && group is null)
        {
            return [];
        }

        return [.. layout.Lanes.SelectMany(l => l.Items).Where(i => !ReferenceEquals(i, item)
            && ((link is not null && ItemLinks.LinkOf(i.Item) == link) || (group is not null && ItemLinks.GroupOf(i.Item) == group)))];
    }

    /// <summary>
    /// How many frames a linked item has slid against its link, its picture if it is sound, or
    /// the first other item if it is picture; null when it is in no link, in sync, or its link's
    /// other items are gone.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="item">The item.</param>
    /// <param name="rate">The frame rate the offset is counted in.</param>
    /// <returns>The offset, positive when the item is late.</returns>
    public static long? Offset(TimelineLayout layout, LaneItem item, double rate)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(item);
        if (ItemLinks.LinkOf(item.Item) is not { } link || Origin(item) is not { } mine)
        {
            return null;
        }

        List<LaneItem> others = [.. layout.Lanes.SelectMany(l => l.Items).Where(i => !ReferenceEquals(i, item) && ItemLinks.LinkOf(i.Item) == link)];
        LaneItem? against = others.FirstOrDefault(i => !layout.Lanes[i.Lane].IsAudio) ?? others.FirstOrDefault();
        if (against is null || Origin(against) is not { } theirs)
        {
            return null;
        }

        long frames = (long)Math.Round((mine - theirs) * rate);
        return frames == 0 ? null : frames;
    }

    /// <summary>Where on the timeline an item's media would start: its start less how far into the media it begins.</summary>
    private static double? Origin(LaneItem item)
    {
        try
        {
            return item.Item is Item playing ? item.Start - playing.TrimmedRange().StartTime.Seconds : null;
        }
        catch (TimelineRangeException)
        {
            return null;
        }
    }
}
