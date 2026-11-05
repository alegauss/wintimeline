using System.IO;
using System.Windows;
using WinTimeline.Engine.Analysis;
using WinTimeline.Engine.Timeline;
using WinTimeline.Media;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Media;

namespace WinTimeline.App;

/// <summary>
/// WT121: Edit > Detect beats marks the beats of the selected music clips, found off the UI
/// thread from their decoded sound; Edit > Cut to beats lays the selected clips from one beat
/// marker to the next (every beat, every second or every fourth), each trimmed to its gap. Each is
/// one undo step, and snapping takes the beat markers like any others.
/// </summary>
public partial class MainWindow
{
    private async void OnDetectBeats(object sender, RoutedEventArgs e) => await DetectBeatsAsync().ConfigureAwait(true);

    private void OnCutToBeats(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } && int.TryParse(tag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int every))
        {
            CutToBeats(every);
        }
    }

    /// <summary>Marks the beats of the selected clips' sound.</summary>
    /// <returns>When it is done.</returns>
    public async Task DetectBeatsAsync()
    {
        List<Clip> clips = [.. Chosen().Select(i => i.Item).OfType<Clip>()];
        string? folder = DocumentPath is { } document ? Path.GetDirectoryName(document) : null;
        List<(Clip Clip, string Path)> files = [.. clips.Select(c => (c, c.MediaReference is { } r ? MediaLinks.PathOf(r, folder) : null)).Where(p => p.Item2 is not null).Select(p => (p.c, p.Item2!))];
        if (files.Count == 0)
        {
            StatusText.Text = UiText.BeatsNeedMusic;
            return;
        }

        StatusText.Text = UiText.DetectingBeats;
        List<(Clip Clip, BeatAnalysis Found)> found = [];
        try
        {
            foreach ((Clip clip, string path) in files)
            {
                BeatAnalysis beats = await Task.Run(() =>
                {
                    DecodedAudio audio = AudioDecoder.Read(path);
                    return Beats.Detect(audio.Samples, audio.Rate);
                }).ConfigureAwait(true);
                found.Add((clip, beats));
            }
        }
        catch (MediaProbeException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        int marked = 0;
        using (History.BeginTransaction(UiText.Plain(UiText.DetectBeatsMenu)))
        {
            foreach ((Clip clip, BeatAnalysis beats) in found)
            {
                // Only the beats within what the clip plays, in its media's time.
                double from = clip.TrimmedRange().StartTime.Seconds, to = from + clip.Duration.Seconds;
                foreach (IEditCommand command in BeatCuts.Mark(clip, beats.Beats.Where(b => b >= from && b < to)))
                {
                    History.Execute(command);
                    marked++;
                }
            }
        }

        StatusText.Text = UiText.BeatsMarked(marked, found[0].Clip.Name, found[0].Found.Bpm);
    }

    /// <summary>Lays the selected clips, all on one track, from beat marker to beat marker.</summary>
    /// <param name="every">1 for every beat, 2 for every second, 4 for every fourth.</param>
    public void CutToBeats(int every)
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<LaneItem> chosen = [.. Chosen().Where(i => i.Item is Item).OrderBy(i => i.Start)];
        if (chosen.Count == 0 || chosen.Select(i => i.Lane).Distinct().Count() != 1)
        {
            StatusText.Text = UiText.CutToBeatsNeedsClips;
            return;
        }

        double[] beats = [.. TimelinePanel.Markers.Where(m => m.Marker.Name == BeatCuts.BeatName).Select(m => m.Start).Distinct().Order()];
        int laid = 0;
        if (Run(
            () =>
            {
                laid = BeatCuts.Cut(History, layout.Lanes[chosen[0].Lane].Track, [.. chosen.Select(i => (Item)i.Item)], beats, every);
                return [];
            },
            UiText.CutToBeatsMenu))
        {
            TimelinePanel.Selection.Clear();
            StatusText.Text = UiText.LaidOnBeats(laid);
        }
    }
}
