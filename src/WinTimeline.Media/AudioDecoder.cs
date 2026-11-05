using System.Runtime.InteropServices;
using Vortice.MediaFoundation;
using WinTimeline.Render;

namespace WinTimeline.Media;

/// <summary>A file's sound, decoded: mono samples from -1 to 1, and how many a second.</summary>
/// <param name="Samples">The samples, the channels averaged.</param>
/// <param name="Rate">Samples a second.</param>
public sealed record DecodedAudio(IReadOnlyList<float> Samples, int Rate)
{
    /// <summary>How long it plays, in seconds.</summary>
    public double Seconds => Rate > 0 ? (double)Samples.Count / Rate : 0;
}

/// <summary>
/// WT121: a file's first sound stream decoded through Media Foundation's source reader into
/// floating-point samples, its channels averaged to one, for analysis that reads waveforms
/// (beats now, silence and sync later). Whole files only: long ones belong to a streaming reader.
/// </summary>
public static class AudioDecoder
{
    /// <summary>Decodes a file's sound.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The samples.</returns>
    /// <exception cref="MediaProbeException">It is missing, holds no sound, or is not media this machine can read.</exception>
    public static DecodedAudio Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new MediaProbeException($"{Path.GetFileName(path)} was not found");
        }

        MediaDevice.Start();
        try
        {
            using IMFSourceReader reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
            using (IMFMediaType wanted = MediaFactory.MFCreateMediaType())
            {
                wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                wanted.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Float);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, wanted);
            }

            int channels, rate;
            using (IMFMediaType current = reader.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream))
            {
                channels = (int)current.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels);
                rate = (int)current.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond);
            }

            List<float> mono = [];
            while (true)
            {
                using IMFSample? sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out SourceReaderFlag flags, out _);
                if (sample is not null)
                {
                    Append(sample, channels, mono);
                }

                if ((flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    break;
                }
            }

            return new DecodedAudio([.. mono], rate);
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            throw new MediaProbeException($"{Path.GetFileName(path)} has no sound this machine can read ({ex.ResultCode})", ex);
        }
    }

    private static void Append(IMFSample sample, int channels, List<float> mono)
    {
        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out IntPtr data, out _, out int length);
        try
        {
            float[] interleaved = new float[length / sizeof(float)];
            Marshal.Copy(data, interleaved, 0, interleaved.Length);
            int across = Math.Max(1, channels);
            for (int i = 0; i + across <= interleaved.Length; i += across)
            {
                float sum = 0;
                for (int c = 0; c < across; c++)
                {
                    sum += interleaved[i + c];
                }

                mono.Add(sum / across);
            }
        }
        finally
        {
            buffer.Unlock();
        }
    }
}
