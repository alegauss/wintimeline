using System.IO;

namespace WinTimeline.Media.Tests;

/// <summary>WT121: a file's sound decoded to mono samples at its own rate, and a file with none refused.</summary>
public sealed class AudioDecoderTests
{
    private static string Fixture(string name) => Path.Combine(Repository.Root, "tests", "fixtures", "media", name);

    [Fact]
    public void AStereoToneDecodesToTwoSecondsOfMonoAtFortyEightKilohertz()
    {
        DecodedAudio tone = AudioDecoder.Read(Fixture("tone-2s.wav"));

        Assert.Equal(48000, tone.Rate);
        Assert.Equal(2.0, tone.Seconds, 2);
        Assert.InRange(tone.Samples.Max(Math.Abs), 0.05f, 1f);
    }

    [Fact]
    public void AMissingFileIsRefusedByName()
    {
        MediaProbeException refused = Assert.Throws<MediaProbeException>(() => AudioDecoder.Read(Fixture("nothing.wav")));

        Assert.Contains("nothing.wav", refused.Message, StringComparison.Ordinal);
    }
}
