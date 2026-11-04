using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>WT54: a marker's editor changes its name, colour, range and note in one undoable step that replays from its JSON.</summary>
public sealed class MarkerCommandTests
{
    private static Timeline Premiere() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus", "premiere_example.otio")));

    [Fact]
    public void AnEditChangesEveryFieldAndUndoesThemAll()
    {
        Timeline timeline = Premiere();
        Marker marker = timeline.Tracks.Markers[0];
        string before = OtioWriter.Write(timeline);
        History history = new();
        TimeRange range = new(new RationalTime(120, 30), new RationalTime(15, 30));

        history.Execute(new EditMarker(marker, "pick up", "BLUE", range, "the line read is better"));

        Assert.Equal(("pick up", "BLUE", range, "the line read is better"), (marker.Name, marker.Color, marker.MarkedRange, marker.Comment));
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void AnEditIsRecordedAsJsonThatReplaysOnACopy()
    {
        Timeline timeline = Premiere();
        EditMarker edit = new(timeline.Tracks.Markers[1], "dsf2", "YELLOW", new TimeRange(new RationalTime(10, 30), new RationalTime(0, 30)), "note");
        new History().Execute(edit);
        JsonObject json = edit.ToJson(timeline);

        Timeline copy = Premiere();
        new History().Execute(CommandJson.Parse(copy, json));

        Assert.Equal(OtioWriter.Write(timeline), OtioWriter.Write(copy));
        Assert.Equal("edit_marker", (string?)json["op"]);
    }
}
