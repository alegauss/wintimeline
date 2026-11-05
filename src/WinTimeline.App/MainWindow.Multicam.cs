using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT81: Edit > Create multicam makes the selected clips the angles of a multicam clip, lined up
/// by timecode or by in points, where the first of them was; the number keys 1 to 9 cut it to an
/// angle at the playhead. Each is one undo step.
/// </summary>
public partial class MainWindow
{
    private void BindMulticam() => PreviewKeyDown += OnAngleKey;

    // Only while a surface has the focus: the digits are a text box's anywhere else.
    private void OnAngleKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || Keyboard.Modifiers != ModifierKeys.None || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        int angle = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (angle >= 0 && CutToAngle(angle))
        {
            e.Handled = true;
        }
    }

    // An angle's menu item carries its index, 0 for the first, as its tag.
    private void OnCutToAngle(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } && int.TryParse(tag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int angle)
            && !CutToAngle(angle))
        {
            StatusText.Text = UiText.NoMulticamHere;
        }
    }

    private void OnMulticamTimecode(object sender, RoutedEventArgs e) => CreateMulticam(MulticamSync.Timecode);

    private void OnMulticamInPoints(object sender, RoutedEventArgs e) => CreateMulticam(MulticamSync.InPoints);

    /// <summary>Makes the selected clips the angles of a multicam clip where the earliest of them starts, on its track.</summary>
    /// <param name="sync">How the angles line up.</param>
    public void CreateMulticam(MulticamSync sync)
    {
        if (ActiveTimeline is not { } timeline || TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<LaneItem> angles = [.. Chosen().Where(i => i.Item is Item).OrderBy(i => i.Start).ThenBy(i => i.Lane)];
        string name = UiText.NewMulticamName(timeline.Tracks.Children.OfType<Track>().Sum(t => t.Children.Count(c => Multicam.AngleOf(c) is not null)) + 1);
        LaneItem first = angles.FirstOrDefault()!;
        bool made = Run(
            () =>
            {
                List<Item> lifted = [.. angles.Select(a => (Item)TrackEdits.Lift(History, layout.Lanes[a.Lane].Track, a.Item))];
                TrackEdits.Overwrite(History, layout.Lanes[first.Lane].Track, first.Start, [Multicam.Create(lifted, name, sync)]);
                return [];
            },
            UiText.MulticamMenu);
        if (made)
        {
            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.MulticamMade(name, angles.Count);
        }
    }

    /// <summary>Cuts the multicam clip at the playhead to an angle: on the selected one's track, or the first track with one there.</summary>
    /// <param name="angle">The angle, 0 for the first.</param>
    /// <returns>Whether a multicam was there to cut.</returns>
    public bool CutToAngle(int angle)
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return false;
        }

        double at = TimelinePanel.PlayheadSeconds;
        IEnumerable<Lane> lanes = Chosen().Where(i => Multicam.AngleOf(i.Item) is not null).Select(i => layout.Lanes[i.Lane])
            .Concat(layout.Lanes.Where(l => l.Items.Any(i => Multicam.AngleOf(i.Item) is not null && i.Start <= at && at < i.End)));
        if (lanes.FirstOrDefault() is not { } lane)
        {
            return false;
        }

        bool cut = false;
        if (Run(
            () =>
            {
                cut = Multicam.CutTo(History, lane.Track, at, angle);
                return [];
            },
            UiText.MulticamMenu))
        {
            StatusText.Text = cut ? UiText.CutToAngle(angle + 1, TimelinePanel.TimecodeAt(at)) : UiText.AlreadyAngle(angle + 1);
        }

        return true;
    }
}
