using System.IO;

namespace WinTimeline.Media.Tests;

/// <summary>WT68: a dropped file says how long it is, at what rate, and whether it has pictures and sound.</summary>
public sealed class MediaProbeTests
{
    private static string Fixture(string name) => Path.Combine(Repository.Root, "tests", "fixtures", "media", name);

    [Fact]
    public void AVideoWithSoundIsMeasuredWithoutDecoding()
    {
        MediaInfo info = MediaProbe.Probe(Fixture("bars-3s-25p.mp4"));

        Assert.Equal(3.0, info.Duration.TotalSeconds, 1);
        Assert.Equal(25.0, info.FrameRate);
        Assert.Equal((320, 180), (info.Width, info.Height));
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void SoundAloneHasNoFrameRate()
    {
        MediaInfo info = MediaProbe.Probe(Fixture("tone-2s.wav"));

        Assert.Equal(2.0, info.Duration.TotalSeconds, 2);
        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void WhatIsNotMediaIsRefusedByName()
    {
        string text = Path.Combine(Directory.CreateTempSubdirectory("wt-probe-").FullName, "notes.txt");
        File.WriteAllText(text, "not a video");

        Assert.StartsWith("notes.txt is not media", Assert.Throws<MediaProbeException>(() => MediaProbe.Probe(text)).Message, StringComparison.Ordinal);
        Assert.Equal("gone.mp4 was not found", Assert.Throws<MediaProbeException>(() => MediaProbe.Probe(Path.Combine(Path.GetDirectoryName(text)!, "gone.mp4"))).Message);
    }
}
