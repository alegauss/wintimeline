using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT56: Edit > Make compound clip nests the selected run of one track's clips into a compound,
/// and Edit > Break apart flattens a selected compound back, each as one undo step.
/// </summary>
public partial class MainWindow
{
    private void BindCompound() => PreviewKeyDown += OnCompoundKey;

    // WT56: Enter opens the selected compound and Esc goes back up, while a surface has the focus.
    private void OnCompoundKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || Keyboard.Modifiers != ModifierKeys.None || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        if (e.Key == Key.Enter && TimelinePanel.Layout is { } layout
            && TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().ToList() is [{ Item: Stack compound }])
        {
            TimelinePanel.Open(compound);
            StatusText.Text = UiText.CompoundOpened(compound.Name);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && TimelinePanel.Up())
        {
            e.Handled = true;
        }
    }

    private void OnMakeCompound(object sender, RoutedEventArgs e) => MakeCompound();

    private void OnBreakApart(object sender, RoutedEventArgs e) => BreakApart();

    /// <summary>Nests the selected items, all on one track, into a compound clip.</summary>
    public void MakeCompound()
    {
        if (ActiveTimeline is not { } timeline || TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<LaneItem> chosen = [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>()];
        if (chosen.Count == 0 || chosen.Select(i => i.Lane).Distinct().Count() != 1)
        {
            StatusText.Text = UiText.CompoundNeedsRun;
            return;
        }

        Track track = layout.Lanes[chosen[0].Lane].Track;
        List<int> indices = [.. chosen.Select(i => track.Children.ToList().IndexOf(i.Item)).Order()];
        string name = UiText.NewCompoundName(timeline.Tracks.Children.OfType<Track>().Sum(t => t.Children.OfType<Stack>().Count()) + 1);
        if (Run(() => Compound.Make(track, indices[0], indices[^1], name), UiText.MakeCompoundMenu))
        {
            // Ids are positions on a lane, so what was selected now names other items.
            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.CompoundMade(name, indices[^1] - indices[0] + 1);
        }
    }

    /// <summary>Flattens the one selected compound clip onto its track.</summary>
    public void BreakApart()
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        if (TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().ToList() is not [{ Item: Stack stack } chosen])
        {
            StatusText.Text = UiText.BreakApartNeedsCompound;
            return;
        }

        Track track = layout.Lanes[chosen.Lane].Track;
        if (Run(() => Compound.BreakApart(track, track.Children.ToList().IndexOf(stack)), UiText.BreakApartMenu))
        {
            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.CompoundBroken(stack.Name);
        }
    }

    // One history step, or none at all when the plan or any of its commands is refused.
    private bool Run(Func<IReadOnlyList<IEditCommand>> plan, string step)
    {
        try
        {
            IReadOnlyList<IEditCommand> commands = plan();
            using History.TransactionScope scope = History.BeginTransaction(UiText.Plain(step));
            try
            {
                foreach (IEditCommand command in commands)
                {
                    History.Execute(command);
                }
            }
            catch (EditRefusedException)
            {
                scope.Cancel();
                throw;
            }

            return true;
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
            return false;
        }
    }
}
