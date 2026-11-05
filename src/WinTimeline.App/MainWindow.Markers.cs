using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Time;

namespace WinTimeline.App;

/// <summary>
/// WT54: markers from the keyboard and the Edit menu. M adds one to the timeline at the playhead,
/// and M on a marker opens its editor; Shift+Up and Shift+Down go to the marker before and after.
/// </summary>
public partial class MainWindow
{
    private void BindMarkers() => PreviewKeyDown += OnMarkerKey;

    // Only while a surface has the focus: M and Shift+arrows are a text box's anywhere else.
    private void OnMarkerKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        Action? act = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.M, ModifierKeys.None) => AddOrEditMarker,
            (Key.Up or Key.Down, ModifierKeys.Shift) => () => TimelinePanel.JumpToMarker(e.Key == Key.Down),
            _ => null,
        };
        if (act is not null)
        {
            act();
            e.Handled = true;
        }
    }

    private void OnAddMarker(object sender, RoutedEventArgs e) => AddOrEditMarker();

    /// <summary>Adds a marker to the timeline at the playhead, or opens the editor of the one already there.</summary>
    public void AddOrEditMarker()
    {
        if (ActiveTimeline is not { } timeline)
        {
            return;
        }

        double rate = TimelinePanel.FrameRate;
        string at = TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds);
        try
        {
            if (TimelinePanel.MarkerAtPlayhead is { } there)
            {
                EditMarker(there, rate);
                return;
            }

            // On the stack shown (WT56: a compound opened, or the timeline's own), in its own time,
            // which starts where its trimmed range does (0 for nearly every file).
            Stack stack = TimelinePanel.Shown ?? timeline.Tracks;
            double origin = stack.SourceRange?.StartTime.Seconds ?? 0;
            RationalTime start = new(Math.Round((origin + TimelinePanel.PlayheadSeconds) * rate), rate);
            string name = UiText.NewMarkerName(stack.Markers.Count + 1);
            History.Execute(new AddMarker(stack, name, new TimeRange(start, new RationalTime(0, rate)), MarkerColor.Green));
            StatusText.Text = UiText.MarkerAdded(name, at);
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
        }
    }

    private void EditMarker(PlacedMarker placed, double rate)
    {
        MarkerWindow editor = new(placed.Marker, rate) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        RationalTime start = placed.Marker.MarkedRange.StartTime;
        TimeRange range = new(start, new RationalTime(editor.ChosenFrames, rate).RescaledTo(start.Rate > 0 ? start.Rate : rate));
        History.Execute(new EditMarker(placed.Marker, editor.ChosenName, editor.ChosenColor, range, editor.ChosenComment));
    }
}
