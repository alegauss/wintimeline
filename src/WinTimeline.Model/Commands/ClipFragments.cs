using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Commands;

/// <summary>What a paste would do: the commands to run, in order, as one undo step.</summary>
/// <param name="Commands">The inserts, each index already counting the ones before it.</param>
/// <param name="Items">How many clips, transitions and nested items it pastes, gaps not counted.</param>
/// <param name="Lanes">The lanes it pastes onto, e.g. "V1".</param>
public sealed record PastePlan(IReadOnlyList<IEditCommand> Commands, int Items, IReadOnlyList<string> Lanes);

/// <summary>
/// WT160: clips copied out of a timeline and pasted into it or another, as an OTIO
/// SerializableCollection fragment in the reference's own format: one track per lane copied,
/// named by its lane ("V1", "A2"), holding the copied items with gaps keeping their places
/// relative to the earliest. Media URLs are made absolute on copy, so a paste into a document
/// in another folder finds the same files.
/// </summary>
public static class ClipFragment
{
    /// <summary>The clipboard format a fragment is put on the clipboard under, beside plain text.</summary>
    public const string Format = "WinTimeline.OtioFragment";

    private const string MarkKey = "wintimeline:clipboard";

    /// <summary>The fragment for items of a timeline's top-level tracks; null when none are.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="items">The items, in any order; those not on a top-level track are left out.</param>
    /// <param name="documentFolder">The folder its document is in, which relative media URLs are made absolute against; null for none.</param>
    /// <param name="within">WT56: the stack the items are on, a compound opened in the panel; null for the timeline's own.</param>
    /// <returns>The fragment's JSON, or null.</returns>
    public static string? Copy(Timeline timeline, IEnumerable<Composable> items, string? documentFolder, Stack? within = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(items);
        HashSet<Composable> chosen = new(items, ReferenceEqualityComparer.Instance);
        List<(Track Track, string Lane, List<int> Indices)> lanes = [];
        foreach ((Track track, string lane) in Lanes(within ?? timeline.Tracks))
        {
            List<int> indices = [.. track.Children.Select((c, i) => (c, i)).Where(p => chosen.Contains(p.c) && Copyable(track, p.i, chosen)).Select(p => p.i)];
            if (indices.Count > 0)
            {
                lanes.Add((track, lane, indices));
            }
        }

        if (lanes.Count == 0)
        {
            return null;
        }

        double earliest = lanes.Min(l => Start(l.Track, l.Indices[0]));
        JsonArray tracks = [];
        foreach ((Track track, string lane, List<int> indices) in lanes)
        {
            JsonArray children = [];
            double at = earliest;
            foreach (int index in indices)
            {
                Composable child = track.Children[index];
                if (child is not Transition)
                {
                    double start = Start(track, index);
                    if (start - at > 1e-9)
                    {
                        children.Add(Gap(start - at, child.Duration.Rate));
                    }

                    at = start + child.Duration.Seconds;
                }

                JsonNode copy = JsonNode.Parse(OtioWriter.Write(child))!;
                Absolute(copy, documentFolder);
                children.Add(copy);
            }

            tracks.Add(new JsonObject
            {
                ["OTIO_SCHEMA"] = "Track.1",
                ["name"] = lane,
                ["kind"] = track.Kind,
                ["children"] = children,
            });
        }

        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "SerializableCollection.1",
            ["name"] = "WinTimeline clipboard",
            ["metadata"] = new JsonObject { [MarkKey] = 1 },
            ["children"] = tracks,
        }.ToJsonString();
    }

    /// <summary>
    /// What pasting a fragment at a time does: each lane's items go in at the first cut at or after
    /// the time on the lane of the same name, rippling what follows, so no clip is cut in two; a
    /// lane that ends before the time gets a gap up to it first.
    /// </summary>
    /// <param name="timeline">The timeline pasted into.</param>
    /// <param name="fragment">A fragment <see cref="Copy"/> made.</param>
    /// <param name="seconds">Where, in seconds from the timeline's start: usually the playhead.</param>
    /// <param name="within">WT56: the stack pasted into, a compound opened in the panel; null for the timeline's own.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="EditRefusedException">The text is no fragment, or the timeline lacks a lane it names.</exception>
    public static PastePlan Plan(Timeline timeline, string fragment, double seconds, Stack? within = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(fragment);
        if (Read(fragment) is not { } collection)
        {
            throw new EditRefusedException("The clipboard holds no clips copied from a timeline.");
        }

        Dictionary<string, Track> targets = Lanes(within ?? timeline.Tracks).ToDictionary(l => l.Lane, l => l.Track, StringComparer.Ordinal);
        List<IEditCommand> commands = [];
        List<string> lanes = [];
        int items = 0;
        foreach (Track source in collection.Children.OfType<Track>())
        {
            if (!targets.TryGetValue(source.Name, out Track? target))
            {
                throw new EditRefusedException($"{timeline.Name} has no {source.Name} to paste onto.");
            }

            (int index, double end) = Cut(target, seconds);
            if (seconds - end > 1e-9)
            {
                commands.Add(new InsertChild(target, index++, Unowned(GapItem(seconds - end, source.Children.Count > 0 ? source.Children[0].Duration.Rate : 24))));
            }

            foreach (Composable child in source.Children)
            {
                commands.Add(new InsertChild(target, index++, Unowned(child)));
                items += child is Gap ? 0 : 1;
            }

            lanes.Add(source.Name);
        }

        return new PastePlan(commands, items, lanes);
    }

    /// <summary>The fragment a text holds, or null when it is not one this app copied.</summary>
    private static SerializableCollection? Read(string text)
    {
        try
        {
            return OtioReader.ReadString(text) is SerializableCollection collection && collection.Metadata.ContainsKey(MarkKey) ? collection : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>The timeline's top-level tracks with their lanes' names: V1 is the lowest video track, A1 the first audio.</summary>
    private static IEnumerable<(Track Track, string Lane)> Lanes(Stack stack) =>
    [
        .. stack.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Video).Select((t, i) => (t, $"V{i + 1}")),
        .. stack.Children.OfType<Track>().Where(t => t.Kind == TrackKind.Audio).Select((t, i) => (t, $"A{i + 1}")),
    ];

    /// <summary>A transition is copied only with both its neighbours, which it overlaps.</summary>
    private static bool Copyable(Track track, int index, HashSet<Composable> chosen) =>
        track.Children[index] is not Transition
        || (index > 0 && index < track.Children.Count - 1 && chosen.Contains(track.Children[index - 1]) && chosen.Contains(track.Children[index + 1]));

    private static double Start(Track track, int index) => track.RangeOfChildAtIndex(index).StartTime.Seconds;

    /// <summary>The index of the first child starting at or after a time, and where the track's last non-overlapping child ends.</summary>
    private static (int Index, double End) Cut(Track track, double seconds)
    {
        double at = 0;
        for (int i = 0; i < track.Children.Count; i++)
        {
            Composable child = track.Children[i];
            if (child.Overlapping)
            {
                continue;
            }

            if (at >= seconds - 1e-9)
            {
                return (i > 0 && track.Children[i - 1] is Transition ? i - 1 : i, at);
            }

            at += child.Duration.Seconds;
        }

        return (track.Children.Count, at);
    }

    private static Composable Unowned(Composable child) => (Composable)OtioReader.ReadString(OtioWriter.Write(child));

    private static Composable GapItem(double seconds, double rate) => (Composable)OtioReader.ReadString(Gap(seconds, rate).ToJsonString());

    private static JsonObject Gap(double seconds, double rate) => new()
    {
        ["OTIO_SCHEMA"] = "Gap.1",
        ["name"] = string.Empty,
        ["source_range"] = new JsonObject
        {
            ["OTIO_SCHEMA"] = "TimeRange.1",
            ["start_time"] = new JsonObject { ["OTIO_SCHEMA"] = "RationalTime.1", ["value"] = 0, ["rate"] = rate },
            ["duration"] = new JsonObject { ["OTIO_SCHEMA"] = "RationalTime.1", ["value"] = Math.Round(seconds * rate), ["rate"] = rate },
        },
    };

    /// <summary>Every target_url under a node that names a relative file, made absolute against the document's folder.</summary>
    private static void Absolute(JsonNode? node, string? documentFolder)
    {
        switch (node)
        {
            case JsonObject obj:
                if (documentFolder is not null && obj["target_url"]?.GetValueKind() == System.Text.Json.JsonValueKind.String
                    && FileUrl.ToPath(obj["target_url"]!.GetValue<string>()) is { Length: > 0 } path && !Path.IsPathRooted(path))
                {
                    obj["target_url"] = FileUrl.FromPath(Path.Combine(documentFolder, path));
                }

                foreach (KeyValuePair<string, JsonNode?> field in obj.ToList())
                {
                    Absolute(field.Value, documentFolder);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Absolute(item, documentFolder);
                }

                break;
        }
    }
}
