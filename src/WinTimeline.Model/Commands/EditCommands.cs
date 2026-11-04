using System.Globalization;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>
/// One change to a document (WT16). Every edit — a tool's, the AI assistant's, a script's — is
/// one of these, so undo sees all of them and none can change the document behind its back.
/// A command keeps only what it needs to put things back (a diff, not a snapshot).
/// </summary>
public interface IEditCommand
{
    /// <summary>What the history panel calls it, e.g. "Trim A001".</summary>
    string Description { get; }

    /// <summary>A rough size of what it retains, for the history's memory budget.</summary>
    long EstimatedBytes { get; }

    /// <summary>Applies it.</summary>
    void Do();

    /// <summary>Puts back what <see cref="Do"/> changed.</summary>
    void Undo();

    /// <summary>
    /// Folds a command that follows into this one, as a drag's steps fold into one undo step; true
    /// when it did, and then this command already includes the next one's effect.
    /// </summary>
    /// <param name="next">The command that just ran after this one.</param>
    /// <returns>Whether it was folded in.</returns>
    bool TryMerge(IEditCommand next);

    /// <summary>The command as JSON, addressed by paths from the timeline, which is what a macro records and the assistant emits.</summary>
    /// <param name="timeline">The timeline its targets are in.</param>
    /// <returns>The JSON.</returns>
    JsonObject ToJson(Timeline timeline);
}

/// <summary>Sets or clears an item's source range: a trim, a slip. Consecutive ones on one item fold together.</summary>
/// <param name="target">The item.</param>
/// <param name="range">The new range, or null for all of what is available.</param>
public sealed class SetSourceRange(Item target, TimeRange? range) : IEditCommand
{
    private TimeRange? _before;

    /// <summary>The item.</summary>
    public Item Target { get; } = target;

    /// <summary>The range it gets.</summary>
    public TimeRange? Range { get; private set; } = range;

    /// <inheritdoc/>
    public string Description => $"Trim {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 96;

    /// <inheritdoc/>
    public void Do()
    {
        _before = Target.SourceRange;
        Target.SetSourceRange(Range);
    }

    /// <inheritdoc/>
    public void Undo() => Target.SetSourceRange(_before);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next)
    {
        if (next is not SetSourceRange other || !ReferenceEquals(other.Target, Target))
        {
            return false;
        }

        Range = other.Range;
        return true;
    }

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "set_source_range",
        ["target"] = Paths.Of(timeline, Target),
        ["range"] = CommandJson.Range(Range),
    };
}

/// <summary>Puts a composable into a composition at an index.</summary>
/// <param name="parent">The composition.</param>
/// <param name="index">Where, 0 to its child count.</param>
/// <param name="child">What, held by nothing yet.</param>
public sealed class InsertChild(Composition parent, int index, Composable child) : IEditCommand
{
    /// <summary>The composition.</summary>
    public Composition Parent { get; } = parent;

    /// <summary>Where.</summary>
    public int Index { get; } = index;

    /// <summary>What.</summary>
    public Composable Child { get; } = child;

    /// <inheritdoc/>
    public string Description => $"Insert {Child.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 256;

    /// <inheritdoc/>
    public void Do()
    {
        if (Child.Owner is not null)
        {
            throw new InvalidOperationException($"{Child.Name} is already held; move it instead.");
        }

        Parent.Insert(Index, Child);
    }

    /// <inheritdoc/>
    public void Undo() => Parent.RemoveAt(Index);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "insert_child",
        ["target"] = Paths.Of(timeline, Parent),
        ["index"] = Index,
        ["child"] = JsonNode.Parse(OtioWriter.Write(Child)),
    };
}

/// <summary>Takes a child out of a composition.</summary>
/// <param name="parent">The composition.</param>
/// <param name="index">Which child.</param>
public sealed class RemoveChild(Composition parent, int index) : IEditCommand
{
    private Composable? _removed;

