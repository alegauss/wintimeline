using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT81: angles lined up in a plain nested stack showing one of them, and cut to another at the playhead.</summary>
public sealed class MulticamTests
{
    private static Timeline Cut() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    [Fact]
    public void AnglesSyncedByInPointsAreOneTrackEachWithOnlyTheFirstShown()
    {
        Timeline timeline = Cut();
        Track v1 = timeline.VideoTracks.First();

        Stack multicam = Multicam.Create([(Item)v1.Children[0], (Item)v1.Children[1]], "Multicam 1", MulticamSync.InPoints);

        Assert.Equal(0, Multicam.AngleOf(multicam));
        Assert.Equal(["Angle 1", "Angle 2"], multicam.Children.Select(c => c.Name));
        Assert.Equal([true, false], multicam.Children.OfType<Track>().Select(t => t.Enabled));
        Assert.Equal(2, multicam.Duration.Seconds, 6);
        Assert.Throws<EditRefusedException>(() => Multicam.Create([(Item)v1.Children[0]], "one", MulticamSync.InPoints));
    }

    [Fact]
    public void AnglesSyncedByTimecodeLeadTheLaterOneByTheGapBetweenTheirMediaStarts()
    {
        Timeline timeline = Cut();
        Track v1 = timeline.VideoTracks.First();
        Item c1 = (Item)v1.Children[0], c2 = (Item)v1.Children[1];
        double gap = Math.Abs(c2.TrimmedRange().StartTime.Seconds - c1.TrimmedRange().StartTime.Seconds);

        Stack multicam = Multicam.Create([c1, c2], "Multicam 1", MulticamSync.Timecode);

        double lead = multicam.Children.OfType<Track>().Max(t => t.Children.OfType<Gap>().Sum(g => g.Duration.Seconds));
        Assert.Equal(gap, lead, 3);
    }

    [Fact]
    public void CuttingToAnotherAngleSplitsAtThePlayheadAndShowsItFromThereAndUndoesExactly()
    {
        Timeline timeline = Cut();
        Track v1 = timeline.VideoTracks.First();
        History history = new();
        Stack multicam = Multicam.Create([(Item)v1.Children[0], (Item)v1.Children[1]], "Multicam 1", MulticamSync.InPoints);
        using (history.BeginTransaction("Create multicam"))
        {
            TrackEdits.Overwrite(history, v1, 0, [multicam]);
        }

        string before = OtioWriter.Write(timeline);

        bool cut;
        using (history.BeginTransaction("Cut to angle 2"))
        {
            cut = Multicam.CutTo(history, v1, 1.0, 1);
        }

        Assert.True(cut);
        Stack[] segments = [.. v1.Children.OfType<Stack>()];
        Assert.Equal([0, 1], segments.Select(s => Multicam.AngleOf(s)!.Value));
        Assert.Equal([1.0, 1.0], segments.Select(s => s.Duration.Seconds));
        Assert.Equal([false, true], segments[1].Children.OfType<Track>().Select(t => t.Enabled));
        Assert.False(Multicam.CutTo(history, v1, 1.5, 1));
        Assert.Throws<EditRefusedException>(() => Multicam.CutTo(history, v1, 0.5, 8));

        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }
}
