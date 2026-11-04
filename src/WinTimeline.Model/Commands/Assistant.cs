using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>
/// WT114: the timeline as the assistant reads it: one line per track and per item, each item
/// with the path its commands address it by, its name, where it plays as timecode and the source
/// range it shows. Text only: the model never sees pixels.
/// </summary>
public static class TimelineDigest
{
    /// <summary>The digest of a timeline.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The text.</returns>
    public static string Describe(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        RationalTime origin = timeline.GlobalStartTime ?? new RationalTime(0, 24);
        double rate = origin.Rate;
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"timeline \"{timeline.Name}\" at {rate} fps, starting {origin.ToTimecode(rate)}\n");
        for (int t = 0; t < timeline.Tracks.Children.Count; t++)
        {
            if (timeline.Tracks.Children[t] is not Track track)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"track {t} \"{track.Name}\" {track.Kind}\n");
            RationalTime at = new(0, rate);
            for (int i = 0; i < track.Children.Count; i++)
            {
                Composable child = track.Children[i];
                string where = $"{(origin + at.RescaledTo(rate)).ToTimecode(rate)}";
                switch (child)
                {
                    case Transition transition:
                        text.Append(CultureInfo.InvariantCulture, $"  {t}/{i} transition \"{transition.Name}\" at {where}\n");
                        continue;
                    case Item item:
                        RationalTime length;
                        try
                        {
                            length = item.Duration;
                        }
                        catch (TimelineRangeException)
                        {
                            length = new RationalTime(0, rate);
                        }

                        string kind = item switch { Clip => "clip", Gap => "gap", _ => item.Schema.ToLowerInvariant() };
                        string source = item.SourceRange is { } r ? $" source [{r.StartTime.Value},{r.Duration.Value}]@{r.Duration.Rate}" : string.Empty;
                        string off = item.Enabled ? string.Empty : " disabled";
                        string markers = item.Markers.Count == 0 ? string.Empty : $" markers {string.Join(", ", item.Markers.Select(m => $"\"{m.Name}\""))}";
                        text.Append(CultureInfo.InvariantCulture, $"  {t}/{i} {kind} \"{item.Name}\" {where} to {(origin + (at + length).RescaledTo(rate)).ToTimecode(rate)}{source}{off}{markers}\n");
                        at += length;
                        break;
                }
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// WT114: the edits the assistant may propose: the command pipeline's own JSON form (WT16), so a
/// proposal is checked, applied and undone by exactly the code a person's edit goes through.
/// </summary>
public static class AssistantTools
{
    /// <summary>The ops offered, each with what it does, as the prompt lists them.</summary>
    public static IReadOnlyDictionary<string, string> Ops { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["set_source_range"] = "trim an item: target path, range [[start, rate], [duration, rate]] in its media's time",
        ["remove_child"] = "delete the child at index from the track at target path",
        ["move_child"] = "move the child at from_index of from to to_index of to",
        ["rename"] = "rename the item at target path to name",
        ["set_enabled"] = "turn the item at target path on or off",
        ["add_marker"] = "add a marker named name over range on the item at target path, color RED, GREEN, BLUE…",
        ["set_metadata"] = "set metadata key to a scalar value on the object at target path",
    };

