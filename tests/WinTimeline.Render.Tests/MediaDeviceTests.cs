using System.IO;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace WinTimeline.Render.Tests;

/// <summary>
/// WT3: Media Foundation decodes into textures on the shared device. A short H.264 clip is
/// encoded here with Windows' own encoder, so no media fixture is committed, then read back with
/// the device manager set: the frame must arrive as a texture on <see cref="GpuContext.Device"/>.
/// </summary>
public sealed class MediaDeviceTests
{
    // MFVideoFormat_H264 and MFVideoFormat_NV12, which Vortice names no constant for.
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");

    private const int Width = 128;
    private const int Height = 96;

    [Fact]
    public void WarpWithoutVideoSupportHasNoMediaDevice()
    {
        using GpuContext gpu = GpuContext.CreateWarp();

        Assert.Equal(gpu.VideoSupport, gpu.Media is not null);
    }

    [Fact]
    public void AnH264FrameDecodesIntoATextureOnTheSharedDevice()
    {
        GpuAdapter? hardware = GpuAdapters.Enumerate().FirstOrDefault(a => !a.IsSoftware);
        if (hardware is null)
        {
            Assert.Skip("DXGI lists no hardware adapter on this desk, and DXVA needs one");
        }

        using GpuContext gpu = GpuContext.Create(AdapterChoice.Pick([hardware], null));
        MediaDevice media = Assert.IsType<MediaDevice>(gpu.Media);

        string clip = Path.Combine(Path.GetTempPath(), $"wt3-{Guid.NewGuid():N}.mp4");
        try
        {
            Encode(clip, frames: 10);

            using IMFAttributes attributes = MediaFactory.MFCreateAttributes(2);
            attributes.Set(SourceReaderAttributeKeys.D3DManager, media.Manager);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            using IMFSourceReader reader = MediaFactory.MFCreateSourceReaderFromURL(clip, attributes);

            using IMFMediaType nv12 = MediaFactory.MFCreateMediaType();
            nv12.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            nv12.Set(MediaTypeAttributeKeys.Subtype, Nv12);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nv12);

            using IMFSample sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out SourceReaderFlag flags, out _);
            Assert.False(flags.HasFlag(SourceReaderFlag.EndOfStream), "the clip ended before a frame was decoded");
            Assert.NotNull(sample);

            using IMFMediaBuffer buffer = sample.GetBufferByIndex(0);
            using IMFDXGIBuffer dxgi = buffer.QueryInterface<IMFDXGIBuffer>();
            using ID3D11Texture2D texture = new(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
            using ID3D11Device owner = texture.Device;

            // The same device, not a copy on another: compared as the COM identity.
            using ID3D11Device ours = gpu.Device.QueryInterface<ID3D11Device>();
            Assert.Equal(ours.NativePointer, owner.NativePointer);
            Assert.True(texture.Description.Width >= Width);
            Assert.True(texture.Description.Height >= Height);
        }
        finally
        {
            File.Delete(clip);
        }
    }

    /// <summary>Writes a few flat grey RGB32 frames as H.264 through the sink writer.</summary>
    private static void Encode(string path, int frames)
    {
        using IMFSinkWriter writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, null);

        using IMFMediaType output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, H264);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, 500_000u);
        output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
        output.Set(MediaTypeAttributeKeys.FrameSize, Pack(Width, Height));
        output.Set(MediaTypeAttributeKeys.FrameRate, Pack(25, 1));
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
        int stream = writer.AddStream(output);

        using IMFMediaType input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
        input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        input.Set(MediaTypeAttributeKeys.FrameSize, Pack(Width, Height));
        input.Set(MediaTypeAttributeKeys.FrameRate, Pack(25, 1));
        input.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
        writer.SetInputMediaType(stream, input, null);

        writer.BeginWriting();
        const long FrameTicks = 10_000_000 / 25;
        int bytes = Width * Height * 4;
        for (int i = 0; i < frames; i++)
        {
            using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
            buffer.Lock(out IntPtr data, out _, out _);
            unsafe
            {
                new Span<byte>((void*)data, bytes).Fill((byte)(64 + (i * 8)));
            }

            buffer.Unlock();
            buffer.CurrentLength = bytes;

            using IMFSample sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = i * FrameTicks;
            sample.SampleDuration = FrameTicks;
            writer.WriteSample(stream, sample);
        }

        writer.Finalize();
    }

    private static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
}
