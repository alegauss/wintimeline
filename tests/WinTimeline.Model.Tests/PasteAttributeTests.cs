using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT78: a clip's effects, markers, colour and metadata pasted onto others, chosen one by one, in one undo step that replays from its JSON.</summary>
public sealed class PasteAttributeTests
{
    private static Timeline Read(params string[] path) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine([Repository.Root, "tests", "fixtures", .. path])));

    private static Item Marked(Timeline premiere) =>
        (Item)premiere.VideoTracks.ElementAt(1).Children.First(c => c.Name == "sc01_sh030_anim.mov");

    [Fact]
    public void OnlyTheChosenAttributesArePastedAndOneUndoTakesThemAllBack()
    {
        Timeline timeline = Read("otio", "corpus", "premiere_example.otio");
        Item from = Marked(timeline);
        History history = new();
        history.Execute(new SetColor(from, new Color(1, 0, 0, 1, "red")));
        history.Execute(new SetMetadata(from, "reviewed", true));
        List<Item> onto = [.. timeline.FindClips().Where(c => !ReferenceEquals(c, from)).Take(2)];
        string before = OtioWriter.Write(timeline);

        int changed;
        using (history.BeginTransaction("Paste attributes"))
        {
            changed = PasteAttributes.Apply(history, from, onto, ClipAttributes.Color | ClipAttributes.Metadata);
        }

        Assert.Equal(2, changed);
        Assert.All(onto, i => Assert.Equal("red", i.Color?.Name));
        Assert.All(onto, i => Assert.Equal(true, i.Metadata["reviewed"]));
        Assert.All(onto, i => Assert.Empty(i.Markers));
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void MarkersLandAtTheSameOffsetIntoTheTargetAndEffectsAreCopied()
    {
        Timeline timeline = Read("otio", "corpus", "premiere_example.otio");
        Item from = Marked(timeline);
        Clip onto = timeline.VideoTracks.First().Children.OfType<Clip>().First(c => c.Duration.Seconds > 3);
        History history = new();

        PasteAttributes.Apply(history, from, [onto], ClipAttributes.Markers | ClipAttributes.Effects);

        // The copied clip's marker is 2.433 s into what it plays; on the target it is 2.433 s into its own.
        Marker marker = Assert.Single(onto.Markers);
        Assert.Equal(2.4333, Math.Round(marker.MarkedRange.StartTime.Seconds - onto.TrimmedRange().StartTime.Seconds, 4));
        Assert.Equal(from.Effects.Count, onto.Effects.Count);
    }

    [Fact]
    public void AnEffectAndAColourAreRecordedAsJsonThatReplaysOnACopy()
    {
        Timeline effects = Read("otio", "corpus", "effects.otio");
        Item withEffect = effects.FindClips().First(c => c.Effects.Count > 0);
        Timeline timeline = Read("diff", "cut_v1.otio");
        Item target = (Item)timeline.VideoTracks.First().Children[0];
        AddEffect add = new(target, (Effect)OtioReader.ReadString(OtioWriter.Write(withEffect.Effects[0])));
        SetColor colour = new(target, new Color(0, 0, 1, 1, "blue"));
        History history = new();
        history.Execute(add);
        history.Execute(colour);
        JsonObject addJson = add.ToJson(timeline), colourJson = colour.ToJson(timeline);

        Timeline copy = Read("diff", "cut_v1.otio");
        History replay = new();
        replay.Execute(CommandJson.Parse(copy, addJson));
        replay.Execute(CommandJson.Parse(copy, colourJson));

        Assert.Equal(OtioWriter.Write(timeline), OtioWriter.Write(copy));
    }
}