    /// <summary>
    /// The JSON Schema a reply must follow, given to the engine's grammar-constrained decoding so
    /// it cannot produce anything else: a plan in words, and the commands that carry it out.
    /// </summary>
    /// <returns>The schema.</returns>
    public static JsonObject Schema()
    {
        JsonObject time = new() { ["type"] = "array", ["prefixItems"] = new JsonArray(Number(), Number()), ["minItems"] = 2, ["maxItems"] = 2 };
        JsonObject range = new() { ["type"] = "array", ["prefixItems"] = new JsonArray(time.DeepClone(), time.DeepClone()), ["minItems"] = 2, ["maxItems"] = 2 };
        JsonObject path = new() { ["type"] = "string", ["pattern"] = "^[0-9]+(/[0-9]+)*$" };
        JsonObject index = new() { ["type"] = "integer", ["minimum"] = 0 };
        JsonObject Op(string op, params (string Name, JsonNode Schema)[] fields)
        {
            JsonObject properties = new() { ["op"] = new JsonObject { ["const"] = op } };
            foreach ((string name, JsonNode schema) in fields)
            {
                properties[name] = schema.DeepClone();
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]),
                ["additionalProperties"] = false,
            };
        }

        JsonObject text = new() { ["type"] = "string" };
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["required"] = new JsonArray("plan", "commands"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["plan"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["commands"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = AssistantProposal.MostCommands,
                    ["items"] = new JsonObject
                    {
                        ["oneOf"] = new JsonArray(
                            Op("set_source_range", ("target", path), ("range", range)),
                            Op("remove_child", ("target", path), ("index", index)),
                            Op("move_child", ("from", path), ("from_index", index), ("to", path), ("to_index", index)),
                            Op("rename", ("target", path), ("name", text)),
                            Op("set_enabled", ("target", path), ("enabled", new JsonObject { ["type"] = "boolean" })),
                            Op("add_marker", ("target", path), ("name", text), ("range", range), ("color", text)),
                            Op("set_metadata", ("target", path), ("key", text), ("value", new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") }))),
                    },
                },
            },
        };

        static JsonObject Number() => new() { ["type"] = "number" };
    }
}

/// <summary>
/// WT114: what the assistant proposed, checked before anyone sees it: every command parses,
/// targets something that exists and runs, in order, on a copy of the timeline. A proposal with
/// one bad command is refused whole, so an invalid output never reaches the document.
/// </summary>
/// <param name="Plan">The plan in words, as the preview shows it.</param>
/// <param name="Commands">The commands, in the pipeline's JSON form.</param>
/// <param name="Problems">Why it was refused; empty when it is valid.</param>
public sealed record AssistantProposal(string Plan, IReadOnlyList<JsonObject> Commands, IReadOnlyList<string> Problems)
{
    /// <summary>The most commands one proposal may carry.</summary>
    public const int MostCommands = 500;

    /// <summary>The prefix of the undo step an applied proposal makes, so the history shows which edits were the assistant's.</summary>
    public const string UndoPrefix = "AI: ";

    /// <summary>Whether it may be applied.</summary>
    public bool IsValid => Problems.Count == 0;

    /// <summary>Checks an assistant's reply against a timeline without touching it.</summary>
    /// <param name="timeline">The timeline the reply is about.</param>
    /// <param name="reply">The reply, a JSON object with plan and commands.</param>
    /// <returns>The proposal, valid or with its problems.</returns>
    public static AssistantProposal Check(Timeline timeline, string reply)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(reply);
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(reply) as JsonObject;
        }
        catch (JsonException ex)
        {
            return Refused(string.Empty, $"the reply is not JSON: {ex.Message}");
        }

        string plan = root?["plan"] is JsonValue p && p.TryGetValue(out string? words) ? words : string.Empty;
        if (root?["commands"] is not JsonArray list)
        {
            return Refused(plan, "the reply has no list of commands");
        }

        if (list.Count > MostCommands)
        {
            return Refused(plan, $"the reply has {list.Count} commands; a proposal may carry {MostCommands}");
        }

        List<JsonObject> commands = [];
        List<string> problems = [];
        if (plan.Length == 0)
        {
            problems.Add("the reply does not say what it plans");
        }

        // Run in order on a copy, so a later command addresses the timeline as the earlier ones left it.
        Timeline copy = (Timeline)OtioReader.ReadString(OtioWriter.Write(timeline));
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject command)
            {
                problems.Add($"command {i + 1} is not an object");
                continue;
            }

            string op = command["op"] is JsonValue o && o.TryGetValue(out string? name) ? name : "(none)";
            if (!AssistantTools.Ops.ContainsKey(op))
            {
                problems.Add($"command {i + 1} uses '{op}', which the assistant may not");
                continue;
            }

            try
            {
                IEditCommand parsed = CommandJson.Parse(copy, command);
                if (Sense(parsed) is { } nonsense)
                {
                    problems.Add($"command {i + 1} ({op}): {nonsense}");
                    continue;
                }

                parsed.Do();
                commands.Add((JsonObject)command.DeepClone());
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimelineRangeException or FormatException or JsonException)
            {
                problems.Add($"command {i + 1} ({op}): {ex.Message}");
            }
        }

        return new AssistantProposal(plan, commands, problems);
    }

    /// <summary>
    /// Applies a valid proposal as one undo step named "AI: plan". Should a command fail anyway
    /// (the timeline changed since the check), everything it did is undone and nothing is recorded.
    /// </summary>
    /// <param name="history">The timeline's history.</param>
    /// <param name="timeline">The timeline.</param>
    /// <exception cref="InvalidOperationException">The proposal is not valid.</exception>
    public void Apply(History history, Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeline);
        if (!IsValid)
        {
            throw new InvalidOperationException($"the proposal was refused: {Problems[0]}");
        }

        History.TransactionScope scope = history.BeginTransaction(UndoPrefix + Plan);
        try
        {
            foreach (JsonObject command in Commands)
            {
                history.Execute(CommandJson.Parse(timeline, command));
            }
        }
        catch
        {
            scope.Cancel();
            throw;
        }

        scope.Dispose();
    }

    private static AssistantProposal Refused(string plan, string problem) => new(plan, [], [problem]);

    /// <summary>What a command can say that parses and still makes no sense.</summary>
    private static string? Sense(IEditCommand command) => command switch
    {
        SetSourceRange { Range: { } r } when r.Duration.Value <= 0 || r.Duration.Rate <= 0 => "a trim must leave a positive duration",
        AddMarker { Range: var r } when r.Duration.Value < 0 || r.Duration.Rate <= 0 => "a marker's range must not be negative",
        _ => null,
    };
}
