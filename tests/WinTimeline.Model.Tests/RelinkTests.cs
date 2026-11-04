using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Media;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT18: media that moved is found from one choice, and relinking is an edit that undoes.</summary>
public sealed class RelinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wt18-" + Guid.NewGuid().ToString("N"));

    public RelinkTests()
    {
        foreach (string file in (string[])[@"shoot\day1\a.mov", @"shoot\day1\b.wav", @"shoot\day2\c.mov", @"elsewhere\d.mov"])
        {
            string path = Path.Combine(_root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AMovedMediaFolderRelinksCompletelyFromOneChoiceAndUndoesInOneStep()
    {
        // The media lived under old\shoot; it is now under shoot. One clip is on the web.
        string old = Path.Combine(_root, "old");
        Timeline timeline = TimelineWith(
            FileUrl.FromPath(Path.Combine(old, @"shoot\day1\a.mov")),
            FileUrl.FromPath(Path.Combine(old, @"shoot\day1\b.wav")),
            FileUrl.FromPath(Path.Combine(old, @"shoot\day2\c.mov")),
            "https://example.com/web.mov");
        string original = OtioWriter.Write(timeline);
        IReadOnlyList<MissingMedia> missing = MediaLinks.Missing(timeline, null);
        Assert.Equal(4, missing.Count);
        Assert.Null(missing[3].Expected);

        RelinkPlan plan = RelinkPlan.From(missing, missing[0], Path.Combine(_root, @"shoot\day1\a.mov"));

        Assert.Equal(new FolderSubstitution(old, _root), plan.Rule);
        Assert.Equal(3, plan.Found.Count);
        Assert.Equal("https://example.com/web.mov", ((ExternalReference)Assert.Single(plan.StillMissing).Reference).TargetUrl);

        History history = new();
        using (history.BeginTransaction("Relink"))
        {
            foreach ((MissingMedia m, string file) in plan.Found)
            {
                history.Execute(new Relink(m.Clip, file));
            }
        }

        Assert.Single(MediaLinks.Missing(timeline, null));
        history.Undo();
        Assert.Equal(original, OtioWriter.Write(timeline));
    }

    [Fact]
    public void WhereNoRuleFitsAFileIsFoundByNameNearThePick()
    {
        // d.mov's old folder has nothing in common with where it is now, but it sits beside the pick.
        File.Copy(Path.Combine(_root, @"elsewhere\d.mov"), Path.Combine(_root, @"shoot\day1\d.mov"));
        Timeline timeline = TimelineWith(
            FileUrl.FromPath(@"Z:\gone\day1\a.mov"),
            FileUrl.FromPath(@"Y:\other\place\d.mov"));
        IReadOnlyList<MissingMedia> missing = MediaLinks.Missing(timeline, null);

        RelinkPlan plan = RelinkPlan.From(missing, missing[0], Path.Combine(_root, @"shoot\day1\a.mov"));

        Assert.Empty(plan.StillMissing);
        Assert.Equal(Path.Combine(_root, @"shoot\day1\d.mov"), plan.Found[1].File);
    }

    [Theory]
    [InlineData(@"D:\media\shoot\day1\A001.mov", @"E:\shoot\day1\A001.mov", @"D:\media", @"E:\")]
    [InlineData(@"D:\proj\a.mov", @"\\nas\proj\a.mov", @"D:\", @"\\nas")]
    [InlineData(@"D:\x\a.mov", @"D:\y\b.mov", null, null)]
    public void OnePickImpliesTheFolderThatMoved(string expected, string found, string? from, string? to)
    {
        FolderSubstitution? rule = FolderSubstitution.Infer(expected, found);

        Assert.Equal(from, rule?.From);
        Assert.Equal(to, rule?.To);
    }

    [Fact]
    public void ARelativeReferenceIsFoundBesideTheDocument()
    {
        Timeline timeline = TimelineWith("shoot/day1/a.mov", "shoot/day1/missing.mov");

        IReadOnlyList<MissingMedia> missing = MediaLinks.Missing(timeline, _root);

        Assert.Equal(Path.Combine(_root, @"shoot\day1\missing.mov"), Assert.Single(missing).Expected);
        Assert.Equal(2, MediaLinks.Missing(timeline, null).Count);
    }

    [Fact]
    public void ARelinkIsRecordedAndReplayedAsJson()
    {
        Timeline timeline = TimelineWith(FileUrl.FromPath(@"Z:\gone\a.mov"));
        Clip clip = timeline.FindClips().Single();
        Relink relink = new(clip, Path.Combine(_root, @"shoot\day1\a.mov"));

        IEditCommand replayed = CommandJson.Parse(timeline, relink.ToJson(timeline));
        replayed.Do();

        Assert.Equal(FileUrl.FromPath(Path.Combine(_root, @"shoot\day1\a.mov")), ((ExternalReference)clip.MediaReference!).TargetUrl);
    }

    private static Timeline TimelineWith(params string[] urls)
    {
        string clips = string.Join(",", urls.Select((url, i) => $$"""
            { "OTIO_SCHEMA": "Clip.2", "name": "c{{i}}",
              "source_range": { "OTIO_SCHEMA": "TimeRange.1",
                "start_time": { "OTIO_SCHEMA": "RationalTime.1", "rate": 24.0, "value": 0.0 },
                "duration": { "OTIO_SCHEMA": "RationalTime.1", "rate": 24.0, "value": 24.0 } },
              "media_references": { "DEFAULT_MEDIA": { "OTIO_SCHEMA": "ExternalReference.1", "target_url": "{{url.Replace("\\", "\\\\", StringComparison.Ordinal)}}" } },
              "active_media_reference_key": "DEFAULT_MEDIA" }
            """));
        return Assert.IsType<Timeline>(OtioReader.ReadString($$"""
            { "OTIO_SCHEMA": "Timeline.1", "name": "t", "tracks": { "OTIO_SCHEMA": "Stack.1", "children": [
              { "OTIO_SCHEMA": "Track.1", "kind": "Video", "children": [ {{clips}} ] } ] } }
            """));
    }
}
