using System.Globalization;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>WT81: how the angles of a multicam clip are lined up.</summary>
public enum MulticamSync
{
    /// <summary>By the media time each angle starts at, its timecode.</summary>
    Timecode,

    /// <summary>By their in points: every angle starts together.</summary>
    InPoints,
}

/// <summary>
/// WT81: multicam clips, kept as plain nested stacks so other tools still read the cut. The stack
/// holds one track per angle, each its clip after a gap that lines it up with the others; the
/// angle shown is the one track left enabled, and the stack's metadata names it. Cutting to
/// another angle splits the multicam at the playhead and shows that angle from there on.
/// </summary>
public static class Multicam
{
    /// <summary>The metadata key marking a stack as a multicam clip, holding the angle shown (0 first).</summary>
    public const string AngleKey = "wintimeline:multicam-angle";

    /// <summary>The angle a multicam shows, or null when the item is no multicam clip.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The angle, 0 for the first.</returns>
    public static int? AngleOf(Composable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item is Stack && item.Metadata.TryGetValue(AngleKey, out object? value) ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>A multicam clip of clips, each an angle, held by nothing, showing the first.</summary>
    /// <param name="angles">The clips, two to nine, in the order their number keys pick them.</param>
    /// <param name="name">Its name.</param>
    /// <param name="sync">How the angles line up.</param>
    /// <returns>The multicam clip.</returns>
    /// <exception cref="EditRefusedException">Fewer than two angles or more than nine.</exception>
    public static Stack Create(IReadOnlyList<Item> angles, string name, MulticamSync sync)
    {
        ArgumentNullException.ThrowIfNull(angles);
        if (angles.Count is < 2 or > 9)
        {
            throw new EditRefusedException("A multicam clip takes two to nine angles: select them on the timeline.");
        }

        List<double> starts = [.. angles.Select(a => sync == MulticamSync.Timecode ? a.TrimmedRange().StartTime.Seconds : 0)];
        double first = starts.Min();
        JsonArray tracks = [];
        for (int i = 0; i < angles.Count; i++)
        {
            double rate = angles[i].Duration.Rate > 0 ? angles[i].Duration.Rate : 24;
            JsonArray children = [];
            double lead = Math.Round((starts[i] - first) * rate);
            if (lead > 0)
            {
                children.Add(Gap(lead, rate));
            }

            children.Add(JsonNode.Parse(OtioWriter.Write(angles[i])));
            tracks.Add(new JsonObject
            {
                ["OTIO_SCHEMA"] = "Track.1",
                ["name"] = string.Create(CultureInfo.InvariantCulture, $"Angle {i + 1}"),
                ["kind"] = angles[i] is Clip clip && Path.GetExtension((clip.MediaReference as ExternalReference)?.TargetUrl ?? string.Empty).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                    ? TrackKind.Audio : TrackKind.Video,
                ["enabled"] = i == 0,
                ["children"] = children,
            });
        }

        // The shortest angle bounds it, so no angle switched to runs out of picture.
        double length = angles.Select((a, i) => starts[i] - first + a.Duration.Seconds).Min();
        double stackRate = angles[0].Duration.Rate > 0 ? angles[0].Duration.Rate : 24;
        return (Stack)OtioReader.ReadString(new JsonObject
        {
            ["OTIO_SCHEMA"] = "Stack.1",
            ["name"] = name,
            ["metadata"] = new JsonObject { [AngleKey] = 0 },
            ["source_range"] = Range(0, Math.Round(length * stackRate), stackRate),
            ["children"] = tracks,
        }.ToJsonString());
    }

    /// <summary>
    /// Cuts the multicam clip playing at a time on a track to an angle: split there, and the half
    /// from the time on shows the angle. Nothing when no multicam plays there or it already shows it.
    /// </summary>
    /// <param name="history">Where the commands run, inside the caller's transaction.</param>
    /// <param name="track">The track.</param>
    /// <param name="seconds">Where.</param>
    /// <param name="angle">The angle, 0 for the first.</param>
    /// <returns>Whether it cut.</returns>
    /// <exception cref="EditRefusedException">The multicam has no such angle.</exception>
    public static bool CutTo(History history, Track track, double seconds, int angle)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(track);
        Stack? playing = PlayingAt(track, seconds);
        if (playing is null || AngleOf(playing) == angle)
        {
            return false;
        }

        if (angle < 0 || angle >= playing.Children.Count)
        {
            throw new EditRefusedException(string.Create(CultureInfo.InvariantCulture, $"{playing.Name} has {playing.Children.Count} angles; there is no angle {angle + 1}."));
        }

        TrackEdits.SplitAt(history, track, seconds);
        Stack from = PlayingAt(track, seconds) ?? playing;
        history.Execute(new SetMetadata(from, AngleKey, (long)angle));
        for (int i = 0; i < from.Children.Count; i++)
        {
            if (from.Children[i] is Track angleTrack && angleTrack.Enabled != (i == angle))
            {
                history.Execute(new SetEnabled(angleTrack, i == angle));
            }
        }

        return true;
    }

    /// <summary>The multicam clip on a track that plays at a time, the one starting there at a cut.</summary>
    private static Stack? PlayingAt(Track track, double seconds)
    {
        double half = 0.5 / TrackEdits.Rate(track);
        return TrackEdits.Placed(track)
            .Where(p => p.Start <= seconds + half && seconds + half < p.End)
            .Select(p => track.Children[p.Index])
            .OfType<Stack>()
            .FirstOrDefault(s => AngleOf(s) is not null);
    }

    private static JsonObject Gap(double frames, double rate) => new()
    {
        ["OTIO_SCHEMA"] = "Gap.1",
        ["name"] = string.Empty,
        ["source_range"] = Range(0, frames, rate),
    };

    private static JsonObject Range(double start, double duration, double rate) => new()
    {
        ["OTIO_SCHEMA"] = "TimeRange.1",
        ["start_time"] = new JsonObject { ["OTIO_SCHEMA"] = "RationalTime.1", ["value"] = start, ["rate"] = rate },
        ["duration"] = new JsonObject { ["OTIO_SCHEMA"] = "RationalTime.1", ["value"] = duration, ["rate"] = rate },
    };
}