    /// <summary>The composition.</summary>
    public Composition Parent { get; } = parent;

    /// <summary>Which child.</summary>
    public int Index { get; } = index;

    /// <inheritdoc/>
    public string Description => $"Remove {(_removed ?? Parent.Children.ElementAtOrDefault(Index))?.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 256;

    /// <inheritdoc/>
    public void Do()
    {
        _removed = Parent.Children[Index];
        Parent.RemoveAt(Index);
    }

    /// <inheritdoc/>
    public void Undo() => Parent.Insert(Index, _removed!);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "remove_child",
        ["target"] = Paths.Of(timeline, Parent),
        ["index"] = Index,
    };
}

/// <summary>Moves a child within a composition or to another; consecutive moves of one child fold together.</summary>
/// <param name="from">Where it is.</param>
/// <param name="fromIndex">Its index there.</param>
/// <param name="to">Where it goes.</param>
/// <param name="toIndex">Its index there, counted after it has left <paramref name="from"/>.</param>
public sealed class MoveChild(Composition from, int fromIndex, Composition to, int toIndex) : IEditCommand
{
    private Composable? _moved;

    /// <summary>Where it is.</summary>
    public Composition From { get; } = from;

    /// <summary>Its index there.</summary>
    public int FromIndex { get; } = fromIndex;

    /// <summary>Where it goes.</summary>
    public Composition To { get; private set; } = to;

    /// <summary>Its index there.</summary>
    public int ToIndex { get; private set; } = toIndex;

    /// <inheritdoc/>
    public string Description => $"Move {(_moved ?? From.Children.ElementAtOrDefault(FromIndex))?.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 64;

    /// <inheritdoc/>
    public void Do()
    {
        _moved = From.Children[FromIndex];
        From.RemoveAt(FromIndex);
        To.Insert(ToIndex, _moved);
    }

    /// <inheritdoc/>
    public void Undo()
    {
        To.RemoveAt(ToIndex);
        From.Insert(FromIndex, _moved!);
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next)
    {
        if (next is not MoveChild other || !ReferenceEquals(other.From, To) || other.FromIndex != ToIndex)
        {
            return false;
        }

        To = other.To;
        ToIndex = other.ToIndex;
        return true;
    }

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "move_child",
        ["from"] = Paths.Of(timeline, From),
        ["from_index"] = FromIndex,
        ["to"] = Paths.Of(timeline, To),
        ["to_index"] = ToIndex,
    };
}

/// <summary>Renames a composable.</summary>
/// <param name="target">What.</param>
/// <param name="name">Its new name.</param>
public sealed class Rename(Composable target, string name) : IEditCommand
{
    private string _before = string.Empty;

    /// <summary>What.</summary>
    public Composable Target { get; } = target;

    /// <summary>Its new name.</summary>
    public string Name { get; private set; } = name;

    /// <inheritdoc/>
    public string Description => $"Rename to {Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 64 + (2L * (Name.Length + _before.Length));

    /// <inheritdoc/>
    public void Do()
    {
        _before = Target.Name;
        Target.Name = Name;
        Target.Changed("name");
    }

    /// <inheritdoc/>
    public void Undo()
    {
        Target.Name = _before;
        Target.Changed("name");
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next)
    {
        if (next is not Rename other || !ReferenceEquals(other.Target, Target))
        {
            return false;
        }

        Name = other.Name;
        return true;
    }

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new() { ["op"] = "rename", ["target"] = Paths.Of(timeline, Target), ["name"] = Name };
}

/// <summary>Turns an item on or off.</summary>
/// <param name="target">The item.</param>
/// <param name="enabled">Whether it plays.</param>
public sealed class SetEnabled(Item target, bool enabled) : IEditCommand
{
    private bool _before;

    /// <summary>The item.</summary>
    public Item Target { get; } = target;

    /// <summary>Whether it plays.</summary>
    public bool Enabled { get; } = enabled;

    /// <inheritdoc/>
    public string Description => $"{(Enabled ? "Enable" : "Disable")} {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 48;

