using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT160 and WT78: clips copied as OTIO and pasted at the playhead by overwrite or insert, in one
/// undo step, and OTIO text from another tool pasted the same way. cut_v1's V1 holds c1 to c7 cut
/// at 0, 2, 4, 5, 6.5, 7.5 and 8.5 seconds, its A1 one clip from 0 to 5.
/// </summary>
public sealed class ClipFragmentTests
{
    private static readonly string CutV1 = Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio");

    private static Timeline Cut(Func<string, string>? edit = null)
    {
        string text = File.ReadAllText(CutV1);
        return Assert.IsType<Timeline>(OtioReader.ReadString(edit is null ? text : edit(text)));
    }

    private static Track V1(Timeline timeline) => timeline.VideoTracks.First();

    private static string[] Shape(Track track) =>
        [.. track.Children.Select(c => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(c is Gap ? "gap" : c.Name)} {c.Duration.Seconds:0.###}"))];

    private static PastePlan Paste(History history, Timeline timeline, string text, double seconds, bool insert = false)
    {
        using History.TransactionScope scope = history.BeginTransaction("Paste");
        return ClipFragment.Paste(history, timeline, text, seconds, insert);
    }

    [Fact]
    public void AnOverwritePasteLaysEachClipAtItsOffsetOverWhatItCoversAndPassesOverTheGapBetween()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string fragment = ClipFragment.Copy(timeline, [v1.Children[3], v1.Children[1]], documentFolder: null)!;
        History history = new();
        string before = OtioWriter.Write(timeline);

        // c2 (2 s) and c4 (1.5 s) with the second between them; at 3 they cover 3 to 5 and 6 to 7.5.
        PastePlan pasted = Paste(history, timeline, fragment, 3.0);

        Assert.Equal(["c1 2", "c2 1", "c2 2", "c4 1", "c4 1.5", "c7 1"], Shape(v1));
        Assert.Equal(2, pasted.Items);
        Assert.Equal(["V1"], pasted.Lanes);
        Assert.True(history.Undo());
        Assert.Equal(before, OtioWriter.Write(timeline));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AnInsertPasteCutsTheClipAtThePlayheadAndPushesEverythingAfterIt()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string fragment = ClipFragment.Copy(timeline, [v1.Children[3], v1.Children[1]], documentFolder: null)!;

        Paste(new History(), timeline, fragment, 3.0, insert: true);

        Assert.Equal(["c1 2", "c2 1", "c2 2", "gap 1", "c4 1.5", "c2 1", "c3 1", "c4 1.5", "c5 1", "c7 1"], Shape(v1));
        Assert.Equal(13, timeline.Duration.Seconds, 6);
    }

    [Fact]
    public void LanesKeepTheirOffsetsAndAPasteBeyondALaneEndPadsItWithAGap()
    {
        Timeline source = Cut();
        string fragment = ClipFragment.Copy(source, [V1(source).Children[2], source.AudioTracks.First().Children[0]], documentFolder: null)!;
        Timeline target = TimelineBuilder.Empty("empty", 24);

        Paste(new History(), target, fragment, 3.0);

        // c3 starts four seconds after a1, so pasted at 3 it lands at 7.
        Assert.Equal(["gap 7", "c3 1"], Shape(target.VideoTracks.First()));
        Assert.Equal(["gap 3", "a1 5"], Shape(target.AudioTracks.First()));
    }

    [Fact]
    public void ARelativeMediaUrlIsMadeAbsoluteAgainstTheCopiedDocumentsFolder()
    {
        Timeline timeline = Cut(text => text.Replace("file:///media/A001.mov", "media/A001.mov", StringComparison.Ordinal));
        string folder = Path.Combine(Path.GetTempPath(), "wintimeline-cut");
        string fragment = ClipFragment.Copy(timeline, [V1(timeline).Children[0]], folder)!;
        Timeline target = TimelineBuilder.Empty("other", 24);

        Paste(new History(), target, fragment, 0);

        Clip pasted = target.FindClips().Single();
        Assert.Equal(FileUrl.FromPath(Path.Combine(folder, "media", "A001.mov")), Assert.IsType<ExternalReference>(pasted.MediaReference).TargetUrl);
    }

    [Fact]
    public void TheClipboardTextRoundTripsToIdenticalItemsAsNotepadWouldHandItBack()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string fragment = ClipFragment.Copy(timeline, [v1.Children[0], v1.Children[1]], documentFolder: null)!;

        // What a text editor would hand back: the same characters, saved and copied again.
        var lanes = ClipFragment.Read(new string(fragment.AsSpan()))!;

        Assert.Equal([OtioWriter.Write(v1.Children[0]), OtioWriter.Write(v1.Children[1])], lanes.Single().Items.Select(i => OtioWriter.Write(i)));
    }

    [Fact]
    public void OtioFromAnotherToolPastesATimelineOnItsLanesAndAClipOnV1()
    {
        Timeline source = Cut();
        Timeline target = TimelineBuilder.Empty("other", 24);

        Paste(new History(), target, OtioWriter.Write(source), 0);
        Assert.Equal(["c1", "c2", "c3", "c4", "c5", "c7"], V1(target).Children.Select(c => c.Name));
        Assert.Equal(["a1"], target.AudioTracks.First().Children.Select(c => c.Name));

        Timeline other = TimelineBuilder.Empty("third", 24);
        Paste(new History(), other, OtioWriter.Write(V1(source).Children[2]), 1);
        Assert.Equal(["gap 1", "c3 1"], Shape(V1(other)));
    }

    [Fact]
    public void TextThatIsNoOtioOrNamesALaneTheTimelineLacksIsRefused()
    {
        Timeline timeline = Cut();
        string fragment = ClipFragment.Copy(timeline, [V1(timeline).Children[0]], documentFolder: null)!;

        Assert.Null(ClipFragment.Copy(timeline, [], documentFolder: null));
        Assert.Contains("no clips", Assert.Throws<EditRefusedException>(() => Paste(new History(), timeline, "not json", 0)).Message, StringComparison.Ordinal);
        Assert.Contains("no clips", Assert.Throws<EditRefusedException>(() => Paste(new History(), timeline, "{}", 0)).Message, StringComparison.Ordinal);
        string elsewhere = fragment.Replace("\"name\":\"V1\"", "\"name\":\"V4\"", StringComparison.Ordinal);
        Assert.Equal("cut v1 has no V4 to paste onto.", Assert.Throws<EditRefusedException>(() => Paste(new History(), timeline, elsewhere, 0)).Message);
    }
}
