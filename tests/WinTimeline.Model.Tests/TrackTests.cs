using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>WT50: tracks are added, deleted, reordered and locked through the pipeline, and a lock holds.</summary>
public sealed class TrackTests
{
    private static Timeline Cut() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    private static (Timeline Cut, History History) Locked()
    {
        Timeline cut = Cut();
        History history = new() { Guard = TrackCommands.LockGuard(Cut()) };
        history.Guard = TrackCommands.LockGuard(cut);
        history.Execute(TrackState.Set((Track)cut.Tracks.Children[0], TrackState.LockedKey, true));
        return (cut, history);
    }

    [Fact]
    public void ATrackIsAddedMovedAndDeletedAndEachUndoes()
    {
        Timeline cut = Cut();
        string before = OtioWriter.Write(cut);
        History history = new();

        history.Execute(TrackCommands.Add(cut, 1, TrackKind.Video, "V2"));
        Assert.Equal(["V1", "V2", "A1"], cut.Tracks.Children.Select(t => t.Name));
        history.Execute(TrackCommands.Move(cut, (Track)cut.Tracks.Children[1], 0));
        Assert.Equal(["V2", "V1", "A1"], cut.Tracks.Children.Select(t => t.Name));
        history.Execute(TrackCommands.Delete(cut, (Track)cut.Tracks.Children[0]));
        Assert.Equal(["V1", "A1"], cut.Tracks.Children.Select(t => t.Name));

        while (history.Undo())
        {
        }

        Assert.Equal(before, OtioWriter.Write(cut));
    }

    [Fact]
    public void LockSoloAndHiddenAreUndoableMetadataThatOtherToolsCarry()
    {
        Timeline cut = Cut();
        History history = new();
        Track v1 = (Track)cut.Tracks.Children[0];

        history.Execute(TrackState.Set(v1, TrackState.LockedKey, true));
        history.Execute(TrackState.Set(v1, TrackState.SoloKey, true));
        Assert.True(TrackState.IsLocked(v1) && TrackState.IsSolo(v1) && !TrackState.IsHidden(v1));
        Assert.Contains("\"wintimeline:locked\": true", OtioWriter.Write(cut), StringComparison.Ordinal);

        history.Undo();
        history.Undo();
        Assert.False(TrackState.IsLocked(v1));
        Assert.DoesNotContain("wintimeline:", OtioWriter.Write(cut), StringComparison.Ordinal);
    }

    [Fact]
    public void ATrackColourIsUndoableMetadataThatTheJournalReplays()
    {
        Timeline cut = Cut();
        History history = new();
        Track v1 = (Track)cut.Tracks.Children[0];

        SetMetadata colour = TrackState.SetColor(v1, "BLUE");
        history.Execute(colour);
        Assert.Equal("BLUE", TrackState.ColorOf(v1));
        Assert.Contains("\"wintimeline:color\": \"BLUE\"", OtioWriter.Write(cut), StringComparison.Ordinal);

        history.Undo();
        Assert.Null(TrackState.ColorOf(v1));
        Assert.DoesNotContain("wintimeline:", OtioWriter.Write(cut), StringComparison.Ordinal);

        // The recovery journal writes the step and replays it on a fresh copy.
        Timeline fresh = Cut();
        CommandJson.Parse(fresh, colour.ToJson(cut)).Do();
        Assert.Equal("BLUE", TrackState.ColorOf((Track)fresh.Tracks.Children[0]));
    }

    [Fact]
    public void ALockedTrackRefusesTrimDropAndDeleteOfItsItems()
    {
        (Timeline cut, History history) = Locked();
        Track v1 = (Track)cut.Tracks.Children[0];
        string before = OtioWriter.Write(cut);

        Assert.Equal("V1 is locked", Assert.Throws<EditRefusedException>(() => history.Execute(new SetSourceRange((Item)v1.Children[0], new TimeRange(new RationalTime(0, 24), new RationalTime(12, 24))))).Message);
        Assert.Throws<EditRefusedException>(() => history.Execute(new RemoveChild(v1, 0)));
        Assert.Throws<EditRefusedException>(() => history.Execute(TrackCommands.Add(cut, 0, TrackKind.Video, "x") is var add ? new InsertChild(v1, 0, add.Child) : null!));
        Assert.Throws<EditRefusedException>(() => history.Execute(new MoveChild(v1, 0, (Track)cut.Tracks.Children[1], 0)));
        Assert.Throws<EditRefusedException>(() => history.Execute(TrackCommands.Delete(cut, v1)));
        Assert.Equal(before, OtioWriter.Write(cut));
    }

    [Fact]
    public void ALockedTrackStillTakesItsOwnRenameAndUnlockAndOtherTracksStayEditable()
    {
        (Timeline cut, History history) = Locked();
        Track v1 = (Track)cut.Tracks.Children[0];

        history.Execute(new Rename(v1, "Picture"));
        history.Execute(new SetEnabled((Item)((Track)cut.Tracks.Children[1]).Children[0], false));
        history.Execute(TrackState.Set(v1, TrackState.LockedKey, false));
        history.Execute(new RemoveChild(v1, 0));

        Assert.Equal("Picture", v1.Name);
        Assert.Equal(5, v1.Children.Count);
    }
}
