using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT71: a split plays exactly the frames the clip played, its markers stay on the half they fall
/// on, and a split inside a transition is refused.
/// </summary>
public sealed class SplitTests
{
    private static Timeline Read(params string[] path) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine([Repository.Root, "tests", "fixtures", .. path])));

    /// <summary>For each frame of a track, which media and which of its frames plays there.</summary>
    private static List<string> Frames(Track track, double rate)
    {
        List<string> frames = [];
        foreach ((int index, double start, double end) in TrackEdits.Placed(track))
        {
            if (track.Children[index] is not Item item)
            {
                continue;
            }

            string media = item is Clip { MediaReference: ExternalReference external } ? external.TargetUrl : item.Name;
            double from = item.TrimmedRange().StartTime.Seconds;
            for (long f = (long)Math.Round(start * rate); f < (long)Math.Round(end * rate); f++)
            {
                frames.Add($"{media}@{Math.Round((from + (f / rate) - start) * rate)}");
            }
        }

        return frames;
    }

    [Fact]
    public void SplittingEveryTenthFramePlaysTheSameFramesAndUndoesToTheFile()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        string before = OtioWriter.Write(timeline);
        List<string> played = Frames(v1, 24);
        History history = new();

        using (history.BeginTransaction("Split"))
        {
            for (int frame = 10; frame < 204; frame += 10)
            {
                TrackEdits.SplitAt(history, v1, frame / 24.0);
            }
        }

        Assert.Equal(played, Frames(v1, 24));
        Assert.True(v1.Children.Count > 20);
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void AMarkerStaysOnTheHalfItFallsOn()
    {
        // Premiere's sc01_sh030_anim.mov starts at 10.733 s on V2 and plays its media from 0; its marker is 2.433 s in.
        Timeline early = Read("otio", "corpus", "premiere_example.otio");
        Track v2 = early.VideoTracks.ElementAt(1);
        int clip = v2.Children.ToList().FindIndex(c => c.Name == "sc01_sh030_anim.mov");

        TrackEdits.SplitAt(new History(), v2, 12.0);
        Assert.Empty(((Item)v2.Children[clip]).Markers);
        Assert.Single(((Item)v2.Children[clip + 1]).Markers);

        Timeline late = Read("otio", "corpus", "premiere_example.otio");
        Track later = late.VideoTracks.ElementAt(1);
        TrackEdits.SplitAt(new History(), later, 14.0);
        Assert.Single(((Item)later.Children[clip]).Markers);
        Assert.Empty(((Item)later.Children[clip + 1]).Markers);
    }

    [Fact]
    public void ASplitInsideATransitionIsRefusedAndOneBesideItIsNot()
    {
        // transition_test's t1 overlaps A and B from 1.667 to 2.5 seconds; A runs to 2.083.
        Timeline timeline = Read("otio", "corpus", "transition_test.otio");
        Track track = timeline.VideoTracks.First();
        string before = OtioWriter.Write(timeline);

        EditRefusedException refused = Assert.Throws<EditRefusedException>(() => TrackEdits.SplitAt(new History(), track, 2.0));

        Assert.Contains("transition", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, OtioWriter.Write(timeline));
        TrackEdits.SplitAt(new History(), track, 3.0);
        Assert.Equal(["t0", "A", "t1", "B", "B", "C", "t3"], track.Children.Select(c => c.Name));
    }

    [Fact]
    public void ASplitAtACutOrPastTheEndDoesNothing()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        string before = OtioWriter.Write(timeline);

        TrackEdits.SplitAt(new History(), v1, 2.0);
        TrackEdits.SplitAt(new History(), v1, 30.0);

        Assert.Equal(before, OtioWriter.Write(timeline));
    }
}