    /// <inheritdoc/>
    public void Do()
    {
        _before = Target.Enabled;
        Target.Enabled = Enabled;
        Target.Changed("enabled");
    }

    /// <inheritdoc/>
    public void Undo()
    {
        Target.Enabled = _before;
        Target.Changed("enabled");
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new() { ["op"] = "set_enabled", ["target"] = Paths.Of(timeline, Target), ["enabled"] = Enabled };
}

/// <summary>Sets one metadata key on a composable, or removes it with a null value. Values are plain JSON values.</summary>
/// <param name="target">What.</param>
/// <param name="key">The key.</param>
/// <param name="value">A string, a number, a bool, or null to remove the key.</param>
public sealed class SetMetadata(Composable target, string key, object? value) : IEditCommand
{
    private bool _had;
    private object? _before;

    /// <summary>What.</summary>
    public Composable Target { get; } = target;

    /// <summary>The key.</summary>
    public string Key { get; } = key;

    /// <summary>The value, or null to remove the key.</summary>
    public object? Value { get; private set; } = value;

    /// <inheritdoc/>
    public string Description => $"Set {Key} on {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 128;

    /// <inheritdoc/>
    public void Do()
    {
        _had = Target.Metadata.TryGetValue(Key, out _before);
        if (Value is null)
        {
            Target.Metadata.Remove(Key);
        }
        else
        {
            Target.Metadata.Set(Key, Value);
        }

        Target.Changed("metadata");
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (_had)
        {
            Target.Metadata.Set(Key, _before);
        }
        else
        {
            Target.Metadata.Remove(Key);
        }

        Target.Changed("metadata");
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next)
    {
        if (next is not SetMetadata other || !ReferenceEquals(other.Target, Target) || other.Key != Key)
        {
            return false;
        }

        Value = other.Value;
        return true;
    }

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "set_metadata",
        ["target"] = Paths.Of(timeline, Target),
        ["key"] = Key,
        ["value"] = Value switch
        {
            null => null,
            string s => s,
            bool b => b,
            long l => l,
            double d => d,
            _ => throw new NotSupportedException($"metadata of type {Value.GetType()} has no command form"),
        },
    };
}

/// <summary>Sets a transition's offsets into its neighbours.</summary>
/// <param name="target">The transition.</param>
/// <param name="inOffset">Into the item before.</param>
/// <param name="outOffset">Into the item after.</param>
public sealed class SetTransitionOffsets(Transition target, RationalTime inOffset, RationalTime outOffset) : IEditCommand
{
    private (RationalTime In, RationalTime Out) _before;

    /// <summary>The transition.</summary>
    public Transition Target { get; } = target;

    /// <summary>Into the item before.</summary>
    public RationalTime InOffset { get; private set; } = inOffset;

    /// <summary>Into the item after.</summary>
    public RationalTime OutOffset { get; private set; } = outOffset;

    /// <inheritdoc/>
    public string Description => $"Change {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 96;

    /// <inheritdoc/>
    public void Do()
    {
        _before = (Target.InOffset, Target.OutOffset);
        (Target.InOffset, Target.OutOffset) = (InOffset, OutOffset);
        Target.Changed("offsets");
    }

    /// <inheritdoc/>
    public void Undo()
    {
        (Target.InOffset, Target.OutOffset) = _before;
        Target.Changed("offsets");
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next)
    {
        if (next is not SetTransitionOffsets other || !ReferenceEquals(other.Target, Target))
        {
            return false;
        }

        (InOffset, OutOffset) = (other.InOffset, other.OutOffset);
        return true;
    }

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "set_transition_offsets",
        ["target"] = Paths.Of(timeline, Target),
        ["in_offset"] = CommandJson.Time(InOffset),
        ["out_offset"] = CommandJson.Time(OutOffset),
    };
}

