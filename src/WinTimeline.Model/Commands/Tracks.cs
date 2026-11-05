using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Commands;

/// <summary>Thrown when an edit would change something the editor has locked.</summary>
public sealed class EditRefusedException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public EditRefusedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public EditRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">The cause.</param>
    public EditRefusedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// WT50: a track's editor state that OTIO has no field for, kept in its metadata under
/// "wintimeline:" keys so other tools carry it through untouched: locked, solo and hidden. Muting
/// audio or disabling video is OTIO's own enabled flag. Every change is an undoable command.
/// </summary>
public static class TrackState
{
    /// <summary>The metadata key that locks a track.</summary>
    public const string LockedKey = "wintimeline:locked";

    /// <summary>The metadata key that solos a track.</summary>
    public const string SoloKey = "wintimeline:solo";

    /// <summary>The metadata key that hides a video track from the viewer.</summary>
    public const string HiddenKey = "wintimeline:hidden";

    /// <summary>Whether the track is locked.</summary>
    /// <param name="track">The track.</param>
    /// <returns>True when locked.</returns>
    public static bool IsLocked(Track track) => Flag(track, LockedKey);

    /// <summary>Whether the track is soloed.</summary>
    /// <param name="track">The track.</param>
    /// <returns>True when soloed.</returns>
    public static bool IsSolo(Track track) => Flag(track, SoloKey);

    /// <summary>Whether the track is hidden from the viewer.</summary>
    /// <param name="track">The track.</param>
    /// <returns>True when hidden.</returns>
    public static bool IsHidden(Track track) => Flag(track, HiddenKey);

    /// <summary>WT169: the metadata key naming a track's colour, e.g. "BLUE", in OTIO's marker colour names.</summary>
    public const string ColorKey = "wintimeline:color";

    /// <summary>WT169: the track's colour name, or null for none.</summary>
    /// <param name="track">The track.</param>
    /// <returns>e.g. "BLUE".</returns>
    public static string? ColorOf(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Metadata.TryGetValue(ColorKey, out object? value) && value is string { Length: > 0 } name ? name : null;
    }

    /// <summary>WT169: the command that colours a track; null takes its colour away.</summary>
    /// <param name="track">The track.</param>
    /// <param name="color">A colour name, e.g. "BLUE", or null.</param>
    /// <returns>The command.</returns>
    public static SetMetadata SetColor(Track track, string? color) => new(track, ColorKey, color);

    /// <summary>The command that sets one of the flags; false removes the key, so an unlocked track carries nothing.</summary>
    /// <param name="track">The track.</param>
    /// <param name="key">One of the keys above.</param>
    /// <param name="on">Whether it is set.</param>
    /// <returns>The command.</returns>
    public static SetMetadata Set(Track track, string key, bool on) => new(track, key, on ? true : null);

    private static bool Flag(Track track, string key)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Metadata.TryGetValue(key, out object? value) && value is true;
    }
}

/// <summary>WT50: adding, deleting and reordering a timeline's tracks, as the pipeline's own commands.</summary>
public static class TrackCommands
{
    /// <summary>A new empty track at a position among the timeline's tracks.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="index">Where it goes in the stack, 0 first.</param>
    /// <param name="kind">Video or Audio.</param>
    /// <param name="name">Its name, e.g. "V3".</param>
    /// <returns>The command.</returns>
    public static InsertChild Add(Timeline timeline, int index, string kind, string name)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        JsonObject track = new() { ["OTIO_SCHEMA"] = "Track.1", ["name"] = name, ["kind"] = kind, ["children"] = new JsonArray() };
        return new InsertChild(timeline.Tracks, Math.Clamp(index, 0, timeline.Tracks.Children.Count), (Composable)OtioReader.ReadString(track.ToJsonString()));
    }

    /// <summary>Deletes a track.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="track">The track.</param>
    /// <returns>The command.</returns>
    public static RemoveChild Delete(Timeline timeline, Track track)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return new RemoveChild(timeline.Tracks, IndexOf(timeline, track));
    }

    /// <summary>Moves a track to another position in the stack.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="track">The track.</param>
    /// <param name="index">Where it goes.</param>
    /// <returns>The command.</returns>
    public static MoveChild Move(Timeline timeline, Track track, int index)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return new MoveChild(timeline.Tracks, IndexOf(timeline, track), timeline.Tracks, Math.Clamp(index, 0, timeline.Tracks.Children.Count - 1));
    }

    /// <summary>
    /// The guard a history runs before every edit: an edit that would change an item on a locked
    /// track, or delete or move a locked track, is refused with the track's name. Changing the
    /// lock itself, or the track's own name, solo or visibility, is allowed.
    /// </summary>
    /// <param name="timeline">The timeline the history edits.</param>
    /// <returns>The guard: the reason to refuse a command, or null.</returns>
    public static Func<IEditCommand, string?> LockGuard(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return command => Touched(timeline, command).Where(TrackState.IsLocked).Select(t => $"{t.Name} is locked").FirstOrDefault();
    }

    private static int IndexOf(Timeline timeline, Track track)
    {
        int index = timeline.Tracks.Children.ToList().IndexOf(track);
        return index >= 0 ? index : throw new ArgumentException($"{track.Name} is not a track of {timeline.Name}", nameof(track));
    }

    /// <summary>The top-level tracks whose items a command changes.</summary>
    private static IEnumerable<Track> Touched(Timeline timeline, IEditCommand command)
    {
        switch (command)
        {
            case SetMetadata { Target: Track } or Rename { Target: Track } or SetEnabled { Target: Track }:
                yield break;
            case RemoveChild remove when ReferenceEquals(remove.Parent, timeline.Tracks):
                if (remove.Index < timeline.Tracks.Children.Count && timeline.Tracks.Children[remove.Index] is Track removed)
                {
                    yield return removed;
                }

                yield break;
            case MoveChild move when ReferenceEquals(move.From, timeline.Tracks):
                if (move.FromIndex < timeline.Tracks.Children.Count && timeline.Tracks.Children[move.FromIndex] is Track moved)
                {
                    yield return moved;
                }

                yield break;
            case InsertChild insert when ReferenceEquals(insert.Parent, timeline.Tracks):
                yield break;
            case Transaction:
                yield break;
        }

        foreach (Composable? touched in command switch
        {
            SetSourceRange c => [c.Target],
            InsertChild c => [c.Parent],
            RemoveChild c => [c.Parent],
            MoveChild c => [c.From, c.To],
            Rename c => [c.Target],
            SetEnabled c => [c.Target],
            SetMetadata c => [c.Target],
            SetTransitionOffsets c => [c.Target],
            AddMarker c => [c.Target],
            Relink c => [c.Target],
            _ => Array.Empty<Composable?>(),
        })
        {
            if (TrackOf(timeline, touched) is { } track)
            {
                yield return track;
            }
        }
    }

    /// <summary>The top-level track something sits in, itself when it is one, or null.</summary>
    private static Track? TrackOf(Timeline timeline, Composable? item)
    {
        for (Composable? at = item; at is not null; at = at.Parent)
        {
            if (at is Track track && ReferenceEquals(track.Parent, timeline.Tracks))
            {
                return track;
            }
        }

        return null;
    }
}
