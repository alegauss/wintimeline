using System.IO;
using WinTimeline.Engine.Analysis;
using WinTimeline.Media;

namespace WinTimeline.Engine.Tests;

/// <summary>WT121: the click track's beats found within 10 ms of where they were made, and its tempo.</summary>
public sealed class BeatTests
{
    [Fact]
    public void EveryClickOfTheClickTrackIsABeatWithinTenMillisecondsAndTheTempoIs120()
    {
        // tests/fixtures/media/make_media.cmd: 3 ms clicks at 120 BPM from 0.25 s, for 4 s.
        DecodedAudio audio = AudioDecoder.Read(Path.Combine(Repository.Root, "tests", "fixtures", "media", "clicks-120bpm.wav"));

        BeatAnalysis found = Beats.Detect(audio.Samples, audio.Rate);

        double[] made = [.. Enumerable.Range(0, 8).Select(k => 0.25 + (0.5 * k))];
        Assert.Equal(made.Length, found.Beats.Count);
        Assert.All(made.Zip(found.Beats), pair => Assert.True(Math.Abs(pair.First - pair.Second) <= 0.010, $"beat at {pair.Second:0.0000} s for one made at {pair.First} s"));
        Assert.Equal(120, found.Bpm, 0);
    }

    [Fact]
    public void SilenceHasNoBeats()
    {
        BeatAnalysis found = Beats.Detect(new float[44100], 44100);

        Assert.Empty(found.Beats);
        Assert.Equal(0, found.Bpm);
    }
}
