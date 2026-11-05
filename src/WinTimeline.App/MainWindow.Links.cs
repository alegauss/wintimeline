using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT79: links and groups from the Edit menu and the keyboard (Ctrl+L links, Ctrl+Shift+L breaks
/// the link, Ctrl+G groups, Ctrl+Shift+G ungroups), linked selection toggled with L, and Move into
/// sync for a linked clip that slid. Each is one undo step.
/// </summary>
public partial class MainWindow
{
    private void BindLinks() => PreviewKeyDown += OnLinkKey;

    // Only while a surface has the focus.
    private void OnLinkKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        Action? act = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.L, ModifierKeys.Control) => () => Join(ItemLinks.Link, UiText.Linked),
            (Key.L, ModifierKeys.Control | ModifierKeys.Shift) => () => Leave(ItemLinks.Unlink, UiText.Unlinked),
            (Key.G, ModifierKeys.Control) => () => Join(ItemLinks.Group, UiText.Grouped),
            (Key.G, ModifierKeys.Control | ModifierKeys.Shift) => () => Leave(ItemLinks.Ungroup, UiText.Ungrouped),
            (Key.L, ModifierKeys.None) => () => SetLinkedSelection(!TimelinePanel.LinkedSelection),
            _ => null,
        };
        if (act is not null)
        {
            act();
            e.Handled = true;
        }
    }

    private void OnLink(object sender, RoutedEventArgs e) => Join(ItemLinks.Link, UiText.Linked);

    private void OnUnlink(object sender, RoutedEventArgs e) => Leave(ItemLinks.Unlink, UiText.Unlinked);

    private void OnGroup(object sender, RoutedEventArgs e) => Join(ItemLinks.Group, UiText.Grouped);

    private void OnUngroup(object sender, RoutedEventArgs e) => Leave(ItemLinks.Ungroup, UiText.Ungrouped);

    private void OnMoveIntoSync(object sender, RoutedEventArgs e) => MoveIntoSync();

    // The item is checkable, so a click has already flipped it: that is the state now.
    private void OnLinkedSelection(object sender, RoutedEventArgs e) => SetLinkedSelection(LinkedSelectionMenuItem.IsChecked);

    /// <summary>Turns linked selection on or off and ticks its menu item.</summary>
    /// <param name="on">Whether a clip's link comes with it.</param>
    public void SetLinkedSelection(bool on)
    {
        TimelinePanel.LinkedSelection = on;
        LinkedSelectionMenuItem.IsChecked = on;
        StatusText.Text = on ? UiText.LinkedSelectionOn : UiText.LinkedSelectionOff;
    }

    private List<LaneItem> Chosen() =>
        TimelinePanel.Layout is { } layout ? [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>()] : [];

    private void Join(Func<IReadOnlyList<Composable>, IReadOnlyList<IEditCommand>> plan, Func<int, string> said)
    {
        List<LaneItem> chosen = Chosen();
        if (Run(() => plan([.. chosen.Select(i => i.Item)]), UiText.LinkMenu))
        {
            StatusText.Text = said(chosen.Count);
        }
    }

    private void Leave(Func<IEnumerable<Composable>, IReadOnlyList<IEditCommand>> plan, Func<int, string> said)
    {
        List<LaneItem> chosen = Chosen();
        if (Run(() => plan(chosen.Select(i => i.Item)), UiText.UnlinkMenu))
        {
            StatusText.Text = said(chosen.Count);
        }
    }

    /// <summary>Moves each selected linked clip that slid back by its offset, so it lines up with its link again.</summary>
    public void MoveIntoSync()
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<(LaneItem Item, long Frames)> slid = [.. Chosen().Select(i => (i, TimelinePanel.SyncOffset(i) ?? 0)).Where(p => p.Item2 != 0)];
        if (slid.Count == 0)
        {
            StatusText.Text = UiText.NothingOutOfSync;
            return;
        }

        try
        {
            using History.TransactionScope scope = History.BeginTransaction(UiText.Plain(UiText.MoveIntoSyncMenu));
            try
            {
                foreach ((LaneItem item, long frames) in slid)
                {
                    Track track = layout.Lanes[item.Lane].Track;
                    double at = item.Start - (frames / TimelinePanel.FrameRate);
                    Composable lifted = TrackEdits.Lift(History, track, item.Item);
                    TrackEdits.Overwrite(History, track, Math.Max(0, at), [lifted]);
                }
            }
            catch (EditRefusedException)
            {
                scope.Cancel();
                throw;
            }

            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.MovedIntoSync(slid.Count);
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
        }
    }
}
