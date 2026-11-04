using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>
/// The undo history of one document (WT16). Every edit runs through <see cref="Execute"/>; a
/// <see cref="BeginTransaction"/> scope makes one user gesture one undo step, and inside a
/// <see cref="BeginGesture"/> scope (a drag) commands on the same thing fold into one entry as
/// they arrive. What the history retains is bounded by a byte budget: past it, the oldest steps
/// are forgotten, never the newest.
/// </summary>
public sealed class History
{
    private readonly LinkedList<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private Transaction? _open;
    private int _gestures;
    private long _retained;

    /// <summary>A history with a budget.</summary>
    /// <param name="budgetBytes">How much the undo steps may retain; 512 MB unless given.</param>
    public History(long budgetBytes = 512L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budgetBytes, 1);
        BudgetBytes = budgetBytes;
    }

    /// <summary>Raised after anything is done, undone or redone.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// WT17: raised for everything that changes what the history holds, a command before it runs,
    /// so a journal replaying these events into a fresh history ends in the same document and the
    /// same undo steps.
    /// </summary>
    public event EventHandler<HistoryEvent>? Recording;

    /// <summary>How much the undo steps may retain.</summary>
    public long BudgetBytes { get; }

    /// <summary>What the undo steps retain now.</summary>
    public long RetainedBytes => _retained;

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0 && _open is null;

    /// <summary>Whether there is a step to redo.</summary>
    public bool CanRedo => _redo.Count > 0 && _open is null;

    /// <summary>The undo steps, oldest first, for the history panel.</summary>
    public IEnumerable<IEditCommand> UndoSteps => _undo;

    /// <summary>What Undo would undo, or null.</summary>
    public string? UndoDescription => CanUndo ? _undo.Last!.Value.Description : null;

    /// <summary>What Redo would redo, or null.</summary>
    public string? RedoDescription => CanRedo ? _redo.Peek().Description : null;

    /// <summary>Runs a command and records it. A new edit forgets whatever could have been redone.</summary>
    /// <param name="command">The command.</param>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Recording?.Invoke(this, new HistoryEvent(HistoryEventKind.Execute, command));
        command.Do();
        if (_open is { } transaction)
        {
            // Redo is forgotten when the transaction commits: a cancelled one leaves history as it was.
            transaction.Add(command);
            return;
        }

        _redo.Clear();
        if (_gestures > 0 && _undo.Last is { } last && last.Value.TryMerge(command))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Push(command);
    }

    /// <summary>Undoes the last step.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        Recording?.Invoke(this, new HistoryEvent(HistoryEventKind.Undo));
        IEditCommand command = _undo.Last!.Value;
        _undo.RemoveLast();
        _retained -= command.EstimatedBytes;
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Redoes the last undone step.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        Recording?.Invoke(this, new HistoryEvent(HistoryEventKind.Redo));
        IEditCommand command = _redo.Pop();
        command.Do();
        Push(command);
        return true;
    }

    /// <summary>
    /// Groups the commands executed until the scope is disposed into one undo step. Dispose
    /// commits it; <see cref="TransactionScope.Cancel"/> undoes what ran instead. Nested scopes
    /// join the outermost.
    /// </summary>
    /// <param name="description">What the history calls the step.</param>
    /// <returns>The scope.</returns>
    public TransactionScope BeginTransaction(string description)
    {
        if (_open is not null)
        {
            return new TransactionScope(this, null);
        }

        Recording?.Invoke(this, new HistoryEvent(HistoryEventKind.BeginTransaction, Description: description));
        _open = new Transaction(description);
        return new TransactionScope(this, _open);
    }

    /// <summary>A drag: until disposed, a command that can fold into the last step does, so the whole drag is one step.</summary>
    /// <returns>The scope.</returns>
    public IDisposable BeginGesture()
    {
        Recording?.Invoke(this, new HistoryEvent(HistoryEventKind.BeginGesture));
        _gestures++;
        return new Gesture(this);
    }

    private void Push(IEditCommand command)
    {
        _undo.AddLast(command);
        _retained += command.EstimatedBytes;
        while (_retained > BudgetBytes && _undo.Count > 1)
        {
            _retained -= _undo.First!.Value.EstimatedBytes;
            _undo.RemoveFirst();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Close(Transaction transaction, bool commit)
    {
        _open = null;
        Recording?.Invoke(this, new HistoryEvent(commit ? HistoryEventKind.Commit : HistoryEventKind.Cancel));
        if (!commit)
        {
            transaction.Rollback();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (transaction.Commands.Count > 0)
        {
            _redo.Clear();
            Push(transaction);
        }
    }

    /// <summary>A transaction in progress.</summary>
    public sealed class TransactionScope : IDisposable
    {
        private readonly History _history;
        private Transaction? _transaction;

        internal TransactionScope(History history, Transaction? transaction)
        {
            _history = history;
            _transaction = transaction;
        }

        /// <summary>Undoes what ran in the transaction and records nothing.</summary>
        public void Cancel()
        {
            if (_transaction is { } t)
            {
                _transaction = null;
                _history.Close(t, commit: false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_transaction is { } t)
            {
                _transaction = null;
                _history.Close(t, commit: true);
            }
        }
    }

    private sealed class Gesture(History history) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                history._gestures--;
                history.Recording?.Invoke(history, new HistoryEvent(HistoryEventKind.EndGesture));
            }
        }
    }
}

/// <summary>What changed in a history, for a journal.</summary>
public enum HistoryEventKind
{
    /// <summary>A command is about to run.</summary>
    Execute,

