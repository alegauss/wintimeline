using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT160: clips copied as an OTIO fragment and pasted at a cut, in one undo step. cut_v1's V1
/// holds c1 to c7 cut at 0, 2, 4, 5, 6.5, 7.5 and 8.5 seconds, its A1 one clip from 0 to 5.
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

    private static string[] Names(Track track) => [.. track.Children.Select(c => c is Gap ? $"gap {c.Duration.Seconds}" : c.Name)];

    private static void Run(History history, PastePlan plan)
    {
        using History.TransactionScope paste = history.BeginTransaction("Paste");
        foreach (IEditCommand command in plan.Commands)
        {
            history.Execute(command);
        }
    }

    [Fact]
    public void TwoClipsApartAreCopiedWithTheGapBetweenThemAndPastedAtTheCutAtThePlayhead()
    {
        Timeline timeline = Cut();
        Track v1 = V1(timeline);
        string fragment = ClipFragment.Copy(timeline, [v1.Children[3], v1.Children[1]], documentFolder: null)!;
        History history = new();
        string before = OtioWriter.Write(timeline);

        PastePlan plan = ClipFragment.Plan(timeline, fragment, 3.0);
        Run(history, plan);

        // The playhead at 3 is inside c2, so the paste goes in at the cut at 4, before c3.
        Assert.Equal(["c1", "c2", "c2", "gap 1", "c4", "c3", "c4", "c5", "c7"], Names(V1(timeline)));
        Assert.Equal(2, plan.Items);
        Assert.Equal(["V1"], plan.Lanes);
        Assert.Equal(13, timeline.Duration.Seconds, 6);

        Assert.True(history.Undo());
        Assert.Equal(before, OtioWriter.Write(timeline));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void LanesKeepTheirOffsetsAndAPasteBeyondALaneEndPadsItWithAGap()
    {
        Timeline source = Cut();
        string fragment = ClipFragment.Copy(source, [V1(source).Children[2], source.AudioTracks.First().Children[0]], documentFolder: null)!;
        Timeline target = TimelineBuilder.Empty("empty", 24);
        History history = new();

        Run(history, ClipFragment.Plan(target, fragment, 3.0));

        // c3 starts at 4 and a1 at 0, so on its own lane c3 keeps the four seconds between them.
        Assert.Equal(["gap 3", "gap 4", "c3"], Names(target.VideoTracks.First()));
        Assert.Equal(["gap 3", "a1"], Names(target.AudioTracks.First()));
    }

    [Fact]
    public void ARelativeMediaUrlIsMadeAbsoluteAgainstTheCopiedDocumentsFolder()
    {
        Timeline timeline = Cut(text => text.Replace("file:///media/A001.mov", "media/A001.mov", StringComparison.Ordinal));
        string folder = Path.Combine(Path.GetTempPath(), "wintimeline-cut");
        string fragment = ClipFragment.Copy(timeline, [V1(timeline).Children[0]], folder)!;
        Timeline target = TimelineBuilder.Empty("other", 24);

        Run(new History(), ClipFragment.Plan(target, fragment, 0));

        Clip pasted = target.FindClips().Single();
        Assert.Equal(FileUrl.FromPath(Path.Combine(folder, "media", "A001.mov")), Assert.IsType<ExternalReference>(pasted.MediaReference).TargetUrl);
    }

    [Fact]
    public void TextThatIsNoFragmentOrNamesALaneTheTimelineLacksIsRefused()
    {
        Timeline timeline = Cut();
        string fragment = ClipFragment.Copy(timeline, [V1(timeline).Children[0]], documentFolder: null)!;

        Assert.Null(ClipFragment.Copy(timeline, [], documentFolder: null));
        Assert.Contains("no clips", Assert.Throws<EditRefusedException>(() => ClipFragment.Plan(timeline, "{}", 0)).Message, StringComparison.Ordinal);
        Assert.Contains("no clips", Assert.Throws<EditRefusedException>(() => ClipFragment.Plan(timeline, OtioWriter.Write(timeline), 0)).Message, StringComparison.Ordinal);
        string elsewhere = fragment.Replace("\"name\":\"V1\"", "\"name\":\"V4\"", StringComparison.Ordinal);
        Assert.Equal("cut v1 has no V4 to paste onto.", Assert.Throws<EditRefusedException>(() => ClipFragment.Plan(timeline, elsewhere, 0)).Message);
    }
}
