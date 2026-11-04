using System.IO;
using WinTimeline.Model.Adapters;

namespace WinTimeline.Model.Tests;

/// <summary>WT158: the rates an EDL's timecodes fit, which a refusal names.</summary>
public sealed class EdlRateTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(Repository.Root, "tests", "fixtures", "edl", "samples", name));

    [Fact]
    public void A25FpsEdlFitsTwentyFiveAndFaster()
    {
        Assert.Equal([25, 29.97, 30, 50, 59.94, 60], EdlRates.Fitting(Sample("25fps.edl")));
    }

    [Fact]
    public void AnEdlWhoseFramesStayUnder24FitsEveryRate()
    {
        Assert.Equal(EdlRates.Standard, EdlRates.Fitting("001  A V C 01:00:00:23 01:00:01:00 00:00:00:00 00:00:00:01"));
        Assert.Equal(["23.976", "29.97", "60"], new[] { 23.976, 29.97, 60 }.Select(EdlRates.Label));
    }

    [Fact]
    public void Read25FpsAtItsRateItOpensAndAt24ItIsRefused()
    {
        Timeline at25 = Assert.IsType<Timeline>(CmxEdlReader.Read(Sample("25fps.edl"), 25, ignoreTimecodeMismatch: false));
        Assert.Equal(4, at25.FindClips().Count());

        EdlParseException refused = Assert.Throws<EdlParseException>(() => CmxEdlReader.Read(Sample("25fps.edl"), 24, ignoreTimecodeMismatch: false));
        Assert.Contains("has frames beyond 23", refused.Message, StringComparison.Ordinal);
    }
}
