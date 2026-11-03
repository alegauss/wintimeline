using Vortice.MediaFoundation;

namespace WinTimeline.Render;

/// <summary>
/// Media Foundation's view of the shared device (WT3): the DXGI device manager a source reader or
/// a decoder transform is handed, so DXVA decodes straight into textures on <see cref="GpuContext.Device"/>
/// and a frame reaches the compositor without a copy through system memory.
/// </summary>
public sealed class MediaDevice : IDisposable
{
    private static readonly Lock StartupLock = new();
    private static bool _started;

    internal MediaDevice(GpuContext gpu)
    {
        Start();
        Manager = MediaFactory.MFCreateDXGIDeviceManager();
        Manager.ResetDevice(gpu.Device);
    }

    /// <summary>The manager to set as <c>MF_SOURCE_READER_D3D_MANAGER</c> or send to a decoder.</summary>
    public IMFDXGIDeviceManager Manager { get; }

    /// <inheritdoc/>
    public void Dispose() => Manager.Dispose();

    /// <summary>Starts Media Foundation once for the process; it is never shut down, as the process owns it.</summary>
    private static void Start()
    {
        lock (StartupLock)
        {
            if (!_started)
            {
                MediaFactory.MFStartup().CheckError();
                _started = true;
            }
        }
    }
}
