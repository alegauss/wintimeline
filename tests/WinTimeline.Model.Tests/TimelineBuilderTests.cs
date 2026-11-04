using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT68: dropped media becomes a timeline in the reference's own shape.</summary>
public sealed class TimelineBuilderTests
{
    [Fact]
    public void PicturesGoOnV1SoundBesideThemAndSoundAloneLeavesAGap()
    {
        Timeline timeline = TimelineBuilder.FromMedia("Untitled", 25,
        [
            new MediaClip("bars", "file:///D:/media/bars.mp4", 3, 25, HasAudio: true),
            new MediaClip("tone", "file:///D:/media/tone.wav", 2, null, HasAudio: true),
            new MediaClip("silent", "file:///D:/media/silent.mov", 1, 25, HasAudio: false),
        ]);

        Track v1 = Assert.Single(timeline.VideoTracks);
        Track a1 = Assert.Single(timeline.AudioTracks);
        Assert.Equal(["bars", "", "silent"], v1.Children.Select(c => c.Name));
        Assert.IsType<Gap>(v1.Children[1]);
        Assert.Equal(["bars", "tone", ""], a1.Children.Select(c => c.Name));
        Assert.Equal(6.0, timeline.Duration.ValueRescaledTo(1), 6);

        Clip bars = timeline.FindClips().First();
        ExternalReference media = Assert.IsType<ExternalReference>(bars.MediaReference);
        Assert.Equal("file:///D:/media/bars.mp4", media.TargetUrl);
        Assert.Equal(75, bars.TrimmedRange().Duration.Value);
    }

    [Fact]
    public void ABuiltTimelineSavesAndReadsBackTheSame()
    {
        Timeline timeline = TimelineBuilder.FromMedia("Untitled", 24, [new MediaClip("a", "file:///a.mov", 1.5, 24, true)]);

        string written = OtioWriter.Write(timeline);
        Assert.Equal(written, OtioWriter.Write(OtioReader.ReadString(written)));
        Assert.Equal(2, TimelineBuilder.Empty("e", 24).Tracks.Children.Count);
    }
}
