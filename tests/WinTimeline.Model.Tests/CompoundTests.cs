using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT56: a run of clips nested into a compound plays exactly as before, and flattening it gives
/// back the very file it came from. cut_v1's V1 holds c1 to c7 cut at 0, 2, 4, 5, 6.5, 7.5 and 8.5 seconds.
/// </summary>
public sealed class CompoundTests
{
    private static Timeline Read(params string[] path) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine([Repository.Root, "tests", "fixtures", .. path])));

    private static void Run(History history, IReadOnlyList<IEditCommand> commands, string step)
    {
        using History.TransactionScope scope = history.BeginTransaction(step);
        foreach (IEditCommand command in commands)
        {
            history.Execute(command);
        }
    }

    /// <summary>Where each clip plays on the timeline, from its parents' ranges, by name.</summary>
    private static Dictionary<string, (double Start, double Duration)> Program(Timeline timeline) =>
        timeline.FindClips().ToDictionary(c => c.Name, c => (Math.Round(Start(timeline, c), 9), Math.Round(c.Duration.Seconds, 9)));

    private static double Start(Timeline timeline, Composable item)
    {
        if (ReferenceEquals(item, timeline.Tracks) || item.Parent is not { } parent)
        {
            return 0;
        }

        double inParent = parent.RangeOfChildAtIndex(parent.Children.ToList().IndexOf(item)).StartTime.Seconds - parent.TrimmedRange().StartTime.Seconds;
        return Start(timeline, parent) + inParent;
    }

    [Fact]
    public void TwoClipsNestedPlayWhereTheyDidAndBreakingApartGivesBackTheFile()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        string before = OtioWriter.Write(timeline);
        var program = Program(timeline);
        History history = new();

        Run(history, Compound.Make(v1, 1, 2, "Compound 1"), "Make compound");

        Assert.Equal(["c1", "Compound 1", "c4", "c5", "c7"], v1.Children.Select(c => c.Name));
        Stack compound = Assert.IsType<Stack>(v1.Children[1]);
        Assert.Equal(["c2", "c3"], Assert.IsType<Track>(Assert.Single(compound.Children)).Children.Select(c => c.Name));
        Assert.Equal(3, compound.Duration.Seconds);
        Assert.Equal(program, Program(timeline));

        Run(history, Compound.BreakApart(v1, 1), "Break apart");

        Assert.Equal(before, OtioWriter.Write(timeline));
        history.Undo();
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void ARunEndingOnATransitionOrHoldingOnlyGapsIsRefused()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        Timeline empty = TimelineBuilder.FromMedia("gaps", 24, [new MediaClip("tone", "file:///tone.wav", 2, null, HasAudio: true)]);

        Assert.Throws<EditRefusedException>(() => Compound.Make(empty.VideoTracks.First(), 0, 0, "Compound 1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Compound.Make(v1, 3, 9, "Compound 1"));
        Assert.Throws<EditRefusedException>(() => Compound.BreakApart(v1, 0));
    }

    [Fact]
    public void ATrimmedCompoundIsNotFlattenedUntilTrimmingExists()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        History history = new();
        Run(history, Compound.Make(v1, 1, 2, "Compound 1"), "Make compound");
        Stack compound = (Stack)v1.Children[1];
        history.Execute(new SetSourceRange(compound, new Time.TimeRange(new Time.RationalTime(12, 24), new Time.RationalTime(48, 24))));

        EditRefusedException refused = Assert.Throws<EditRefusedException>(() => Compound.BreakApart(v1, 1));

        Assert.Equal("Compound 1 is trimmed; breaking apart a trimmed compound waits on trimming.", refused.Message);
    }

    [Fact]
    public void ACompoundCarryingItsOwnMarkerIsNotFlattenedSoTheMarkerIsNotLost()
    {
        Timeline timeline = Read("diff", "cut_v1.otio");
        Track v1 = timeline.VideoTracks.First();
        History history = new();
        Run(history, Compound.Make(v1, 1, 2, "Compound 1"), "Make compound");
        history.Execute(new AddMarker((Stack)v1.Children[1], "note", new Time.TimeRange(new Time.RationalTime(0, 24), new Time.RationalTime(0, 24))));

        EditRefusedException refused = Assert.Throws<EditRefusedException>(() => Compound.BreakApart(v1, 1));

        Assert.Equal("Compound 1 carries markers or effects of its own, which breaking it apart would lose.", refused.Message);
    }

    [Fact]
    public void FinalCutsCompoundOfSeveralTracksIsNotFlattenedOntoOne()
    {
        Timeline timeline = Read("fcpx", "expected", "fcpx_project.otio");
        (Track track, int index, Stack stack) = timeline.Tracks.Children.OfType<Track>()
            .SelectMany(t => t.Children.Select((c, i) => (t, i, c)))
            .Where(p => p.c is Stack { Name: "compound_clip_1" })
            .Select(p => (p.t, p.i, (Stack)p.c))
            .First();

        EditRefusedException refused = Assert.Throws<EditRefusedException>(() => Compound.BreakApart(track, index));

        Assert.Equal($"compound_clip_1 holds {stack.Children.Count} tracks; only a compound of one track flattens onto a track.", refused.Message);
    }
}
