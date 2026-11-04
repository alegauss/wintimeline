using Vortice.MediaFoundation;
using WinTimeline.Render;

namespace WinTimeline.Media;

/// <summary>What a media file holds, as far as making a clip of it needs.</summary>
/// <param name="Duration">How long it plays.</param>
/// <param name="FrameRate">Its video frame rate, or null when it has no video.</param>
/// <param name="Width">Its picture width, or 0.</param>
/// <param name="Height">Its picture height, or 0.</param>
/// <param name="HasAudio">Whether it carries sound.</param>
public sealed record MediaInfo(TimeSpan Duration, double? FrameRate, int Width, int Height, bool HasAudio)
{
    /// <summary>Whether it carries pictures.</summary>
    public bool HasVideo => FrameRate is not null;
}

/// <summary>Thrown when a file is not media Media Foundation can read.</summary>
public sealed class MediaProbeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MediaProbeException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public MediaProbeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">The cause.</param>
    public MediaProbeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// WT68: the least a dropped file must tell before it becomes a clip: duration, frame rate,
/// picture size and whether it has sound, read through a Media Foundation source reader without
/// decoding a frame. The full probe (timecode, colour tags, audio layout) is WT24's.
/// </summary>
public static class MediaProbe
{
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    private const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
    private const int MediaSource = unchecked((int)0xFFFFFFFF);

    /// <summary>Probes a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>What it holds.</returns>
    /// <exception cref="MediaProbeException">It is missing, or not media this machine can read.</exception>
    public static MediaInfo Probe(string path)
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
            long duration = (long)Convert.ToUInt64(reader.GetPresentationAttribute(MediaSource, PresentationDescriptionAttributeKeys.Duration).Value, System.Globalization.CultureInfo.InvariantCulture);
            double? rate = null;
            int width = 0, height = 0;
            if (TryNativeType(reader, FirstVideoStream) is { } video)
            {
                using (video)
                {
                    ulong packedRate = video.GetUInt64(MediaTypeAttributeKeys.FrameRate);
                    uint numerator = (uint)(packedRate >> 32), denominator = (uint)packedRate;
                    rate = denominator == 0 ? null : (double)numerator / denominator;
                    ulong size = video.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                    (width, height) = ((int)(size >> 32), (int)(uint)size);
                }
            }

            bool audio = false;
            if (TryNativeType(reader, FirstAudioStream) is { } sound)
            {
                sound.Dispose();
                audio = true;
            }

            if (rate is null && !audio)
            {
                throw new MediaProbeException($"{Path.GetFileName(path)} has neither pictures nor sound");
            }

            return new MediaInfo(TimeSpan.FromTicks(duration), rate, width, height, audio);
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            throw new MediaProbeException($"{Path.GetFileName(path)} is not media this machine can read ({ex.ResultCode})", ex);
        }
    }

    private static IMFMediaType? TryNativeType(IMFSourceReader reader, int stream)
    {
        try
        {
            return reader.GetNativeMediaType(stream, 0);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            return null;
        }
    }
}
