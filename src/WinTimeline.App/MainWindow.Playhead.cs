using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;

namespace WinTimeline.App;

/// <summary>
/// WT51: the playhead from the keyboard (arrows step a frame, Shift ten, Up and Down jump between
/// edit points, Home and End go to the programme's limits), its follow mode from View > Follow
/// playhead, and the viewer tied to the same position.
/// </summary>
public partial class MainWindow
{
    private void BindPlayhead()
    {
        PreviewKeyDown += OnPlayheadKey;
        TimelinePanel.Position.Changed += (_, frame) => ShowViewerFrame(frame);
        ShowFollow();
    }

    /// <summary>Sets the follow mode and ticks the menu item that names it.</summary>
    /// <param name="mode">The mode.</param>
    public void SetFollow(PlayheadFollow mode)
    {
        TimelinePanel.Follow = mode;
        ShowFollow();
    }

    // Only while one of the surfaces has the focus: anywhere else the arrows belong to the
    // control that has it (a menu, a tab strip, a track header).
    private void OnPlayheadKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        bool shift = Keyboard.Modifiers == ModifierKeys.Shift;
        Action? move = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.Left or Key.Right, ModifierKeys.None or ModifierKeys.Shift) => () => TimelinePanel.Step((e.Key == Key.Right ? 1 : -1) * (shift ? 10 : 1)),
            (Key.Up or Key.Down, ModifierKeys.None) => () => TimelinePanel.JumpToEdit(e.Key == Key.Down),
            (Key.Home or Key.End, ModifierKeys.None) => () => TimelinePanel.GoToLimit(e.Key == Key.End),
            (Key.S, ModifierKeys.None) => () => SetSnapping(!TimelinePanel.Snapping),
            _ => null,
        };
        if (move is not null)
        {
            move();
            e.Handled = true;
        }
    }

    private void ShowViewerFrame(long frame)
    {
        Viewer.Status = ActiveTimeline is null ? null : UiText.ViewerFrame(TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds), frame);
        Viewer.ItemsChanged();
        Viewer.Invalidate();
    }

    /// <summary>WT53: turns snapping on or off and ticks the menu item.</summary>
    /// <param name="on">Whether it snaps.</param>
    public void SetSnapping(bool on)
    {
        TimelinePanel.Snapping = on;
        SnappingMenuItem.IsChecked = on;
    }

    // WT57: a heat lane's checkable item, named by its tag; the click has already flipped it.
    private void OnHeatLane(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } item && Enum.TryParse(name, out HeatKind kind))
        {
            TimelinePanel.ShowHeat(kind, item.IsChecked);
        }
    }

    // The item is checkable, so a click has already flipped IsChecked: that is the new state.
    private void OnSnapping(object sender, RoutedEventArgs e) => SetSnapping(SnappingMenuItem.IsChecked);

    private void OnFollow(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse(name, out PlayheadFollow mode))
        {
            SetFollow(mode);
        }
    }

    private void ShowFollow()
    {
        foreach (MenuItem item in new[] { FollowPage, FollowSmooth, FollowOff })
        {
            item.IsChecked = item.Tag is string name && name == TimelinePanel.Follow.ToString();
        }
    }
}