/// <summary>Adds a marker to an item.</summary>
/// <param name="target">The item.</param>
/// <param name="name">The marker's name.</param>
/// <param name="range">What it marks, in the item's time.</param>
/// <param name="color">RED, GREEN, ...</param>
public sealed class AddMarker(Item target, string name, TimeRange range, string color = MarkerColor.Red) : IEditCommand
{
    /// <summary>The item.</summary>
    public Item Target { get; } = target;

    /// <summary>The marker's name.</summary>
    public string Name { get; } = name;

    /// <summary>What it marks.</summary>
    public TimeRange Range { get; } = range;

    /// <summary>Its colour.</summary>
    public string Color { get; } = color;

    /// <inheritdoc/>
    public string Description => $"Add marker {Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 192;

    /// <inheritdoc/>
    public void Do()
    {
        Metadata fields = new();
        fields.Set("name", Name);
        fields.Set("marked_range", Range);
        fields.Set("color", Color);
        Target.AddMarker(new Marker(fields));
    }

    /// <inheritdoc/>
    public void Undo() => Target.RemoveMarkerAt(Target.Markers.Count - 1);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "add_marker",
        ["target"] = Paths.Of(timeline, Target),
        ["name"] = Name,
        ["range"] = CommandJson.Range(Range),
        ["color"] = Color,
    };
}

/// <summary>Several commands as one undo step: a ripple trim is a transaction of moves and trims.</summary>
public sealed class Transaction : IEditCommand
{
    private readonly List<IEditCommand> _commands = [];

    /// <summary>Creates an empty transaction.</summary>
    /// <param name="description">What the history calls it.</param>
    public Transaction(string description) => Description = description;

    /// <inheritdoc/>
    public string Description { get; }

    /// <summary>The commands, in the order they ran.</summary>
    public IReadOnlyList<IEditCommand> Commands => _commands;

    /// <inheritdoc/>
    public long EstimatedBytes => 32 + _commands.Sum(c => c.EstimatedBytes);

    /// <inheritdoc/>
    public void Do()
    {
        foreach (IEditCommand command in _commands)
        {
            command.Do();
        }
    }

    /// <inheritdoc/>
    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => throw new NotSupportedException("a transaction is recorded as its commands, each with the paths it saw when it ran");

    /// <summary>Adds a command that has already run, folding it into the last when it can.</summary>
    internal void Add(IEditCommand command)
    {
        if (_commands.Count > 0 && _commands[^1].TryMerge(command))
        {
            return;
        }

        _commands.Add(command);
    }

    /// <summary>Undoes the commands that ran, for a transaction abandoned midway.</summary>
    internal void Rollback() => Undo();
}

/// <summary>Paths from a timeline to the composables in it: child indices from its tracks stack, e.g. "0/3".</summary>
public static class Paths
{
    /// <summary>A composable's path; "" for the tracks stack itself.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="target">Something in it.</param>
    /// <returns>The path.</returns>
    /// <exception cref="ArgumentException">The target is not in the timeline.</exception>
    public static string Of(Timeline timeline, Composable target)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(target);
        List<int> indices = [];
        Composable at = target;
        while (!ReferenceEquals(at, timeline.Tracks))
        {
            Composition parent = at.Parent ?? throw new ArgumentException($"{target.Name} is not in {timeline.Name}", nameof(target));
            indices.Add(parent.Children.ToList().IndexOf(at));
            at = parent;
        }

        indices.Reverse();
        return string.Join('/', indices.Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The composable at a path.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="path">A path from <see cref="Of"/>.</param>
    /// <returns>The composable.</returns>
    /// <exception cref="ArgumentException">The path names nothing.</exception>
    public static Composable Resolve(Timeline timeline, string path)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(path);
        Composable at = timeline.Tracks;
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (at is not Composition composition || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= composition.Children.Count)
            {
                throw new ArgumentException($"'{path}' names nothing in {timeline.Name}", nameof(path));
            }

            at = composition.Children[index];
        }

        return at;
    }
}
