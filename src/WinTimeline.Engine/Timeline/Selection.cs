namespace WinTimeline.Engine.Timeline;

/// <summary>WT52: how a click on an item changes the selection.</summary>
public enum SelectGesture
{
    /// <summary>A plain click: the item alone.</summary>
    Replace,

    /// <summary>Ctrl+click: the item in or out, the rest kept.</summary>
    Toggle,

    /// <summary>Shift+click: everything from the last item clicked to this one, across the lanes between.</summary>
    Extend,
}

/// <summary>
/// WT52: the selected items of a shown timeline, by the ids UI Automation names them with
/// ("V1-0": the lane's label and the item's index in its track). Gaps are never selected.
/// Every change raises <see cref="Changed"/> once.
/// </summary>
public sealed class Selection
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private string? _anchor;

    /// <summary>Raised after the selection changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The selected ids.</summary>
    public IReadOnlySet<string> Ids => _ids;

    /// <summary>Whether an id is selected.</summary>
    /// <param name="id">The id.</param>
    /// <returns>Whether it is.</returns>
    public bool Contains(string id) => _ids.Contains(id);

    /// <summary>A click on an item.</summary>
    /// <param name="layout">The layout it is on.</param>
    /// <param name="item">The item.</param>
    /// <param name="gesture">Plain, Ctrl or Shift.</param>
    public void Click(TimelineLayout layout, LaneItem item, SelectGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind == LaneItemKind.Gap)
        {
            return;
        }

        string id = layout.IdOf(item);
        switch (gesture)
        {
            case SelectGesture.Toggle:
                if (!_ids.Remove(id))
                {
                    _ids.Add(id);
                }

                _anchor = id;
                break;
            case SelectGesture.Extend when _anchor is not null && layout.Find(_anchor) is { } anchor:
                double from = Math.Min(anchor.Start, item.Start), to = Math.Max(anchor.Start, item.Start);
                int top = Math.Min(anchor.Lane, item.Lane), bottom = Math.Max(anchor.Lane, item.Lane);
                _ids.UnionWith(Selectable(layout).Where(i => i.Lane >= top && i.Lane <= bottom && i.Start >= from && i.Start <= to).Select(layout.IdOf));
                break;
            default:
                _ids.Clear();
                _ids.Add(id);
                _anchor = id;
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A marquee drawn over empty space: every item it touches.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="from">Its first second.</param>
    /// <param name="to">Its last.</param>
    /// <param name="firstLane">The first lane it covers.</param>
    /// <param name="lastLane">The last.</param>
    /// <param name="add">Whether it adds to the selection (Ctrl or Shift held) rather than replacing it.</param>
    public void Marquee(TimelineLayout layout, double from, double to, int firstLane, int lastLane, bool add)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Set(layout.Visible(Math.Min(from, to), Math.Max(from, to), Math.Min(firstLane, lastLane), Math.Max(firstLane, lastLane)).Where(i => i.Kind != LaneItemKind.Gap), layout, add);
    }

    /// <summary>A or Shift+A: every item on every lane from the playhead on, or up to it.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="seconds">Where the playhead is.</param>
    /// <param name="forward">Forward (A) or backward (Shift+A).</param>
    public void TrackSelect(TimelineLayout layout, double seconds, bool forward)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Set(Selectable(layout).Where(i => forward ? i.End > seconds : i.Start < seconds), layout, add: false);
    }

    /// <summary>Ctrl+A: everything.</summary>
    /// <param name="layout">The layout.</param>
    public void All(TimelineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Set(Selectable(layout), layout, add: false);
    }

    /// <summary>Nothing selected.</summary>
    public void Clear()
    {
        _anchor = null;
        if (_ids.Count > 0)
        {
            _ids.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>After an edit: forgets ids the timeline no longer has.</summary>
    /// <param name="layout">The timeline's new layout.</param>
    public void Keep(TimelineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_ids.RemoveWhere(id => layout.Find(id) is null) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IEnumerable<LaneItem> Selectable(TimelineLayout layout) =>
        layout.Lanes.SelectMany(l => l.Items).Where(i => i.Kind != LaneItemKind.Gap);

    private void Set(IEnumerable<LaneItem> items, TimelineLayout layout, bool add)
    {
        if (!add)
        {
            _ids.Clear();
        }

        _ids.UnionWith(items.Select(layout.IdOf));
        _anchor = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
