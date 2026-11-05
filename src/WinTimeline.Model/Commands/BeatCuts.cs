using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>
/// WT121: clips laid on beats. Beat markers are named <see cref="BeatName"/>; Cut to beats takes
/// the selected clips in their order and puts each from one chosen beat to the next, trimming its
/// tail to fit, or keeping its length where it is shorter than the gap.
/// </summary>
public static class BeatCuts
{
    /// <summary>What a beat marker is called.</summary>
    public const string BeatName = "beat";

    /// <summary>The colour beat markers carry.</summary>
    public const string BeatColor = "YELLOW";

    /// <summary>The commands that mark beats on an item, at times in its own media.</summary>
    /// <param name="item">The item.</param>
    /// <param name="mediaSeconds">Each beat, in seconds of the item's media.</param>
    /// <returns>The commands, one marker each.</returns>
    public static IReadOnlyList<IEditCommand> Mark(Item item, IEnumerable<double> mediaSeconds)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(mediaSeconds);
        double rate = item.Duration.Rate > 0 ? item.Duration.Rate : 24;
        return [.. mediaSeconds.Select(s => (IEditCommand)new AddMarker(
            item, BeatName, new TimeRange(new RationalTime(Math.Round(s * rate), rate), new RationalTime(0, rate)), BeatColor))];
    }

    /// <summary>Lays clips on a track from beat to beat, every <paramref name="every"/>th beat, each trimmed to its gap.</summary>
    /// <param name="history">Where the commands run, inside the caller's transaction.</param>
    /// <param name="track">The track the clips are on and go to.</param>
    /// <param name="clips">The clips, in the order they are laid.</param>
    /// <param name="beats">The beats, in timeline seconds, sorted.</param>
    /// <param name="every">1 for every beat, 2 for every second, and so on.</param>
    /// <returns>How many clips were laid.</returns>
    /// <exception cref="EditRefusedException">Fewer than two beats to lay a clip between.</exception>
    public static int Cut(History history, Track track, IReadOnlyList<Item> clips, IReadOnlyList<double> beats, int every)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(beats);
        double[] chosen = [.. beats.Where((_, i) => i % Math.Max(1, every) == 0)];
        if (chosen.Length < 2)
        {
            throw new EditRefusedException("Cut to beats needs two beats or more: detect them on the music first.");
        }

        int laid = 0;
        List<Item> lifted = [.. clips.Select(c => (Item)TrackEdits.Lift(history, track, c))];
        foreach (Item clip in lifted)
        {
            if (laid + 1 >= chosen.Length)
            {
                break;
            }

            double gap = chosen[laid + 1] - chosen[laid];
            TimeRange trimmed = clip.TrimmedRange();
            double rate = trimmed.Duration.Rate > 0 ? trimmed.Duration.Rate : 24;
            RationalTime length = new(Math.Round(Math.Min(gap, trimmed.Duration.Seconds) * rate), rate);
            history.Execute(new SetSourceRange(clip, new TimeRange(trimmed.StartTime, length)));
            TrackEdits.Overwrite(history, track, chosen[laid], [clip]);
            laid++;
        }

        return laid;
    }
}
