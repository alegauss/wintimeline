using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT70: a released drag of clips carried out as one undo step: lifted from where they were
/// (left there for a duplicate) and put down by overwrite, insert or swap on the lanes they were
/// dropped on, which must carry the same kind of media.
/// </summary>
public partial class MainWindow
{
    private void BindMove() => TimelinePanel.MoveRequested += (_, request) => MoveClips(request);

    /// <summary>Carries out a drag of clips.</summary>
    /// <param name="request">What was dragged where, and how.</param>
    public void MoveClips(MoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (TimelinePanel.Layout is not { } layout || request.Items.Count == 0)
        {
            return;
        }

        try
        {
            List<(LaneItem Item, Track From, Track To)> moves = [];
            foreach (LaneItem item in request.Items.OrderBy(i => i.Start))
            {
                int lane = item.Lane + request.Lanes;
                if (lane < 0 || lane >= layout.Lanes.Count || layout.Lanes[lane].IsAudio != layout.Lanes[item.Lane].IsAudio)
                {
                    throw new EditRefusedException(UiText.MoveWrongTrack(item.Name));
                }

                moves.Add((item, layout.Lanes[item.Lane].Track, layout.Lanes[lane].Track));
            }

            using History.TransactionScope scope = History.BeginTransaction(UiText.MoveStep(request.Mode, request.Duplicate));
            try
            {
                Move(request, moves);
            }
            catch (EditRefusedException)
            {
                scope.Cancel();
                throw;
            }

            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.Moved(moves.Count, request.Mode, request.Duplicate);
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
        }
    }

    private void Move(MoveRequest request, List<(LaneItem Item, Track From, Track To)> moves)
    {
        if (request.Mode == MoveMode.Swap)
        {
            // One clip changes places with the one it was dropped on: out of its place, closing the
            // hole, and in again where the other starts now.
            if (moves is not [var (item, from, to)] || request.Onto is not { } onto || ReferenceEquals(onto.Item, item.Item))
            {
                throw new EditRefusedException(UiText.SwapNeedsOne);
            }

            Composable moved = TrackEdits.ExtractChild(History, from, item.Item);
            double at = TrackEdits.Placed(to).First(p => ReferenceEquals(to.Children[p.Index], onto.Item)).Start;
            TrackEdits.Insert(History, to, at, [moved]);
            return;
        }

        // Taken up first, all of them, so none lands on a clip about to leave; times do not move,
        // since a lift leaves a gap.
        List<(Composable Item, Track To, double At)> drops = [];
        foreach ((LaneItem item, Track from, Track to) in moves)
        {
            Composable dropped = request.Duplicate ? TrackEdits.Duplicate(item.Item) : TrackEdits.Lift(History, from, item.Item);
            drops.Add((dropped, to, Math.Max(0, item.Start + request.Delta)));
        }

        foreach ((Composable item, Track to, double at) in drops)
        {
            if (request.Mode == MoveMode.Insert)
            {
                TrackEdits.Insert(History, to, at, [item]);
            }
            else
            {
                TrackEdits.Overwrite(History, to, at, [item]);
            }
        }
    }
}
