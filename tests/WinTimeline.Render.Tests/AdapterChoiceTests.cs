namespace WinTimeline.Render.Tests;

/// <summary>WT3: what --adapter resolves to, on a made-up hybrid laptop.</summary>
public sealed class AdapterChoiceTests
{
    private static readonly GpuAdapter Discrete = new(0, "NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 0x28A0, 8L << 30, 16L << 30, "32.0.15.6094", false, 1);
    private static readonly GpuAdapter Integrated = new(1, "Intel(R) UHD Graphics 770", 0x8086, 0xA780, 128L << 20, 16L << 30, "31.0.101.5186", false, 2);
    private static readonly GpuAdapter Basic = new(2, "Microsoft Basic Render Driver", 0x1414, 0x8C, 0, 16L << 30, "10.0.26100.1", true, 3);
    private static readonly GpuAdapter[] Laptop = [Discrete, Integrated, Basic];

    [Fact]
    public void NoRequestTakesTheFirstHardwareAdapterInHighPerformanceOrder()
    {
        Assert.Equal(Discrete, AdapterChoice.Pick(Laptop, null).Adapter);
    }

    [Fact]
    public void NoRequestOnADeskWithoutHardwareRunsOnWarp()
    {
        AdapterChoice choice = AdapterChoice.Pick([Basic], null);

        Assert.True(choice.Ok);
        Assert.Null(choice.Adapter);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("intel")]
    [InlineData("UHD")]
    public void AnIndexOrAPartOfTheNamePicksOneAdapter(string request)
    {
        Assert.Equal(Integrated, AdapterChoice.Pick(Laptop, request).Adapter);
    }

    [Theory]
    [InlineData("warp")]
    [InlineData("WARP")]
    public void WarpAsksForTheSoftwareRasteriser(string request)
    {
        AdapterChoice choice = AdapterChoice.Pick(Laptop, request);

        Assert.True(choice.Ok);
        Assert.Null(choice.Adapter);
    }

    [Theory]
    [InlineData("7", "--adapter 7: there is no adapter 7. This PC has: 0 NVIDIA GeForce RTX 4060 Laptop GPU; 1 Intel(R) UHD Graphics 770; 2 Microsoft Basic Render Driver, or warp.")]
    [InlineData("radeon", "--adapter radeon matches no adapter. This PC has: 0 NVIDIA GeForce RTX 4060 Laptop GPU; 1 Intel(R) UHD Graphics 770; 2 Microsoft Basic Render Driver, or warp.")]
    [InlineData("o", "--adapter o matches 2 adapters; give its index. This PC has: 0 NVIDIA GeForce RTX 4060 Laptop GPU; 1 Intel(R) UHD Graphics 770; 2 Microsoft Basic Render Driver, or warp.")]
    public void ARequestThatNamesNoSingleAdapterIsRefusedWithTheList(string request, string error)
    {
        AdapterChoice choice = AdapterChoice.Pick(Laptop, request);

        Assert.False(choice.Ok);
        Assert.Equal(error, choice.Error);
    }

    [Fact]
    public void TheLabelGivesTheMemoryInGigabytes()
    {
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU, 8 GB", Discrete.Label);
        Assert.Equal("Microsoft Basic Render Driver (software)", Basic.Label);
        Assert.Equal("NVIDIA", Discrete.Vendor);
        Assert.Equal("Intel", Integrated.Vendor);
    }
}
