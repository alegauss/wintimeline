using System.Windows;
using System.Windows.Input;

namespace WinTimeline.App;

/// <summary>WT48: the timeline's zoom from the View menu and the keyboard: + and - about the playhead, Shift+Z to fit.</summary>
public partial class MainWindow
{
    // The keys are read in PreviewKeyDown rather than as key bindings, which WPF refuses (and
    // throws for) on a key with no modifier, and never while a text box has the focus.
    private void BindZoom() => PreviewKeyDown += OnZoomKey;

    private void OnZoomKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        Action? zoom = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.OemPlus or Key.Add, ModifierKeys.None or ModifierKeys.Shift) => TimelinePanel.ZoomIn,
            (Key.OemMinus or Key.Subtract, ModifierKeys.None) => TimelinePanel.ZoomOut,
            (Key.Z, ModifierKeys.Shift) => TimelinePanel.ZoomToFit,
            _ => null,
        };
        if (zoom is not null)
        {
            zoom();
            e.Handled = true;
        }
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => TimelinePanel.ZoomIn();

    private void OnZoomOut(object sender, RoutedEventArgs e) => TimelinePanel.ZoomOut();

    private void OnZoomFit(object sender, RoutedEventArgs e) => TimelinePanel.ZoomToFit();
}
