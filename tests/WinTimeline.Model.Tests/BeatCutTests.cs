using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT121: beats marked on a clip, and clips laid from beat to beat, trimmed to fit, in one undo step.</summary>
public sealed class BeatCutTests
{
    private static Timeline Cut() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    [Fact]
    public void BeatsAreYellowMarkersInTheClipsOwnMedia()
    {
        Timeline timeline = Cut();
        Item a1 = (Item)timeline.AudioTracks.First().Children[0];

        History history = new();
        using (history.BeginTransaction("Mark beats"))
        {
            foreach (IEditCommand command in BeatCuts.Mark(a1, [0.25, 0.75]))
            {
                history.Execute(command);
            }
        }

        Assert.Equal([0.25, 0.75], a1.Markers.Select(m => m.MarkedRange.StartTime.Seconds));
        Assert.All(a1.Markers, m => Assert.Equal(("beat", "YELLOW"), (m.Name, m.Color)));
    }

    [Fact]
    public void ClipsAreLaidFromBeatToBeatTrimmedToEachGapAndOneUndoPutsThemBack()
    {
        Timeline timeline = Cut();
        Track v1 = timeline.VideoTracks.First();
        string before = OtioWriter.Write(timeline);
        History history = new();
        List<Item> clips = [(Item)v1.Children[0], (Item)v1.Children[1], (Item)v1.Children[2]];

        // Beats every second from 1; every second beat is 1, 3, 5: c1 and c2 (2 s each) fill two
        // gaps of 2 s, and c3 has no beat after 5 to end on, so it stays lifted.
        int laid;
        using (history.BeginTransaction("Cut to beats"))
        {
            laid = BeatCuts.Cut(history, v1, clips, [1, 2, 3, 4, 5], every: 2);
        }

        Assert.Equal(2, laid);
        Dictionary<string, (double, double)> placed = TrackEdits.Placed(v1)
            .Where(p => v1.Children[p.Index] is Clip)
            .ToDictionary(p => v1.Children[p.Index].Name + "@" + p.Start.ToString(System.Globalization.CultureInfo.InvariantCulture), p => (p.Start, p.End));
        Assert.Equal((1, 3), placed["c1@1"]);
        Assert.Equal((3, 5), placed["c2@3"]);

        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void FewerThanTwoBeatsIsRefused()
    {
        Timeline timeline = Cut();
        Track v1 = timeline.VideoTracks.First();

        Assert.Throws<EditRefusedException>(() => BeatCuts.Cut(new History(), v1, [(Item)v1.Children[0]], [1, 2], every: 4));
    }
}
