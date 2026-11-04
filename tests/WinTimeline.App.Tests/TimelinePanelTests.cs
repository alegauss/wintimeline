using WinTimeline.Model.Time;

namespace WinTimeline.App.Tests;

/// <summary>WT47: the timeline panel's timecodes never throw out of a frame or an automation query.</summary>
public sealed class TimelinePanelTests
{
    [Fact]
    public void ATimeIsTimecodeFromTheGlobalStart()
    {
        Assert.Equal("01:00:04:12", TimelinePanel.Timecode(4.5, new RationalTime(86400, 24)));
        Assert.Equal("00:00:01:00", TimelinePanel.Timecode(1, null));
    }

    [Fact]
    public void ATransitionReachingBeforeTheStartIsSignedNotThrown()
    {
        Assert.Equal("-00:00:00:12", TimelinePanel.Timecode(-0.5, null));
    }

    [Fact]
    public void ARateWithNoTimecodeFallsBackToSeconds()
    {
        Assert.Equal("2.000 s", TimelinePanel.Timecode(2, new RationalTime(0, 1)));
    }
}
