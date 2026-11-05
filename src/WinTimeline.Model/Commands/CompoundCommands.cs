using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Commands;

/// <summary>
/// WT56: nesting a run of a track's items into a compound clip (a Stack holding one track of
/// them, where they were) and flattening one back. Both move the items themselves rather than
/// copies, so every clip keeps its source range, markers and metadata, and the program plays
/// exactly as before. The commands run in order as one history transaction.
/// </summary>
public static class Compound
{
    /// <summary>The commands that nest the items from one index to another of a track into a new compound.</summary>
    /// <param name="track">The track.</param>
    /// <param name="first">The first item's index.</param>
    /// <param name="last">The last item's index.</param>
    /// <param name="name">The compound's name.</param>
    /// <returns>The commands.</returns>
    /// <exception cref="EditRefusedException">The run starts or ends on a transition, or holds nothing that plays.</exception>
    public static IReadOnlyList<IEditCommand> Make(Track track, int first, int last, string name)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(name);
        if (first < 0 || last >= track.Children.Count || first > last)
        {
            throw new ArgumentOutOfRangeException(nameof(first), $"{first} to {last} is not a run of {track.Name}'s children");
        }

        if (track.Children[first] is Transition || track.Children[last] is Transition)
        {
            throw new EditRefusedException("A compound cannot start or end on a transition: it overlaps a clip left outside.");
        }

        if (track.Children.Skip(first).Take(last - first + 1).All(c => c is Gap))
        {
            throw new EditRefusedException("Only gaps are selected; a compound needs a clip.");
        }

        Composable stack = (Composable)OtioReader.ReadString(new JsonObject
        {
            ["OTIO_SCHEMA"] = "Stack.1",
            ["name"] = name,
            ["children"] = new JsonArray(new JsonObject
            {
                ["OTIO_SCHEMA"] = "Track.1",
                ["name"] = track.Name,
                ["kind"] = track.Kind,
                ["children"] = new JsonArray(),
            }),
        }.ToJsonString());
        Track inner = (Track)((Composition)stack).Children[0];

        List<IEditCommand> commands = [new InsertChild(track, first, stack)];
        for (int i = 0; i <= last - first; i++)
        {
            commands.Add(new MoveChild(track, first + 1, inner, i));
        }

        return commands;
    }

    /// <summary>The commands that flatten a compound of one track back into the track that holds it.</summary>
    /// <param name="track">The track holding it.</param>
    /// <param name="index">Its index there.</param>
    /// <returns>The commands.</returns>
    /// <exception cref="EditRefusedException">It is not a compound of one track, or it is trimmed.</exception>
    public static IReadOnlyList<IEditCommand> BreakApart(Track track, int index)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.Children.ElementAtOrDefault(index) is not Stack stack)
        {
            throw new EditRefusedException("Only a compound clip can be broken apart.");
        }

        if (stack.Children is not [Track inner])
        {
            throw new EditRefusedException($"{stack.Name} holds {stack.Children.Count} tracks; only a compound of one track flattens onto a track.");
        }

        // A compound that plays less than it holds would need its outer items trimmed (WT72).
        if (stack.SourceRange is { } shown && !shown.Equals(stack.AvailableRange()))
        {
            throw new EditRefusedException($"{stack.Name} is trimmed; breaking apart a trimmed compound waits on trimming.");
        }

        List<IEditCommand> commands = [];
        for (int i = 0; i < inner.Children.Count; i++)
        {
            commands.Add(new MoveChild(inner, 0, track, index + 1 + i));
        }

        commands.Add(new RemoveChild(track, index));
        return commands;
    }
}
