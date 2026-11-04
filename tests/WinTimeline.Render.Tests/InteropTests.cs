using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3D11 = Vortice.Direct3D11;

namespace WinTimeline.Render.Tests;

/// <summary>WT151: a texture crosses from D3D11 to D3D12 and back by NT handle, and the device reports its memory and decoders.</summary>
public sealed class InteropTests
{
    private const int Size = 64;

    [Fact]
    public void ATextureRoundTripsFromD3D11ToD3D12AndBackWithoutACopy()
    {
        using GpuContext gpu = Gpu();
        using D3D12Interop interop = Interop(gpu);
        using D3D11.ID3D11Texture2D shared = interop.CreateShareable(Size, Size, Format.R8G8B8A8_UNorm, D3D11.BindFlags.RenderTarget);
        using ID3D12Resource opened = interop.Open(shared);

        // D3D11 paints it red; D3D12 reads the same memory.
        using (D3D11.ID3D11RenderTargetView view = gpu.Device.CreateRenderTargetView(shared))
        {
            gpu.ImmediateContext.ClearRenderTargetView(view, new Color4(1, 0, 0, 1));
        }

        interop.HandToD3D12();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, ReadOn12(interop, opened));

        // D3D12 writes green into it; D3D11 reads it after waiting on the fence.
        WriteOn12(interop, opened, [0, 255, 0, 255]);
        interop.HandToD3D11();
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, ReadOn11(gpu, shared));
    }

    [Fact]
    public void TheDeviceReportsItsVideoMemoryAndAWatchFollowsTheBudget()
    {
        using GpuContext gpu = Gpu();
        using VideoMemoryWatch watch = new(gpu);

        (long inUse, long budget) = watch.Now;
        Assert.True(budget > 0 && inUse >= 0, $"{inUse} of {budget}");
        Assert.False(watch.Over(1.0) && inUse < budget);

        // The OS raises a budget change only under real pressure; filling a budget of several
        // gigabytes to provoke one would take this desk's memory, so the event is not forced here.
        Assert.SkipWhen(budget > (2L << 30), $"the budget is {budget >> 20} MB, too large to fill in a test; the event is registered and its handler reads the budget");
    }

    [Fact]
    public void AHardwareGpuNamesTheCodecsItDecodes()
    {
        GpuAdapter? hardware = GpuAdapters.Enumerate().FirstOrDefault(a => !a.IsSoftware);
        Assert.SkipWhen(hardware is null, "DXGI lists no hardware adapter on this desk");
        using GpuContext gpu = GpuContext.Create(new AdapterChoice(hardware, null));
        Assert.SkipUnless(gpu.VideoSupport, $"{hardware!.Name} opened without video support");

        // A VM's display adapter (VMware's SVGA) opens with video support and decodes nothing,
        // which the empty list says truthfully; H.264 is asked only of the GPU vendors that ship it.
        Assert.SkipUnless(hardware.VendorId is 0x10DE or 0x8086 or 0x1002, $"{hardware.Name} is not an NVIDIA, Intel or AMD GPU");

        IReadOnlyList<DecodeProfile> profiles = DecodeProfiles.Of(gpu);

        Assert.Contains(profiles, p => p.Name == "H.264");
    }

    [Fact]
    public void HandingA4KTextureToD3D12CostsLessThanCopyingItThroughTheCpu()
    {
        GpuAdapter? hardware = GpuAdapters.Enumerate().FirstOrDefault(a => !a.IsSoftware);
        Assert.SkipWhen(hardware is null, "the measurement is for a hardware adapter");
        using GpuContext gpu = GpuContext.Create(new AdapterChoice(hardware, null));
        D3D12Interop? maybe = null;
        try
        {
            maybe = D3D12Interop.Create(gpu);
        }
        catch (NotSupportedException ex)
        {
            Assert.Skip(ex.Message);
        }

        using D3D12Interop interop = maybe!;
        using D3D11.ID3D11Texture2D shared = interop.CreateShareable(3840, 2160, Format.B8G8R8A8_UNorm, D3D11.BindFlags.RenderTarget);
        using ID3D12Resource opened = interop.Open(shared);
        using D3D11.ID3D11Texture2D staging = gpu.Device.CreateTexture2D(shared.Description with { Usage = D3D11.ResourceUsage.Staging, BindFlags = D3D11.BindFlags.None, CPUAccessFlags = D3D11.CpuAccessFlags.Read, MiscFlags = D3D11.ResourceOptionFlags.None });

        double handOff = Median(20, () => { interop.HandToD3D12(); interop.Drain(); });
        double copy = Median(20, () =>
        {
            gpu.ImmediateContext.CopyResource(staging, shared);
            D3D11.MappedSubresource map = gpu.ImmediateContext.Map(staging, 0, D3D11.MapMode.Read);
            byte[] frame = new byte[3840 * 2160 * 4];
            Marshal.Copy(map.DataPointer, frame, 0, frame.Length);
            gpu.ImmediateContext.Unmap(staging, 0);
        });

        TestContext.Current.TestOutputHelper?.WriteLine($"4K BGRA on {hardware!.Name}: shared hand-off {handOff:F2} ms, staging copy {copy:F2} ms");
        Assert.True(handOff < copy, $"shared hand-off {handOff:F2} ms against staging copy {copy:F2} ms");
    }

    private static GpuContext Gpu() => GpuContext.CreateWarp();

    private static D3D12Interop Interop(GpuContext gpu)
    {
        try
        {
            return D3D12Interop.Create(gpu);
        }
        catch (NotSupportedException ex)
        {
            Assert.Skip(ex.Message);
            throw;
        }
    }

    private static double Median(int runs, Action action)
    {
        List<double> times = [];
        for (int i = 0; i < runs; i++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            action();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        return times.Order().ElementAt(runs / 2);
    }

    private static byte[] ReadOn12(D3D12Interop interop, ID3D12Resource texture)
    {
        ID3D12Device device = interop.Device;
        PlacedSubresourceFootPrint[] layouts = new PlacedSubresourceFootPrint[1];
        device.GetCopyableFootprints(texture.Description, 0, 1, 0, layouts, new uint[1], new ulong[1], out ulong total);
        using ID3D12Resource readback = device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(total), ResourceStates.CopyDest);
        Submit(interop, list =>
        {
            list.ResourceBarrierTransition(texture, ResourceStates.Common, ResourceStates.CopySource);
            list.CopyTextureRegion(new TextureCopyLocation(readback, layouts[0]), 0, 0, 0, new TextureCopyLocation(texture, 0));
            list.ResourceBarrierTransition(texture, ResourceStates.CopySource, ResourceStates.Common);
        });
        unsafe
        {
            void* data;
            readback.Map(0, null, &data).CheckError();
            byte[] pixel = new byte[4];
            Marshal.Copy((nint)data + (nint)(((Size - 1) * layouts[0].Footprint.RowPitch) + ((Size - 1) * 4)), pixel, 0, 4);
            readback.Unmap(0);
            return pixel;
        }
    }

    private static void WriteOn12(D3D12Interop interop, ID3D12Resource texture, byte[] rgba)
    {
        ID3D12Device device = interop.Device;
        PlacedSubresourceFootPrint[] layouts = new PlacedSubresourceFootPrint[1];
        device.GetCopyableFootprints(texture.Description, 0, 1, 0, layouts, new uint[1], new ulong[1], out ulong total);
        using ID3D12Resource upload = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(total), ResourceStates.GenericRead);
        unsafe
        {
            void* data;
            upload.Map(0, null, &data).CheckError();
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Marshal.Copy(rgba, 0, (nint)data + (nint)((y * layouts[0].Footprint.RowPitch) + (x * 4)), 4);
                }
            }

            upload.Unmap(0);
        }

        Submit(interop, list =>
        {
            list.ResourceBarrierTransition(texture, ResourceStates.Common, ResourceStates.CopyDest);
            list.CopyTextureRegion(new TextureCopyLocation(texture, 0), 0, 0, 0, new TextureCopyLocation(upload, layouts[0]));
            list.ResourceBarrierTransition(texture, ResourceStates.CopyDest, ResourceStates.Common);
        });
    }

    private static void Submit(D3D12Interop interop, Action<ID3D12GraphicsCommandList> record)
    {
        using ID3D12CommandAllocator allocator = interop.Device.CreateCommandAllocator(CommandListType.Direct);
        using ID3D12GraphicsCommandList list = interop.Device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, allocator, null);
        record(list);
        list.Close();
        interop.Queue.ExecuteCommandList(list);
        interop.Drain();
    }

    private static byte[] ReadOn11(GpuContext gpu, D3D11.ID3D11Texture2D texture)
    {
        using D3D11.ID3D11Texture2D staging = gpu.Device.CreateTexture2D(texture.Description with { Usage = D3D11.ResourceUsage.Staging, BindFlags = D3D11.BindFlags.None, CPUAccessFlags = D3D11.CpuAccessFlags.Read, MiscFlags = D3D11.ResourceOptionFlags.None });
        gpu.ImmediateContext.CopyResource(staging, texture);
        D3D11.MappedSubresource map = gpu.ImmediateContext.Map(staging, 0, D3D11.MapMode.Read);
        byte[] pixel = new byte[4];
        Marshal.Copy(map.DataPointer + (nint)(((Size - 1) * map.RowPitch) + ((Size - 1) * 4)), pixel, 0, 4);
        gpu.ImmediateContext.Unmap(staging, 0);
        return pixel;
    }
}
