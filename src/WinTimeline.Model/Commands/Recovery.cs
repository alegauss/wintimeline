using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Commands;

/// <summary>A session that did not close cleanly and left edits behind.</summary>
/// <param name="Id">The session's id.</param>
/// <param name="Name">The document's file name, for the offer.</param>
/// <param name="SourcePath">Where the document was opened from, or null for one never saved.</param>
/// <param name="Edits">How many commands ran since the last save.</param>
/// <param name="LastWrite">When the journal was last written.</param>
public sealed record RecoveryCandidate(string Id, string Name, string? SourcePath, int Edits, DateTimeOffset LastWrite);

/// <summary>
/// A recovered document: as it was when the session ended, with each of its timelines' undo
/// histories replayed (WT161: a collection's every reel, not only one timeline).
/// </summary>
public sealed class RecoveredDocument
{
    /// <summary>A recovered document of several timelines.</summary>
    /// <param name="document">The document: a timeline, or a collection of them.</param>
    /// <param name="histories">Each timeline's history, replayed.</param>
    /// <param name="fromAutosave">Whether the journal could not be replayed and the last autosave was used instead.</param>
    public RecoveredDocument(ModelObject document, IReadOnlyDictionary<Timeline, History> histories, bool fromAutosave)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(histories);
        Document = document;
        FromAutosave = fromAutosave;
        Dictionary<Timeline, History> all = new(histories, ReferenceEqualityComparer.Instance);
        foreach (Timeline timeline in DocumentTimelines.Of(document))
        {
            all.TryAdd(timeline, new History());
        }

        Histories = all;
    }

    /// <summary>A recovered document of one timeline.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="history">Its history.</param>
    /// <param name="fromAutosave">Whether the last autosave was used.</param>
    public RecoveredDocument(Timeline timeline, History history, bool fromAutosave)
        : this(timeline, new Dictionary<Timeline, History>(ReferenceEqualityComparer.Instance) { [timeline] = history }, fromAutosave)
    {
    }

    /// <summary>The document.</summary>
    public ModelObject Document { get; }

    /// <summary>Each timeline's history, every timeline of the document having one.</summary>
    public IReadOnlyDictionary<Timeline, History> Histories { get; }

    /// <summary>Whether the journal could not be replayed and the last autosave was used instead.</summary>
    public bool FromAutosave { get; }

    /// <summary>The document's first timeline.</summary>
    public Timeline Timeline => DocumentTimelines.Of(Document)[0];

    /// <summary>The first timeline's history.</summary>
    public History History => Histories[Timeline];
}

/// <summary>
/// Autosave and recovery (WT17). A session writes, beside each other in the recovery folder:
/// <c>&lt;id&gt;.json</c> (what was opened), <c>&lt;id&gt;.base.otio</c> (the document as opened),
/// and <c>&lt;id&gt;.journal.jsonl</c>, one line per thing the history did — every command as JSON
/// before it runs, every undo, redo, transaction and gesture boundary, and every save — each
/// flushed to disk as it happens. Replaying the journal on the base into a fresh history ends in
/// the same document with the same undo steps; a transaction still open when the session died is
/// dropped, so recovery stops at the last committed command. The journal is never compacted
/// during a session, because an undo after a compaction could reach a step the replay never
/// saw; <see cref="Autosave"/> writes a snapshot that is used only if the journal cannot be read.
/// </summary>
public sealed class RecoveryJournal : IDisposable
{
    private readonly ModelObject _document;
    private readonly Dictionary<History, EventHandler<HistoryEvent>> _watched = [];
    private readonly FileStream _journal;
    private long _sequence;
    private bool _disposed;

