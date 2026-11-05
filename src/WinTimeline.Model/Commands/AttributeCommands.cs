using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Commands;

/// <summary>WT78: adds an effect to the end of an item's list, as a paste of attributes does.</summary>
/// <param name="target">The item.</param>
/// <param name="effect">The effect, held by nothing.</param>
public sealed class AddEffect(Item target, Effect effect) : IEditCommand
{
    /// <summary>The item.</summary>
    public Item Target { get; } = target;

    /// <summary>The effect.</summary>
    public Effect Effect { get; } = effect;

    /// <inheritdoc/>
    public string Description => $"Add {Effect.EffectName} to {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 256;

    /// <inheritdoc/>
    public void Do() => Target.AddEffect(Effect);

    /// <inheritdoc/>
    public void Undo() => Target.RemoveEffectAt(Target.Effects.Count - 1);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "add_effect",
        ["target"] = Paths.Of(timeline, Target),
        ["effect"] = JsonNode.Parse(OtioWriter.Write(Effect)),
    };
}

/// <summary>WT78: sets or clears an item's colour in the timeline.</summary>
/// <param name="target">The item.</param>
/// <param name="color">The colour, or null for none.</param>
public sealed class SetColor(Item target, Color? color) : IEditCommand
{
    private Color? _before;

    /// <summary>The item.</summary>
    public Item Target { get; } = target;

    /// <summary>The colour.</summary>
    public Color? Color { get; } = color;

    /// <inheritdoc/>
    public string Description => $"Colour {Target.Name}";

    /// <inheritdoc/>
    public long EstimatedBytes => 96;

    /// <inheritdoc/>
    public void Do()
    {
        _before = Target.Color;
        Target.SetColor(Color);
    }

    /// <inheritdoc/>
    public void Undo() => Target.SetColor(_before);

    /// <inheritdoc/>
    public bool TryMerge(IEditCommand next) => false;

    /// <inheritdoc/>
    public JsonObject ToJson(Timeline timeline) => new()
    {
        ["op"] = "set_color",
        ["target"] = Paths.Of(timeline, Target),
        ["color"] = Color is { } c ? new JsonObject { ["r"] = c.R, ["g"] = c.G, ["b"] = c.B, ["a"] = c.A, ["name"] = c.Name } : null,
    };
}

/// <summary>WT78: what a paste of attributes carries from the copied clip.</summary>
[Flags]
public enum ClipAttributes
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Its effects, speed (a time warp) among them, added after the target's own.</summary>
    Effects = 1,

    /// <summary>Its markers, at the same offset into the target's media as into its own.</summary>
    Markers = 2,

    /// <summary>Its colour in the timeline.</summary>
    Color = 4,

    /// <summary>Its metadata's plain values: text, numbers and switches.</summary>
    Metadata = 8,
}

/// <summary>WT78: a copied clip's chosen attributes put onto other items, as Ctrl+Alt+V does.</summary>
public static class PasteAttributes
{
    /// <summary>Puts the chosen attributes of one item onto others, through a history.</summary>
    /// <param name="history">Where the commands run, inside the caller's transaction.</param>
    /// <param name="from">The item copied.</param>
    /// <param name="onto">The items pasted onto.</param>
    /// <param name="what">Which attributes.</param>
    /// <returns>How many items changed.</returns>
    public static int Apply(History history, Item from, IEnumerable<Item> onto, ClipAttributes what)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(onto);
        int changed = 0;
        foreach (Item target in onto.Where(t => !ReferenceEquals(t, from)))
        {
            bool any = false;
            if (what.HasFlag(ClipAttributes.Effects))
            {
                foreach (Effect effect in from.Effects)
                {
                    history.Execute(new AddEffect(target, (Effect)OtioReader.ReadString(OtioWriter.Write(effect))));
                    any = true;
                }
            }

            if (what.HasFlag(ClipAttributes.Markers))
            {
                any |= Markers(history, from, target);
            }

            if (what.HasFlag(ClipAttributes.Color) && from.Color != target.Color)
            {
                history.Execute(new SetColor(target, from.Color));
                any = true;
            }

            if (what.HasFlag(ClipAttributes.Metadata))
            {
                foreach ((string key, object? value) in from.Metadata.Where(p => p.Value is string or bool or long or double))
                {
                    history.Execute(new SetMetadata(target, key, value));
                    any = true;
                }
            }

            changed += any ? 1 : 0;
        }

        return changed;
    }

    // At the same offset into the target's media as into the copied clip's, and only those that fall within what the target plays.
    private static bool Markers(History history, Item from, Item target)
    {
        TimeRange source = from.TrimmedRange(), into = target.TrimmedRange();
        bool any = false;
        foreach (Marker marker in from.Markers)
        {
            RationalTime offset = marker.MarkedRange.StartTime - source.StartTime.RescaledTo(marker.MarkedRange.StartTime);
            if (offset.Value < 0 || offset >= into.Duration.RescaledTo(offset))
            {
                continue;
            }

            RationalTime start = into.StartTime.RescaledTo(offset) + offset;
            history.Execute(new AddMarker(target, marker.Name, new TimeRange(start, marker.MarkedRange.Duration), marker.Color));
            any = true;
        }

        return any;
    }
}
