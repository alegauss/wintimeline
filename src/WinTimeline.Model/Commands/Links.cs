namespace WinTimeline.Model.Commands;

/// <summary>
/// WT79: links (a picture and its sound from one source) and groups (any items) kept in each
/// item's metadata under wintimeline: keys, so they survive in OTIO and in any tool that keeps
/// metadata. A link or a group is a shared id; the items carrying it belong together.
/// </summary>
public static class ItemLinks
{
    /// <summary>The metadata key a link's id is under.</summary>
    public const string LinkKey = "wintimeline:link";

    /// <summary>The metadata key a group's id is under.</summary>
    public const string GroupKey = "wintimeline:group";

    /// <summary>The link an item carries, or null.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its link's id.</returns>
    public static string? LinkOf(Composable item) => Id(item, LinkKey);

    /// <summary>The group an item carries, or null.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its group's id.</returns>
    public static string? GroupOf(Composable item) => Id(item, GroupKey);

    /// <summary>The commands that link items to each other, under one new id.</summary>
    /// <param name="items">The items, two or more.</param>
    /// <returns>The commands.</returns>
    /// <exception cref="EditRefusedException">Fewer than two.</exception>
    public static IReadOnlyList<IEditCommand> Link(IReadOnlyList<Composable> items) => Join(items, LinkKey, "link");

    /// <summary>The commands that group items, under one new id.</summary>
    /// <param name="items">The items, two or more.</param>
    /// <returns>The commands.</returns>
    /// <exception cref="EditRefusedException">Fewer than two.</exception>
    public static IReadOnlyList<IEditCommand> Group(IReadOnlyList<Composable> items) => Join(items, GroupKey, "group");

    /// <summary>The commands that take items out of their links.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The commands; none for items in no link.</returns>
    public static IReadOnlyList<IEditCommand> Unlink(IEnumerable<Composable> items) => Leave(items, LinkKey);

    /// <summary>The commands that take items out of their groups.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The commands; none for items in no group.</returns>
    public static IReadOnlyList<IEditCommand> Ungroup(IEnumerable<Composable> items) => Leave(items, GroupKey);

    private static string? Id(Composable item, string key)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metadata.TryGetValue(key, out object? value) && value is string { Length: > 0 } id ? id : null;
    }

    private static IReadOnlyList<IEditCommand> Join(IReadOnlyList<Composable> items, string key, string what)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count < 2)
        {
            throw new EditRefusedException($"Select two clips or more to {what} them.");
        }

        string id = Guid.NewGuid().ToString("N");
        return [.. items.Select(i => (IEditCommand)new SetMetadata(i, key, id))];
    }

    private static IReadOnlyList<IEditCommand> Leave(IEnumerable<Composable> items, string key)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items.Where(i => Id(i, key) is not null).Select(i => (IEditCommand)new SetMetadata(i, key, null))];
    }
}
