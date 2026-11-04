using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Commands;

/// <summary>The timelines a document holds, found through any depth of collections (WT20).</summary>
public static partial class DocumentTimelines
{
    /// <summary>Every timeline in a document, in file order; a document that is one timeline holds that one.</summary>
    /// <param name="root">The document's top object.</param>
    /// <returns>The timelines.</returns>
    public static IReadOnlyList<Timeline> Of(ModelObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        List<Timeline> found = [];
        Walk(root, found);
        return found;
    }

    /// <summary>
    /// The name a new version of a timeline takes: "reel1" becomes "reel1 v002", "reel1 v002"
    /// becomes "reel1 v003", skipping any number a sibling already has.
    /// </summary>
    /// <param name="name">The timeline's name.</param>
    /// <param name="taken">The names already in the collection.</param>
    /// <returns>The version's name.</returns>
    public static string NextVersion(string name, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Match m = Versioned().Match(name);
        string stem = m.Success ? m.Groups["stem"].Value : name;
        int number = m.Success ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : 1;
        HashSet<string> used = new(taken, StringComparer.Ordinal);
        string candidate;
        do
        {
            number++;
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} v{number:000}");
        }
        while (used.Contains(candidate));

        return candidate;
    }

    private static void Walk(ModelObject node, List<Timeline> found)
    {
        if (node is Timeline timeline)
        {
            found.Add(timeline);
        }
        else if (node is SerializableCollection collection)
        {
            foreach (ModelObject child in collection.Children)
            {
                Walk(child, found);
            }
        }
    }

    [GeneratedRegex(@"^(?<stem>.*) v(?<n>\d{3,})$")]
    private static partial Regex Versioned();
}

/// <summary>Puts an object into a collection at an index.</summary>
/// <param name="collection">The collection.</param>
/// <param name="index">Where.</param>
/// <param name="child">What, held by nothing yet.</param>
public sealed class AddToCollection(SerializableCollection collection, int index, ModelObject child) : IEditCommand
{
    /// <summary>The collection.</summary>
    public SerializableCollection Collection { get; } = collection;

    /// <summary>Where.</summary>
    public int Index { get; } = index;

    /// <summary>What.</summary>
    public ModelObject Child { get; } = child;

    /// <inheritdoc/>
    public string Description => $"Add {(Child as NamedObject)?.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 1024;

    /// <inheritdoc/>
    public void Do() => Collection.Insert(Index, Child);

    /// <inheritdoc/>
    public void Undo() => Collection.RemoveAt(Index);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) =>
        throw new NotSupportedException("a collection edit is not addressed from a timeline");
}

/// <summary>Takes an object out of a collection.</summary>
/// <param name="collection">The collection.</param>
/// <param name="index">Which.</param>
public sealed class RemoveFromCollection(SerializableCollection collection, int index) : IEditCommand
{
    private ModelObject? _removed;

    /// <summary>The collection.</summary>
    public SerializableCollection Collection { get; } = collection;

    /// <summary>Which.</summary>
    public int Index { get; } = index;

    /// <inheritdoc/>
    public string Description => $"Remove {((_removed ?? Collection.Children.ElementAtOrDefault(Index)) as NamedObject)?.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 1024;

    /// <inheritdoc/>
    public void Do()
    {
        _removed = Collection.Children[Index];
        Collection.RemoveAt(Index);
    }

    /// <inheritdoc/>
    public void Undo() => Collection.Insert(Index, _removed!);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) =>
        throw new NotSupportedException("a collection edit is not addressed from a timeline");
}

/// <summary>Copies a timeline as its next version, right after it in its collection.</summary>
public static class DuplicateTimeline
{
    /// <summary>The command that adds the copy: a deep copy named as the next version.</summary>
    /// <param name="timeline">A timeline held by a collection.</param>
    /// <returns>The command, not yet run.</returns>
    /// <exception cref="InvalidOperationException">The timeline is not in a collection.</exception>
    public static AddToCollection Command(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.Owner is not SerializableCollection collection)
        {
            throw new InvalidOperationException($"{timeline.Name} is not in a collection; versions live beside it in one.");
        }

        Timeline copy = (Timeline)OtioReader.ReadString(OtioWriter.Write(timeline));
        copy.Name = DocumentTimelines.NextVersion(timeline.Name, collection.Children.OfType<NamedObject>().Select(n => n.Name));
        int index = collection.Children.ToList().IndexOf(timeline) + 1;
        return new AddToCollection(collection, index, copy);
    }
}
