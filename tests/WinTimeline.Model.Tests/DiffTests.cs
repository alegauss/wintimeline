using System.IO;
using WinTimeline.Model.Compare;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT21: two versions of a cut whose differences are known give exactly those differences.</summary>
public sealed class DiffTests
{
    private static Timeline Cut(string name) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", name)));

    [Fact]
    public void TheKnownEditsAreExactlyTheChangeList()
    {
        TimelineDiff diff = TimelineDiff.Compare(Cut("cut_v1.otio"), Cut("cut_v2.otio"));

        Assert.Equal(
            [
                "Moved c5 to V1 at 01:00:00:00 from 01:00:06:12.",
                "Trimmed c2 on V1 at 01:00:03:00: head +5, tail -3 frames.",
                "Removed c3 from V1 at 01:00:04:00.",
                "Replaced c7 with c7b on V1 at 01:00:06:04.",
                "Added c6 to V1 at 01:00:07:04.",
            ],
            diff.Changes.Select(c => c.Text));
        Assert.Equal("5 changes; 3 clips unchanged.", diff.Summary);
    }

    [Fact]
    public void ACutComparedWithItselfHasNoChanges()
    {
        TimelineDiff diff = TimelineDiff.Compare(Cut("cut_v1.otio"), Cut("cut_v1.otio"));

        Assert.Empty(diff.Changes);
        Assert.Equal("No changes; 7 clips unchanged.", diff.Summary);
    }

    [Fact]
    public void TheReverseComparisonUndoesEachChange()
    {
        TimelineDiff diff = TimelineDiff.Compare(Cut("cut_v2.otio"), Cut("cut_v1.otio"));

        Assert.Equal(
            [ChangeKind.Removed, ChangeKind.Added, ChangeKind.Replaced, ChangeKind.Moved, ChangeKind.Trimmed],
            diff.Changes.Select(c => c.Kind).Order());
        ClipChange trim = Assert.Single(diff.Changes, c => c.Kind == ChangeKind.Trimmed);
        Assert.Equal((-5, 3), (trim.HeadFrames, trim.TailFrames));
    }
}
