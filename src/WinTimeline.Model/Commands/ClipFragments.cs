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
    /// WT78: pastes clipboard text at a time, each lane's items onto the lane of the same name:
    /// overwriting what they cover (Ctrl+V) or inserting and pushing what follows later
    /// (Ctrl+Shift+V), a clip under the time cut in two either way. The text is a fragment this app
    /// copied or any OTIO another tool did: a clip or gap lands on V1 (A1 for sound alone), a track
    /// on the first lane of its kind, a timeline's or stack's tracks on theirs.
    /// </summary>
    /// <param name="history">Where the commands run, inside the caller's transaction.</param>
    /// <param name="timeline">The timeline pasted into.</param>
    /// <param name="text">The clipboard's text.</param>
    /// <param name="seconds">Where, in seconds from the stack's start: usually the playhead.</param>
    /// <param name="insert">Whether to insert rather than overwrite.</param>
    /// <param name="within">WT56: the stack pasted into, a compound opened in the panel; null for the timeline's own.</param>
    /// <returns>What it pasted.</returns>
    /// <exception cref="EditRefusedException">The text is no OTIO a timeline can take, or the timeline lacks a lane it names.</exception>
    public static PastePlan Paste(History history, Timeline timeline, string text, double seconds, bool insert, Stack? within = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<(string Lane, IReadOnlyList<Composable> Items)> fragment = Read(text)
            ?? throw new EditRefusedException("The clipboard holds no clips: copy some, or OpenTimelineIO text from another tool.");

        Dictionary<string, Track> targets = Lanes(within ?? timeline.Tracks).ToDictionary(l => l.Lane, l => l.Track, StringComparer.Ordinal);
        foreach ((string lane, _) in fragment)
        {
            if (!targets.ContainsKey(lane))
            {
                throw new EditRefusedException($"{timeline.Name} has no {lane} to paste onto.");
            }
        }

        foreach ((string lane, IReadOnlyList<Composable> items) in fragment)
        {
            if (insert)
            {
                TrackEdits.Insert(history, targets[lane], seconds, items);
                continue;
            }

            // An overwrite lays each clip at its own offset and passes over the gaps between, which
            // keep the lanes in step but must not blank what the paste lands on.
            double at = seconds;
            foreach (Composable item in items)
            {
                if (item is not Model.Gap && !item.Overlapping)
                {
                    TrackEdits.Overwrite(history, targets[lane], at, [item]);
                }

                at += item.Overlapping ? 0 : item.Duration.Seconds;
            }
        }

        return new PastePlan([], fragment.Sum(f => f.Items.Count(i => i is not Model.Gap)), [.. fragment.Select(f => f.Lane)]);
    }

    /// <summary>
    /// The lanes a clipboard text pastes onto and what goes on each, held by nothing; null when it
    /// is no OTIO a timeline can take.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The lanes, or null.</returns>
    public static IReadOnlyList<(string Lane, IReadOnlyList<Composable> Items)>? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ModelObject read;
        try
        {
            read = OtioReader.ReadString(text);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or InvalidCastException)
        {
            return null;
        }

        List<(string, IReadOnlyList<Composable>)> lanes = read switch
        {
            SerializableCollection collection when collection.Metadata.ContainsKey(MarkKey) =>
                [.. collection.Children.OfType<Track>().Select(t => (t.Name, Children(t)))],
            Timeline other => FromStack(other.Tracks),
            Stack stack => FromStack(stack),
            Track track => [(track.Kind == TrackKind.Audio ? "A1" : "V1", Children(track))],
            Item item and not Stack => [(item is Clip clip && IsSound(clip) ? "A1" : "V1", [TrackEdits.Duplicate(item)])],
            _ => [],
        };

        return lanes.Count > 0 && lanes.Any(l => l.Item2.Count > 0) ? lanes : null;
    }

    private static List<(string, IReadOnlyList<Composable>)> FromStack(Stack stack) =>
        [.. Lanes(stack).Select(l => (l.Lane, Children(l.Track)))];

    private static IReadOnlyList<Composable> Children(Track track) => [.. track.Children.Select(TrackEdits.Duplicate)];

    // A clip whose media is sound alone, by its file's extension, goes to A1.
    private static bool IsSound(Clip clip) =>
        clip.MediaReference is ExternalReference { TargetUrl: var url } && Path.GetExtension(url).ToUpperInvariant() is ".WAV" or ".AIFF" or ".AIF" or ".MP3" or ".FLAC" or ".AAC" or ".M4A";

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
