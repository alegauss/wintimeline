using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WinTimeline.Render.Tests;

/// <summary>WT3: the one device opens on WARP everywhere and on each hardware adapter the desk has.</summary>
public sealed class GpuContextTests
{
    [Fact]
    public void WarpOpensAMultithreadProtectedDeviceAtFeatureLevel11()
    {
        using GpuContext gpu = GpuContext.CreateWarp();

        Assert.True(gpu.IsWarp);
        Assert.True(gpu.Adapter.IsSoftware);
        Assert.True(gpu.FeatureLevel >= FeatureLevel.Level_11_0);
        using ID3D11Multithread multithread = gpu.ImmediateContext.QueryInterface<ID3D11Multithread>();
        Assert.True(multithread.GetMultithreadProtected());
    }

    [Fact]
    public void AWarpDeviceCreatesAndClearsATexture()
    {
        // What every later layer does with the device: a BGRA render target a frame lands in.
        using GpuContext gpu = GpuContext.CreateWarp();
        using ID3D11Texture2D texture = gpu.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, 64, 36, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        using ID3D11RenderTargetView view = gpu.Device.CreateRenderTargetView(texture);

        gpu.ImmediateContext.ClearRenderTargetView(view, new Vortice.Mathematics.Color4(0.2f, 0.4f, 0.6f, 1f));
        gpu.ImmediateContext.Flush();

        Assert.Equal(64u, texture.Description.Width);
    }

    [Fact]
    public void AChoiceNamingWarpOpensWarp()
    {
        using GpuContext gpu = GpuContext.Create(AdapterChoice.Pick(GpuAdapters.Enumerate(), AdapterChoice.Warp));

        Assert.True(gpu.IsWarp);
    }

    [Fact]
    public void ARefusedChoiceIsNotQuietlyReplaced()
    {
        AdapterChoice refused = AdapterChoice.Pick([], "nvidia");

        ArgumentException error = Assert.Throws<ArgumentException>(() => GpuContext.Create(refused));
        Assert.StartsWith("--adapter nvidia matches no adapter.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHardwareAdapterOpensAVideoCapableDevice()
    {
        // The done-when's hybrid laptop, on whatever this desk has: each hardware adapter by index.
        GpuAdapter[] hardware = [.. GpuAdapters.Enumerate().Where(a => !a.IsSoftware)];
        if (hardware.Length == 0)
        {
            Assert.Skip("DXGI lists no hardware adapter on this desk");
        }

        foreach (GpuAdapter adapter in hardware)
        {
            using GpuContext gpu = GpuContext.Create(AdapterChoice.Pick(hardware, adapter.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            Assert.False(gpu.IsWarp, $"{adapter.Name} fell back to WARP");
            Assert.Equal(adapter.Luid, gpu.Adapter.Luid);
            Assert.True(gpu.VideoSupport, $"{adapter.Name} opened without video support");
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", gpu.Adapter.DriverVersion);
        }
    }
}
