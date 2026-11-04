using System.Windows;
using System.Windows.Input;

namespace WinTimeline.App;

/// <summary>
/// WT52: the timeline's selection from the Edit menu and the keyboard: Ctrl+A everything,
/// Ctrl+Shift+A nothing, A every item from the playhead on and Shift+A every item up to it.
/// Clicks, Ctrl and Shift clicks and the marquee are the panel's own.
/// </summary>
public partial class MainWindow
{
    private void BindSelection() => PreviewKeyDown += OnSelectionKey;

    // Only while a surface has the focus: Ctrl+A in a text box selects its text.
    private void OnSelectionKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        Action? select = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.A, ModifierKeys.Control) => TimelinePanel.SelectAll,
            (Key.A, ModifierKeys.Control | ModifierKeys.Shift) => TimelinePanel.Selection.Clear,
            (Key.A, ModifierKeys.None) => () => TimelinePanel.TrackSelect(forward: true),
            (Key.A, ModifierKeys.Shift) => () => TimelinePanel.TrackSelect(forward: false),
            _ => null,
        };
        if (select is not null)
        {
            select();
            e.Handled = true;
        }
    }

    private void OnSelectAll(object sender, RoutedEventArgs e) => TimelinePanel.SelectAll();

    private void OnDeselectAll(object sender, RoutedEventArgs e) => TimelinePanel.Selection.Clear();

    private void OnTrackForward(object sender, RoutedEventArgs e) => TimelinePanel.TrackSelect(forward: true);

    private void OnTrackBackward(object sender, RoutedEventArgs e) => TimelinePanel.TrackSelect(forward: false);
}