    private RecoveryJournal(string folder, string id, ModelObject document, string name, string? sourcePath)
    {
        Folder = folder;
        Id = id;
        _document = document;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".base.otio"), OtioWriter.Write(document));
        File.WriteAllText(Path.Combine(folder, id + ".json"), new JsonObject { ["name"] = name, ["source_path"] = sourcePath }.ToJsonString());
        _journal = new FileStream(Path.Combine(folder, id + ".journal.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    // A recovered session, carrying on: the same base, and the journal appended to.
    private RecoveryJournal(string folder, string id, RecoveredDocument recovered)
    {
        Folder = folder;
        Id = id;
        _document = recovered.Document;
        string journal = Path.Combine(folder, id + ".journal.jsonl");
        _sequence = ReadLines(journal).Select(TryParse).TakeWhile(n => n is JsonObject).Count();
        _journal = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.Read);
        foreach ((Timeline timeline, History history) in recovered.Histories)
        {
            Watch(timeline, history);
        }
    }

    /// <summary>
    /// WT161: journals one timeline's history, as the app makes it: a collection's reels each have
    /// their own, made when the reel is first shown.
    /// </summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="history">Its history.</param>
    public void Watch(Timeline timeline, History history)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(history);
        if (_disposed || _watched.ContainsKey(history))
        {
            return;
        }

        EventHandler<HistoryEvent> record = (_, e) => OnRecording(timeline, e);
        _watched[history] = record;
        history.Recording += record;
    }

    /// <summary>
    /// Recovers a session and carries on journaling it, so its undo steps — replayed with it —
    /// stay recoverable if this session dies too. From an autosave, a fresh session starts instead.
    /// </summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="candidate">The session to recover.</param>
    /// <param name="journal">The journal now running for it.</param>
    /// <returns>The document.</returns>
    public static RecoveredDocument Resume(string folder, RecoveryCandidate candidate, out RecoveryJournal journal)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RecoveredDocument recovered = Recover(folder, candidate.Id);
        if (recovered.FromAutosave)
        {
            Remove(folder, candidate.Id);
            journal = Start(folder, recovered.Timeline, recovered.History, candidate.Name, candidate.SourcePath);
        }
        else
        {
            journal = new RecoveryJournal(folder, candidate.Id, recovered);
        }

        return recovered;
    }

    /// <summary>The recovery folder.</summary>
    public string Folder { get; }

    /// <summary>This session's id.</summary>
    public string Id { get; }

    /// <summary>Starts journaling a document's history.</summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="timeline">The document, as opened.</param>
    /// <param name="history">Its history, empty.</param>
    /// <param name="name">The file name, for a later offer.</param>
    /// <param name="sourcePath">Where it was opened from, or null.</param>
    /// <returns>The journal.</returns>
    public static RecoveryJournal Start(string folder, Timeline timeline, History history, string name, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(history);
        RecoveryJournal journal = Start(folder, (ModelObject)timeline, name, sourcePath);
        journal.Watch(timeline, history);
        return journal;
    }

    /// <summary>WT161: starts journaling a document of any number of timelines; each one's history is added with <see cref="Watch"/>.</summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="document">The document, as opened: a timeline or a collection.</param>
    /// <param name="name">The file name, for a later offer.</param>
    /// <param name="sourcePath">Where it was opened from, or null.</param>
    /// <returns>The journal.</returns>
    public static RecoveryJournal Start(string folder, ModelObject document, string name, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(document);
        return new RecoveryJournal(folder, Guid.NewGuid().ToString("N"), document, name, sourcePath);
    }

    /// <summary>Records that the document was saved: a session that ends here has nothing to recover.</summary>
    public void MarkSaved() => Append(new JsonObject { ["kind"] = "saved" });

    /// <summary>Writes the document as it is now, the fallback when the journal cannot be replayed.</summary>
    public void Autosave()
    {
        string path = Path.Combine(Folder, Id + ".autosave.otio");
        File.WriteAllText(path + ".tmp", OtioWriter.Write(_document));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>Ends the session cleanly: everything it wrote is removed.</summary>
    public void Discard()
    {
        Dispose();
        Remove(Folder, Id);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((History history, EventHandler<HistoryEvent> record) in _watched)
        {
            history.Recording -= record;
        }

        _watched.Clear();
        _journal.Dispose();
    }

    /// <summary>The sessions in a folder that ended with edits after their last save.</summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="exceptId">A session to leave out, the one running now.</param>
    /// <returns>The candidates, newest first.</returns>
    public static IReadOnlyList<RecoveryCandidate> Pending(string folder, string? exceptId = null)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<RecoveryCandidate> found = [];
        foreach (string manifest in Directory.GetFiles(folder, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(manifest);
            string journal = Path.Combine(folder, id + ".journal.jsonl");
            if (id == exceptId || !File.Exists(journal) || IsOpenByAnotherProcess(journal))
            {
                continue;
            }

            int edits = EditsSinceSave(journal);
            if (edits == 0)
            {
                // Saved, or never edited: nothing to offer, and nothing worth keeping.
                Remove(folder, id);
                continue;
            }

            JsonNode? info = TryParse(File.ReadAllText(manifest));
            found.Add(new RecoveryCandidate(id, (string?)info?["name"] ?? id, (string?)info?["source_path"], edits, File.GetLastWriteTime(journal)));
        }

        return [.. found.OrderByDescending(c => c.LastWrite)];
    }

    /// <summary>
    /// Rebuilds a session's document: the base, then every journal line replayed into a fresh
    /// history. A line torn by the crash ends the replay; a transaction left open is cancelled.
    /// When the journal cannot be replayed at all, the last autosave is used.
    /// </summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="id">The session.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidDataException">Neither the journal nor an autosave could be read.</exception>
    public static RecoveredDocument Recover(string folder, string id)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(id);
        try
        {
            ModelObject document = OtioReader.ReadFile(Path.Combine(folder, id + ".base.otio"));
            Dictionary<Timeline, History> histories = new(ReferenceEqualityComparer.Instance);
            Replay(document, histories, Path.Combine(folder, id + ".journal.jsonl"));
            return new RecoveredDocument(document, histories, fromAutosave: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or InvalidCastException or JsonException or ArgumentOutOfRangeException)
        {
            string autosave = Path.Combine(folder, id + ".autosave.otio");
            if (File.Exists(autosave))
            {
                ModelObject saved = OtioReader.ReadFile(autosave);
                return new RecoveredDocument(saved, new Dictionary<Timeline, History>(ReferenceEqualityComparer.Instance), fromAutosave: true);
            }

            throw new InvalidDataException($"session {id} could not be recovered: {ex.Message}", ex);
        }
    }

    /// <summary>Removes what a session left.</summary>
    /// <param name="folder">The recovery folder.</param>
    /// <param name="id">The session.</param>
    public static void Remove(string folder, string id)
    {
        foreach (string suffix in (string[])[".json", ".base.otio", ".journal.jsonl", ".autosave.otio"])
        {
            try
            {
                File.Delete(Path.Combine(folder, id + suffix));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the next start to prune.
            }
        }
    }

    /// <summary>
    /// Replays a journal on its base. Each line is played on the history of the timeline it names
    /// (WT161: by its index in the document as the line was written, which the replay reproduces
    /// line by line, versions added and taken away included); a line naming none is the first's.
    /// </summary>
    private static void Replay(ModelObject document, Dictionary<Timeline, History> histories, string journal)
    {
        Stack<History.TransactionScope> transactions = new();
        Stack<IDisposable> gestures = new();
        foreach (string line in ReadLines(journal))
        {
            if (TryParse(line) is not JsonObject entry)
            {
                break; // torn by the crash: everything before it is what was written
            }

            Timeline timeline = DocumentTimelines.Of(document)[(int?)entry["timeline"] ?? 0];
            if (!histories.TryGetValue(timeline, out History? history))
            {
                histories[timeline] = history = new History();
            }

            switch ((string?)entry["kind"])
            {
                case "execute":
                    history.Execute(Command(document, timeline, entry["command"]!.AsObject()));
                    break;
                case "undo":
                    history.Undo();
                    break;
                case "redo":
                    history.Redo();
                    break;
                case "begin_transaction":
                    transactions.Push(history.BeginTransaction((string?)entry["description"] ?? string.Empty));
                    break;
                case "commit":
                    transactions.Pop().Dispose();
                    break;
                case "cancel":
                    transactions.Pop().Cancel();
                    break;
                case "begin_gesture":
                    gestures.Push(history.BeginGesture());
                    break;
                case "end_gesture":
                    gestures.Pop().Dispose();
                    break;
            }
        }

        // What was still open when the session died was never committed.
        while (transactions.Count > 0)
        {
            transactions.Pop().Cancel();
        }

        while (gestures.Count > 0)
        {
            gestures.Pop().Dispose();
        }
    }

    private static int EditsSinceSave(string journal)
    {
        int edits = 0;
        foreach (string line in ReadLines(journal))
        {
            if (TryParse(line) is not JsonObject entry)
            {
                break;
            }

            string? kind = (string?)entry["kind"];
            edits = kind == "saved" ? 0 : kind is "execute" or "undo" or "redo" ? edits + 1 : edits;
        }

        return edits;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A journal another WinTimeline is still writing is its session, not a crashed one.
    private static bool IsOpenByAnotherProcess(string journal)
    {
        try
        {
            using FileStream probe = new(journal, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void OnRecording(Timeline timeline, HistoryEvent e)
    {
        JsonObject entry = e.Kind switch
        {
            HistoryEventKind.Execute => new JsonObject { ["kind"] = "execute", ["command"] = Json(timeline, e.Command!) },
            HistoryEventKind.BeginTransaction => new JsonObject { ["kind"] = "begin_transaction", ["description"] = e.Description },
            _ => new JsonObject { ["kind"] = Snake(e.Kind) },
        };

        // WT161: which of a collection's timelines the line belongs to; a document of one needs none.
        if (_document is not Timeline)
        {
            entry["timeline"] = DocumentTimelines.Of(_document).ToList().IndexOf(timeline);
        }

        Append(entry);
    }

    /// <summary>A command's journal form: a collection edit carries the timeline it adds, every other is addressed from its timeline.</summary>
    private static JsonObject Json(Timeline timeline, IEditCommand command) => command switch
    {
        AddToCollection add => new JsonObject { ["op"] = "add_to_collection", ["index"] = add.Index, ["child"] = JsonNode.Parse(OtioWriter.Write(add.Child)) },
        RemoveFromCollection remove => new JsonObject { ["op"] = "remove_from_collection", ["index"] = remove.Index },
        _ => command.ToJson(timeline),
    };

    /// <summary>A journal line's command, a collection edit against the document's collection.</summary>
    private static IEditCommand Command(ModelObject document, Timeline timeline, JsonObject json) => (string?)json["op"] switch
    {
        "add_to_collection" => new AddToCollection(
            document as SerializableCollection ?? throw new InvalidDataException("a collection edit in the journal of a document that is no collection"),
            (int?)json["index"] ?? throw new InvalidDataException("add_to_collection needs an index"),
            OtioReader.ReadString(json["child"]?.ToJsonString() ?? throw new InvalidDataException("add_to_collection needs a child"))),
        "remove_from_collection" => new RemoveFromCollection(
            document as SerializableCollection ?? throw new InvalidDataException("a collection edit in the journal of a document that is no collection"),
            (int?)json["index"] ?? throw new InvalidDataException("remove_from_collection needs an index")),
        _ => CommandJson.Parse(timeline, json),
    };

    private void Append(JsonObject entry)
    {
        if (_disposed)
        {
            return;
        }

        entry["seq"] = ++_sequence;
        byte[] line = Encoding.UTF8.GetBytes(entry.ToJsonString() + "\n");
        _journal.Write(line);
        _journal.Flush(flushToDisk: true);
    }

    private static string Snake(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Undo => "undo",
        HistoryEventKind.Redo => "redo",
        HistoryEventKind.Commit => "commit",
        HistoryEventKind.Cancel => "cancel",
        HistoryEventKind.BeginGesture => "begin_gesture",
        HistoryEventKind.EndGesture => "end_gesture",
        _ => kind.ToString().ToLower(CultureInfo.InvariantCulture),
    };
}
