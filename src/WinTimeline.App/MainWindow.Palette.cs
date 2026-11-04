using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinTimeline.App.Palette;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;

namespace WinTimeline.App;

/// <summary>
/// WT66: the command palette over every command in <see cref="CommandTable"/>, the shown
/// timeline's clips and markers, and timecodes. A command runs as if its menu item were chosen,
/// so the palette can never disagree with the menus about what a command does.
/// </summary>
public partial class MainWindow
{
    /// <summary>Opens the palette; Ctrl+Shift+P.</summary>
    public static readonly RoutedUICommand PaletteCommand = new(UiText.PaletteCommandText, "Palette", typeof(MainWindow));

    private readonly List<string> _recentCommands = [];

    /// <summary>What the palette offers for what was typed, against the window as it is.</summary>
    /// <param name="typed">What was typed.</param>
    /// <returns>The results.</returns>
    public IReadOnlyList<PaletteResult> SearchPalette(string typed) => PaletteSearch.Search(typed, PaletteSourcesNow());

    /// <summary>Runs a palette result: a command through its menu item, or a jump of the playhead.</summary>
    /// <param name="result">The result.</param>
    public void RunPaletteResult(PaletteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        switch (result.Kind)
        {
            case PaletteKind.Command when result.CommandId is { } id:
                _recentCommands.Remove(id);
                _recentCommands.Insert(0, id);
                Choose(MenuItemFor(id));
                break;
            case PaletteKind.Clip or PaletteKind.Marker or PaletteKind.Timecode:
                TimelinePanel.Seek(result.Seconds);
                StatusText.Text = UiText.PlayheadAt(TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds), result.Title);
                break;
            case PaletteKind.Ask:
                StatusText.Text = UiText.AssistantNotHere;
                break;
        }
    }

    private void BindPalette()
    {
        CommandBindings.Add(new CommandBinding(PaletteCommand, (_, _) => OpenPalette()));
        InputBindings.Add(new KeyBinding(PaletteCommand, new KeyGesture(Key.P, ModifierKeys.Control | ModifierKeys.Shift)));

        // Every command in the table backs a menu item, and the palette is no use if one does not.
        string[] missing = [.. CommandTable.All.Where(a => FindMenuItem(MainMenu.Items, a.MenuItem) is null).Select(a => a.Command.Id)];
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"commands with no menu item: {string.Join(", ", missing)}");
        }
    }

    private void OnPalette(object sender, RoutedEventArgs e) => OpenPalette();

    private void OpenPalette()
    {
        PaletteWindow palette = new(SearchPalette) { Owner = this };
        if (palette.ShowDialog() == true && palette.Chosen is { } chosen)
        {
            RunPaletteResult(chosen);
        }
    }

    private PaletteSources PaletteSourcesNow()
    {
        List<PalettePlace> clips = [];
        List<PalettePlace> markers = [];
        if (TimelinePanel.Layout is { } layout)
        {
            foreach (LaneItem item in layout.Lanes.SelectMany(l => l.Items).Where(i => i.Kind == LaneItemKind.Clip))
            {
                string lane = layout.Lanes[item.Lane].Label;
                clips.Add(new PalettePlace(item.Name, item.Start, UiText.LaneAt(lane, TimelinePanel.TimecodeAt(item.Start))));
                if (item.Item is Clip clip)
                {
                    foreach (Marker marker in clip.Markers)
                    {
                        double at = item.Start + (marker.MarkedRange.StartTime - clip.TrimmedRange().StartTime).ValueRescaledTo(1);
                        markers.Add(new PalettePlace(marker.Name.Length > 0 ? marker.Name : marker.Comment, at, UiText.LaneAt(lane, TimelinePanel.TimecodeAt(at))));
                    }
                }
            }
        }

        return new PaletteSources(CommandTable.Commands, _recentCommands, clips, markers, TimelinePanel.ParseTimecode);
    }

    private MenuItem MenuItemFor(string commandId)
    {
        string automationId = CommandTable.All.First(a => a.Command.Id == commandId).MenuItem;
        return FindMenuItem(MainMenu.Items, automationId) ?? throw new InvalidOperationException($"no menu item {automationId}");
    }

    /// <summary>Does what choosing a menu item does: its command if it has one, otherwise its Click.</summary>
    private void Choose(MenuItem item)
    {
        if (item.Command is RoutedCommand routed)
        {
            if (routed.CanExecute(item.CommandParameter, this))
            {
                routed.Execute(item.CommandParameter, this);
            }
            else
            {
                StatusText.Text = UiText.CannotBeDoneNow(UiText.Plain(item.Header.ToString()));
            }

            return;
        }

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    private static MenuItem? FindMenuItem(ItemCollection items, string automationId)
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            if (AutomationProperties.GetAutomationId(item) == automationId)
            {
                return item;
            }

            if (FindMenuItem(item.Items, automationId) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
