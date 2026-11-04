using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using Track = WinTimeline.Model.Track;

namespace WinTimeline.App;

/// <summary>
/// WT50: one WPF header per lane beside the timeline surface, scrolled with its lanes: the
/// track's name (edited in place), lock, mute or disable, solo, hide for video, and add and
/// delete. Every change is a command in the timeline's history, so it undoes; the lock is
/// enforced by the history's guard, not by these buttons.
/// </summary>
public sealed class TrackHeaders : Border
{
    private const double Width0 = 168;
    private readonly Canvas _lanes = new() { ClipToBounds = true };
    private readonly TimelinePanel _panel;
    private readonly Func<History> _history;
    private readonly Action<string> _say;
    private Timeline? _timeline;

    /// <summary>Headers for a panel.</summary>
    /// <param name="panel">The timeline panel they sit beside.</param>
    /// <param name="history">The shown timeline's history.</param>
    /// <param name="say">Tells the status bar why an edit was refused.</param>
    public TrackHeaders(TimelinePanel panel, Func<History> history, Action<string> say)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _say = say ?? throw new ArgumentNullException(nameof(say));
        Width = Width0;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "LayerFillColorDefaultBrush");
        AutomationProperties.SetAutomationId(this, "TrackHeaders");
        Child = new DockPanel { LastChildFill = true, Children = { Spacer(), _lanes } };
        panel.LanesMoved += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    /// <summary>Shows the headers of a timeline, or none.</summary>
    /// <param name="timeline">The timeline.</param>
    public void Show(Timeline? timeline)
    {
        if (_timeline is not null)
        {
            _timeline.ModelChanged -= OnModelChanged;
        }

        _timeline = timeline;
        if (timeline is not null)
        {
            timeline.ModelChanged += OnModelChanged;
        }

        Rebuild();
    }

    private static Border Spacer()
    {
        Border spacer = new() { Height = TimelinePanel.LanesTopDip };
        DockPanel.SetDock(spacer, Dock.Top);
        return spacer;
    }

    private void OnModelChanged(object? sender, ModelChange e) => Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        _lanes.Children.Clear();
        if (_timeline is null)
        {
            return;
        }

        TimelineLayout layout = TimelineLayout.Build(_timeline);
        for (int i = 0; i < layout.Lanes.Count; i++)
        {
            _lanes.Children.Add(Header(layout.Lanes[i], i));
        }

        Place();
    }

    private void Place()
    {
        double top = -_panel.LanesScrollDip;
        foreach (FrameworkElement header in _lanes.Children.OfType<FrameworkElement>())
        {
            Canvas.SetTop(header, top + ((int)header.Tag * TimelinePanel.LaneHeightDip));
        }
    }

    private Grid Header(Lane lane, int index)
    {
        Track track = lane.Track;
        string id = lane.Label;
        Grid header = new() { Tag = index, Width = Width0, Height = TimelinePanel.LaneHeightDip, Margin = new Thickness(0) };
        AutomationProperties.SetAutomationId(header, $"TrackHeader-{id}");
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBox name = new() { Text = track.Name, Margin = new Thickness(4, 2, 2, 2), Padding = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 11, MinHeight = 0 };
        AutomationProperties.SetAutomationId(name, $"TrackName-{id}");
        AutomationProperties.SetName(name, UiText.TrackNameName(id));
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && name.Text.Length > 0 && name.Text != track.Name)
            {
                Run(new Rename(track, name.Text));
                e.Handled = true;
            }
        };
        header.Children.Add(name);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 2, 0) };
        Grid.SetColumn(buttons, 1);
        buttons.Children.Add(Toggle($"TrackLock-{id}", UiText.TrackLockGlyph, UiText.TrackLock, TrackState.IsLocked(track), on => TrackState.Set(track, TrackState.LockedKey, on)));
        buttons.Children.Add(Toggle($"TrackMute-{id}", lane.IsAudio ? UiText.TrackMuteGlyph : UiText.TrackDisableGlyph, lane.IsAudio ? UiText.TrackMute : UiText.TrackDisable, !track.Enabled, on => new SetEnabled(track, !on)));
        buttons.Children.Add(Toggle($"TrackSolo-{id}", UiText.TrackSoloGlyph, UiText.TrackSolo, TrackState.IsSolo(track), on => TrackState.Set(track, TrackState.SoloKey, on)));
        if (!lane.IsAudio)
        {
            buttons.Children.Add(Toggle($"TrackHide-{id}", UiText.TrackHideGlyph, UiText.TrackHide, TrackState.IsHidden(track), on => TrackState.Set(track, TrackState.HiddenKey, on)));
        }

        buttons.Children.Add(Button($"TrackAdd-{id}", UiText.TrackAddGlyph, UiText.TrackAdd(lane.IsAudio), () => AddBeside(track)));
        buttons.Children.Add(Button($"TrackDelete-{id}", UiText.TrackDeleteGlyph, UiText.TrackDelete, () => Delete(track)));
        header.Children.Add(buttons);
        return header;
    }

    private ToggleButton Toggle(string id, string glyph, string name, bool on, Func<bool, IEditCommand> command)
    {
        ToggleButton toggle = new() { Content = glyph, IsChecked = on, Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 10, Margin = new Thickness(1, 0, 0, 0), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
        AutomationProperties.SetAutomationId(toggle, id);
        AutomationProperties.SetName(toggle, name);
        toggle.ToolTip = name;
        toggle.Click += (_, _) => Run(command(toggle.IsChecked == true));
        return toggle;
    }

    private static Button Button(string id, string glyph, string name, Action run)
    {
        Button button = new() { Content = glyph, Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 10, Margin = new Thickness(1, 0, 0, 0), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        button.ToolTip = name;
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>A new track of the same kind next to this one: above it for video, below for audio, which is one place further up the stack either way.</summary>
    private void AddBeside(Track track)
    {
        if (_timeline is not { } timeline)
        {
            return;
        }

        int count = timeline.Tracks.Children.OfType<Track>().Count(t => t.Kind == track.Kind);
        string name = (track.Kind == TrackKind.Audio ? "A" : "V") + (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Run(TrackCommands.Add(timeline, timeline.Tracks.Children.ToList().IndexOf(track) + 1, track.Kind, name));
    }

    private void Delete(Track track)
    {
        if (_timeline is not { } timeline)
        {
            return;
        }

        if (track.Children.Count > 0
            && MessageBox.Show(Window.GetWindow(this), UiText.TrackDeleteQuestion(track.Name, track.Children.Count), UiText.TrackDelete, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        Run(TrackCommands.Delete(timeline, track));
    }

    private void Run(IEditCommand command)
    {
        try
        {
            _history().Execute(command);
        }
        catch (EditRefusedException refused)
        {
            _say(refused.Message);
            Rebuild();
        }
    }
}
