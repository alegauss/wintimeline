using System.IO;
using WinTimeline.Model.Serialization;
using ModelTimeline = WinTimeline.Model.Timeline;

namespace WinTimeline.Engine.Tests;

/// <summary>The feature-length cut WT47 is measured on: 24 tracks, 3,000 clips (tests/fixtures/timeline/make_feature.py).</summary>
internal static class Big
{
    public static ModelTimeline Timeline() =>
        Assert.IsType<ModelTimeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "timeline", "feature.otio")));
}
