using System.IO;
using System.Text.Json;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model.Time;

namespace WinTimeline.Engine.Tests;

/// <summary>WT49: the ruler's ticks adapt to the zoom, and their timecode is the reference library's at every tick.</summary>
public sealed class RulerTests
{
    private static readonly JsonElement Reference = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(Repository.Root, "tests", "fixtures", "ruler", "timecodes.json"))).RootElement;

    public static TheoryData<string> Rates => ["23.976", "25", "29.97", "59.94"];

    [Theory]
    [MemberData(nameof(Rates))]
    public void EveryLabelledTickReadsAsTheReferenceWritesItsTimecode(string name)
    {
        JsonElement entry = Reference.GetProperty(name);
        double rate = entry.GetProperty("rate").GetDouble();
        JsonElement timecodes = entry.GetProperty("timecodes");
        int checkedTicks = 0;

        // Zooms from the closest to two hours across, views placed across the first two hours.
        foreach (double pps in new[] { 200 * rate, 40 * rate, 8 * rate, 400, 60, 12, 2, 0.4 })
        {
            foreach (double start in new[] { 0, 59.5, 600, 3599, 7000 })
            {
                // The table holds frame steps for the first two minutes, second steps for two hours.
                if (Ruler.Step(pps, rate, 90) < Math.Round(rate) && start > 100)
                {
                    continue;
                }

                TimelineViewport view = new(start, pps, 0, 30, 1600, 400) { FrameRate = rate };
                foreach (RulerTick tick in Ruler.Ticks(view, rate, new RationalTime(0, rate), RulerUnit.Timecode, 90).Where(t => t.Major))
                {
                    if (!timecodes.TryGetProperty(tick.Frame.ToString(System.Globalization.CultureInfo.InvariantCulture), out JsonElement expected))
                    {
                        Assert.True(tick.Frame > Math.Round(rate) * 7200, $"tick at frame {tick.Frame} ({name}) is not on a step the reference table covers");
                        continue;
                    }

                    Assert.Equal(expected.GetString(), tick.Label);
                    checkedTicks++;
                }
            }
        }

        Assert.True(checkedTicks > 100, $"only {checkedTicks} ticks were checked at {name}");
    }

    [Fact]
    public void TheStepGrowsWithDistanceFromOneFrameToAnHour()
    {
        Assert.Equal(1, Ruler.Step(200 * 25, 25, 90));
        Assert.Equal(25, Ruler.Step(100, 25, 90));
        Assert.Equal(25 * 60, Ruler.Step(2, 25, 90));
        Assert.Equal(25 * 3600, Ruler.Step(0.0001, 25, 90));
    }

    [Fact]
    public void LabelsInFramesFeetAndSecondsAndMinorTicksBetween()
    {
        Assert.Equal("1234", Ruler.Label(1234, 24, new RationalTime(0, 24), RulerUnit.Frames));
        Assert.Equal("77+02", Ruler.Label(1234, 24, new RationalTime(0, 24), RulerUnit.FeetFrames));
        Assert.Equal("51.417 s", Ruler.Label(1234, 24, new RationalTime(0, 24), RulerUnit.Seconds));
        Assert.Equal("01:00:51:10", Ruler.Label(1234, 24, new RationalTime(86400, 24), RulerUnit.Timecode));

        IReadOnlyList<RulerTick> ticks = Ruler.Ticks(new TimelineViewport(0, 100, 0, 30, 1000, 300) { FrameRate = 25 }, 25, new RationalTime(0, 25), RulerUnit.Frames, 90);
        Assert.Equal(["0", "", "", "", "", "25"], ticks.Take(6).Select(t => t.Label));
    }
}
