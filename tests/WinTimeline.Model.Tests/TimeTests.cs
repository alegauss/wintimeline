using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>WT12: opentime's arithmetic and timecode, against values the reference library printed.</summary>
public sealed class TimeTests
{
    private const double Ntsc = 30000.0 / 1001.0;
    private const double Ntsc60 = 60000.0 / 1001.0;

    [Theory]
    [InlineData(0, 24, "00:00:00:00")]
    [InlineData(86399, 24, "00:59:59:23")]
    [InlineData(2160000, 24, "01:00:00:00")] // 25 hours rolls over to 1
    [InlineData(1800, Ntsc, "00:01:00;02")]
    [InlineData(17982, Ntsc, "00:10:00;00")]
    [InlineData(107892, Ntsc, "01:00:00;00")]
    [InlineData(3600, Ntsc60, "00:01:00;04")]
    [InlineData(1084319, Ntsc, "10:03:00;05")]
    [InlineData(100, 23.976, "00:00:04:04")]
    [InlineData(100, 24000.0 / 1001.0, "00:00:04:04")]
    [InlineData(12345.5, 25, "00:08:13:20")]
    [InlineData(10, 29.97, "00:00:00;10")]
    [InlineData(500, 59.94, "00:00:08;20")]
    public void TimecodeMatchesTheReference(double value, double rate, string timecode)
    {
        Assert.Equal(timecode, new RationalTime(value, rate).ToTimecode(rate));
    }

    [Theory]
    [InlineData(-1, 24)]
    [InlineData(90, 7)]
    public void TimecodeIsRefusedWhereTheReferenceRefusesIt(double value, double rate)
    {
        Assert.Throws<TimecodeException>(() => new RationalTime(value, rate).ToTimecode(rate));
    }

    [Theory]
    [InlineData("01:00:00:00", 24.0, 86400.0)]
    [InlineData("00:01:00;02", Ntsc, 1800.0)]
    [InlineData("10:00:00;00", Ntsc60, 2157840.0)]
    public void FromTimecodeMatchesTheReference(string timecode, double rate, double value)
    {
        Assert.Equal(new RationalTime(value, rate), RationalTime.FromTimecode(timecode, rate));
    }

    [Theory]
    [InlineData("00:00:01:24", 24.0)]
    [InlineData("00:01:00;02", 25.0)]
    public void FromTimecodeIsRefusedWhereTheReferenceRefusesIt(string timecode, double rate)
    {
        Assert.Throws<TimecodeException>(() => RationalTime.FromTimecode(timecode, rate));
    }

    [Fact]
    public void ArithmeticRescalesToTheHigherRate()
    {
        RationalTime sum = new RationalTime(12, 24) + new RationalTime(15, 30);

        Assert.Equal(30, sum.Rate);
        Assert.Equal(30, sum.Value);
        Assert.Equal(new RationalTime(1, 1), sum); // equality is by time, not by fields
        Assert.True(new RationalTime(1, 24) < new RationalTime(2, 30));
    }

    [Fact]
    public void TheInclusiveEndIsOneFrameBeforeTheExclusiveOne()
    {
        TimeRange range = new(new RationalTime(10, 24), new RationalTime(5, 24));

        Assert.Equal(new RationalTime(15, 24), range.EndTimeExclusive);
        Assert.Equal(new RationalTime(14, 24), range.EndTimeInclusive);
        Assert.Equal(range, TimeRange.FromStartEndTimeInclusive(new RationalTime(10, 24), new RationalTime(14, 24)));
        Assert.True(range.Contains(new RationalTime(14, 24)));
        Assert.False(range.Contains(new RationalTime(15, 24)));
    }
}
