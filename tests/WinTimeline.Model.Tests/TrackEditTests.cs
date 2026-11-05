using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT70: overwrite, insert, lift and a move on cut_v1, whose V1 holds c1 to c7 cut at 0, 2, 4,
/// 5, 6.5, 7.5 and 8.5 seconds. Every gesture is one transaction, and its undo gives back the file.
/// </summary>
public sealed class TrackEditTests
{
    private static Timeline Cut() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    private static Track V1(Timeline timeline) => timeline.VideoTracks.First();

    private static string[] Shape(Track track) =>
        [.. track.Children.Select(c => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(c is Gap ? "gap" : c.Name)} {c.Duration.Seconds:0.###}"))];

    private static void Gesture(History history, Action edit)
    {
        using History.TransactionScope scope = history.BeginTransaction("Move");
        edit();
    }

    [Fact]
    public void AnOverwriteReplacesWhatItCoversSplittingTheClipItLandsInAndUndoesExactly()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string before = OtioWriter.Write(timeline);
        History history = new();
        string c2Source = ((Item)v1.Children[1]).TrimmedRange().StartTime.Seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        Gesture(history, () => TrackEdits.Overwrite(history, v1, 3.0, [TrackEdits.Duplicate(v1.Children[0])]));

        Assert.Equal(["c1 2", "c2 1", "c1 2", "c4 1.5", "c5 1", "c7 1"], Shape(v1));
        Assert.Equal(c2Source, ((Item)v1.Children[1]).TrimmedRange().StartTime.Seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(8.5, TrackEdits.End(v1), 6);
        Assert.True(history.Undo());
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void AnInsertPushesEverythingAfterItLaterAndTheSecondHalfPlaysOnFromWhereTheFirstStopped()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        History history = new();
        double c2From = ((Item)v1.Children[1]).TrimmedRange().StartTime.Seconds;

        Gesture(history, () => TrackEdits.Insert(history, v1, 3.0, [TrackEdits.Duplicate(v1.Children[0])]));

        Assert.Equal(["c1 2", "c2 1", "c1 2", "c2 1", "c3 1", "c4 1.5", "c5 1", "c7 1"], Shape(v1));
        Assert.Equal(c2From + 1, ((Item)v1.Children[3]).TrimmedRange().StartTime.Seconds, 6);
        Assert.Equal(10.5, TrackEdits.End(v1), 6);
    }

    [Fact]
    public void ALiftLeavesAGapSoNothingMovesAndAMoveIsALiftThenAnOverwrite()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string before = OtioWriter.Write(timeline);
        History history = new();

        // c2 (2 to 4) moved by overwrite to 6: its place becomes a gap, and it covers c5 and half of c4's tail.
        Gesture(history, () =>
        {
            Composable c2 = TrackEdits.Lift(history, v1, v1.Children[1]);
            TrackEdits.Overwrite(history, v1, 6.0, [c2]);
        });

        Assert.Equal(["c1 2", "gap 2", "c3 1", "c4 1", "c2 2", "c7 0.5"], Shape(v1));
        Assert.True(history.Undo());
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void LiftingTheLastClipLeavesNoGapAtTheEndAndAnOverwritePastTheEndPadsUpToIt()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        History history = new();

        Gesture(history, () =>
        {
            Composable c7 = TrackEdits.Lift(history, v1, v1.Children[5]);
            TrackEdits.Overwrite(history, v1, 10.0, [c7]);
        });

        Assert.Equal(["c1 2", "c2 2", "c3 1", "c4 1.5", "c5 1", "gap 2.5", "c7 1"], Shape(v1));
    }
}
