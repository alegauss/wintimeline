using System.Text.Json.Nodes;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>WT54: changes a marker's name, colour, range and note, as its editor does.</summary>
/// <param name="target">The marker.</param>
/// <param name="name">Its new name.</param>
/// <param name="color">Its new colour, e.g. RED.</param>
/// <param name="range">What it marks now, in its item's time.</param>
/// <param name="comment">Its new note.</param>
public sealed class EditMarker(Marker target, string name, string color, TimeRange range, string comment) : IEditCommand
{
    private (string Name, string Color, TimeRange Range, string Comment) _before;

    /// <summary>The marker.</summary>
    public Marker Target { get; } = target;

    /// <inheritdoc/>
    public string Description => $"Edit marker {name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 128 + (2L * (name.Length + comment.Length + _before.Name.Length + _before.Comment.Length));

    /// <inheritdoc/>
    public void Do()
    {
        _before = (Target.Name, Target.Color, Target.MarkedRange, Target.Comment);
        Set(name, color, range, comment);
    }

    /// <inheritdoc/>
    public void Undo() => Set(_before.Name, _before.Color, _before.Range, _before.Comment);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        Item owner = Target.Owner as Item ?? throw new InvalidOperationException($"marker {Target.Name} is on nothing");
        return new()
        {
            ["op"] = "edit_marker",
            ["target"] = Paths.Of(timeline, owner),
            ["marker"] = owner.Markers.ToList().IndexOf(Target),
            ["name"] = name,
            ["color"] = color,
            ["range"] = CommandJson.Range(range),
            ["comment"] = comment,
        };
    }

    private void Set(string newName, string newColor, TimeRange newRange, string newComment)
    {
        Target.Name = newName;
        Target.Color = newColor;
        Target.MarkedRange = newRange;
        Target.Comment = newComment;
        Target.Changed("marker");
    }
}