    /// <summary>The last step was undone.</summary>
    Undo,

    /// <summary>The last undone step was redone.</summary>
    Redo,

    /// <summary>A transaction began.</summary>
    BeginTransaction,

    /// <summary>The open transaction was committed as one step.</summary>
    Commit,

    /// <summary>The open transaction was undone and dropped.</summary>
    Cancel,

    /// <summary>A gesture (a drag) began.</summary>
    BeginGesture,

    /// <summary>The gesture ended.</summary>
    EndGesture,
}

/// <summary>One thing a history did.</summary>
/// <param name="Kind">What.</param>
/// <param name="Command">The command, for <see cref="HistoryEventKind.Execute"/>.</param>
/// <param name="Description">The transaction's name, for <see cref="HistoryEventKind.BeginTransaction"/>.</param>
public sealed record HistoryEvent(HistoryEventKind Kind, IEditCommand? Command = null, string? Description = null);

/// <summary>The JSON form of commands: what a macro records, the AI assistant emits and a script sends.</summary>
public static class CommandJson
{
    /// <summary>A time as [value, rate].</summary>
    /// <param name="time">The time.</param>
    /// <returns>The JSON.</returns>
    public static JsonArray Time(RationalTime time) => [time.Value, time.Rate];

    /// <summary>A range as [[start value, rate], [duration value, rate]], or null.</summary>
    /// <param name="range">The range.</param>
    /// <returns>The JSON.</returns>
    public static JsonArray? Range(TimeRange? range) => range is { } r ? [Time(r.StartTime), Time(r.Duration)] : null;

    /// <summary>A command from its JSON form, addressed against a timeline.</summary>
    /// <param name="timeline">The timeline the paths are in.</param>
    /// <param name="json">The command, as <see cref="IEditCommand.ToJson"/> wrote it.</param>
    /// <returns>The command, not yet run.</returns>
    /// <exception cref="ArgumentException">The op is unknown or a path names nothing.</exception>
    public static IEditCommand Parse(Timeline timeline, JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(json);
        string op = (string?)json["op"] ?? throw new ArgumentException("a command needs an op", nameof(json));
        return op switch
        {
            "set_source_range" => new SetSourceRange(Item(timeline, json["target"]), ParseRange(json["range"])),
            "insert_child" => new InsertChild(Composition(timeline, json["target"]), Int(json["index"]), Child(json["child"])),
            "remove_child" => new RemoveChild(Composition(timeline, json["target"]), Int(json["index"])),
            "move_child" => new MoveChild(Composition(timeline, json["from"]), Int(json["from_index"]), Composition(timeline, json["to"]), Int(json["to_index"])),
            "rename" => new Rename(Paths.Resolve(timeline, Path(json["target"])), (string?)json["name"] ?? string.Empty),
            "set_enabled" => new SetEnabled(Item(timeline, json["target"]), (bool?)json["enabled"] ?? true),
            "set_metadata" => new SetMetadata(Paths.Resolve(timeline, Path(json["target"])), (string?)json["key"] ?? throw new ArgumentException("set_metadata needs a key", nameof(json)), Scalar(json["value"])),
            "set_transition_offsets" => new SetTransitionOffsets(
                Paths.Resolve(timeline, Path(json["target"])) as Transition ?? throw new ArgumentException("the target is not a transition", nameof(json)),
                ParseTime(json["in_offset"]),
                ParseTime(json["out_offset"])),
            "relink" => new Relink(
                Paths.Resolve(timeline, Path(json["target"])) as Clip ?? throw new ArgumentException("the target is not a clip", nameof(json)),
                (string?)json["file"] ?? throw new ArgumentException("relink needs a file", nameof(json))),
            "add_marker" => new AddMarker(Item(timeline, json["target"]), (string?)json["name"] ?? string.Empty, ParseRange(json["range"]) ?? new TimeRange(), (string?)json["color"] ?? MarkerColor.Red),
            _ => throw new ArgumentException($"unknown op '{op}'", nameof(json)),
        };
    }

    private static string Path(JsonNode? node) => (string?)node ?? string.Empty;

    private static int Int(JsonNode? node) => (int?)node ?? throw new ArgumentException("an index is missing");

    private static Item Item(Timeline timeline, JsonNode? path) =>
        Paths.Resolve(timeline, Path(path)) as Item ?? throw new ArgumentException($"'{path}' is not an item");

    private static Composition Composition(Timeline timeline, JsonNode? path) =>
        Paths.Resolve(timeline, Path(path)) as Composition ?? throw new ArgumentException($"'{path}' is not a composition");

    private static Composable Child(JsonNode? node) =>
        OtioReader.ReadString(node?.ToJsonString() ?? "null") as Composable ?? throw new ArgumentException("the child is not a composable");

    private static RationalTime ParseTime(JsonNode? node) =>
        node is JsonArray a ? new RationalTime((double)a[0]!, (double)a[1]!) : throw new ArgumentException("a time is [value, rate]");

    private static TimeRange? ParseRange(JsonNode? node) =>
        node is JsonArray a ? new TimeRange(ParseTime(a[0]), ParseTime(a[1])) : null;

    private static object? Scalar(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out long l) && !v.ToJsonString().Contains('.', StringComparison.Ordinal) => l,
        JsonValue v when v.TryGetValue(out double d) => d,
        _ => throw new ArgumentException($"metadata value {node.ToJsonString()} is not a scalar"),
    };
}
