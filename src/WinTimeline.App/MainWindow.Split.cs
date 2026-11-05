using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT71: splitting. Ctrl+K cuts at the playhead the tracks of the selected clips, or every track
/// when nothing is selected (track targets come with WT169); Ctrl+Shift+K cuts every track. B
/// chooses the razor, which cuts the lane it is pressed on at the snapped frame, and V goes back to
/// selecting. Each split is one undo step.
/// </summary>
public partial class MainWindow
{
    private void BindSplit()
    {
        PreviewKeyDown += OnSplitKey;
        TimelinePanel.SplitRequested += (_, at) => SplitLane(at.Lane, at.Seconds);
    }

    // Only while a surface has the focus: B, V and Ctrl+K are a text box's anywhere else.
    private void OnSplitKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        Action? act = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.K, ModifierKeys.Control) => () => SplitAtPlayhead(allTracks: false),
            (Key.K, ModifierKeys.Control | ModifierKeys.Shift) => () => SplitAtPlayhead(allTracks: true),
            (Key.B, ModifierKeys.None) => () => SetRazor(true),
            (Key.V, ModifierKeys.None) => () => SetRazor(false),
            _ => null,
        };
        if (act is not null)
        {
            act();
            e.Handled = true;
        }
    }

    private void OnSplitAtPlayhead(object sender, RoutedEventArgs e) => SplitAtPlayhead(allTracks: false);

    private void OnSplitAllTracks(object sender, RoutedEventArgs e) => SplitAtPlayhead(allTracks: true);

    // The item is checkable, so a click has already flipped it: that is the tool now.
    private void OnRazor(object sender, RoutedEventArgs e) => SetRazor(RazorMenuItem.IsChecked);

    /// <summary>Chooses the razor tool, or the selection tool.</summary>
    /// <param name="on">Whether the razor.</param>
    public void SetRazor(bool on)
    {
        TimelinePanel.Razor = on;
        RazorMenuItem.IsChecked = on;
        StatusText.Text = on ? UiText.RazorOn : UiText.RazorOff;
    }

    /// <summary>Splits at the playhead the tracks of the selected clips (every track with none selected), or every track.</summary>
    /// <param name="allTracks">Whether every track, whatever is selected.</param>
    public void SplitAtPlayhead(bool allTracks)
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<int> lanes = [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().Select(i => i.Lane).Distinct()];
        IEnumerable<Track> tracks = allTracks || lanes.Count == 0 ? layout.Lanes.Select(l => l.Track) : lanes.Select(l => layout.Lanes[l].Track);
        Split(tracks, TimelinePanel.PlayheadSeconds);
    }

    private void SplitLane(int lane, double seconds)
    {
        if (TimelinePanel.Layout is { } layout && lane < layout.Lanes.Count)
        {
            Split([layout.Lanes[lane].Track], seconds);
        }
    }

    private void Split(IEnumerable<Track> tracks, double seconds)
    {
        string at = TimelinePanel.TimecodeAt(seconds);
        try
        {
            int cut;
            using (History.TransactionScope scope = History.BeginTransaction(UiText.Plain(UiText.SplitMenu)))
            {
                try
                {
                    cut = TrackEdits.SplitAll(History, tracks, seconds);
                }
                catch (EditRefusedException)
                {
                    scope.Cancel();
                    throw;
                }
            }

            StatusText.Text = UiText.Split(cut, at);
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
        }
    }
}
