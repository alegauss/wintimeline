using WinTimeline.Render;

namespace WinTimeline.Inference.Tests;

/// <summary>WT99: where models run, and how many may stay loaded.</summary>
public sealed class InferenceTests
{
    private static readonly GpuAdapter Rtx = new(0, "NVIDIA GeForce RTX 4060", 0x10DE, 0x2882, 8L << 30, 16L << 30, "32.0.15.6094", false, 1234);
    private static readonly GpuAdapter Warp = new(1, "Microsoft Basic Render Driver", 0x1414, 0x8C, 0, 8L << 30, string.Empty, true, 99);

    [Fact]
    public void TheProbeModelRunsOnTheCpuThroughARealSession()
    {
        Assert.True(new OnnxRuntimeProbe().TryRun(new ProviderSelection(ExecutionProvider.Cpu, null, null), out string? error), error);
    }

    [Fact]
    public void OnAHardwareGpuAutoRunsTheProbeOnDirectMLOrSaysWhyNot()
    {
        GpuAdapter? hardware = GpuAdapters.Enumerate().FirstOrDefault(a => !a.IsSoftware);
        Assert.SkipWhen(hardware is null, "DXGI lists no hardware adapter on this desk");

        // A real GPU runs it on DirectML; a VM's display adapter without DirectX 12 (VMware's
        // SVGA answers 887A0004) falls back to the CPU and names the reason instead of throwing.
        ProviderSelection chosen = ProviderDetector.Detect(ProviderPreference.Auto, hardware);
        Assert.True(
            chosen.Provider == ExecutionProvider.DirectML || (chosen.Provider == ExecutionProvider.Cpu && chosen.Fallback is { Length: > 0 }),
            chosen.Label);
    }

    [Fact]
    public void TheDeviceReportsTheVideoMemoryThisProcessUses()
    {
        using GpuContext gpu = GpuContext.CreateWarp();

        (long inUse, long budget) = Assert.NotNull(gpu.VideoMemory());
        Assert.True(inUse >= 0 && budget > 0, $"{inUse} in use of {budget}");
    }

    [Fact]
    public void AutoRunsOnDirectMLOnTheAppsGpuAndFallsBackToTheCpuSayingWhy()
    {
        Assert.Equal("DirectML · NVIDIA GeForce RTX 4060, 8 GB", ProviderDetector.Detect(ProviderPreference.Auto, Rtx, [], new Probe()).Label);
        Assert.Equal("CPU (GPU unavailable: the device was removed)", ProviderDetector.Detect(ProviderPreference.Auto, Rtx, [], new Probe(failing: ExecutionProvider.DirectML)).Label);
        Assert.Equal("CPU (GPU unavailable: the app runs on WARP)", ProviderDetector.Detect(ProviderPreference.Auto, Warp, [], new Probe()).Label);
        Assert.Equal("CPU", ProviderDetector.Detect(ProviderPreference.Cpu, Rtx, [], new Probe()).Label);
    }

    [Fact]
    public void WindowsMLPrefersAProviderOnTheAppsGpuOverAnNpu()
    {
        WindowsMLDevice npu = new("QNNExecutionProvider", 0x5143, IsNpu: true);
        WindowsMLDevice trt = new("NvTensorRTRTXExecutionProvider", 0x10DE, Luid: 1234);

        ProviderSelection chosen = ProviderDetector.Detect(ProviderPreference.WindowsML, Rtx, [npu, trt], new Probe());
        Assert.Equal("TensorRT-RTX · NVIDIA GeForce RTX 4060, 8 GB", chosen.Label);

        ProviderSelection onNpu = ProviderDetector.Detect(ProviderPreference.WindowsML, Warp, [npu, trt], new Probe());
        Assert.Equal(("QNN", null), (onNpu.Label, onNpu.Adapter));
    }

    [Fact]
    public void RankingDropsBuiltInsAndPluginVariantsAndPutsTheMeasuredFirst()
    {
        IReadOnlyList<WindowsMLDevice> ranked = WindowsMLProviders.Rank(
        [
            new("DmlExecutionProvider", 0x10DE),
            new("OpenVINOExecutionProvider.AUTO", 0x8086),
            new("OpenVINOExecutionProvider", 0x8086),
            new("NvTensorRTRTXExecutionProvider", 0x10DE),
            new("NvTensorRTRTXExecutionProvider", 0x10DE),
        ]);

        Assert.Equal(["NvTensorRTRTXExecutionProvider", "OpenVINOExecutionProvider"], ranked.Select(d => d.EpName));
    }

    [Theory]
    [InlineData("auto", ProviderPreference.Auto)]
    [InlineData("DirectML", ProviderPreference.Auto)]
    [InlineData("winml", ProviderPreference.WindowsML)]
    [InlineData("cpu", ProviderPreference.Cpu)]
    public void TheProviderFlagIsRead(string value, ProviderPreference expected)
    {
        Assert.True(ProviderDetector.TryParse(value, out ProviderPreference read));
        Assert.Equal(expected, read);
        Assert.False(ProviderDetector.TryParse("tpu", out _));
    }

    [Fact]
    public void TheLeastRecentlyUsedIdleSessionsGoWhenTheBudgetIsReached()
    {
        Dictionary<string, long> sizes = new(StringComparer.Ordinal) { ["clip"] = 400, ["transnet"] = 300, ["whisper"] = 500, ["huge"] = 2000 };
        List<string> unloaded = [];
        using SessionCache<FakeSession> cache = new(key => new FakeSession(key.Model), key => sizes[key.Model], budget: 1000, idle: TimeSpan.FromHours(1));
        cache.Unloaded += (_, e) => unloaded.Add($"{e.Key.Model}: {e.Reason}");

        cache.Acquire(new ModelKey("clip", "fp16")).Dispose();
        cache.Acquire(new ModelKey("transnet", "fp16")).Dispose();
        cache.Acquire(new ModelKey("clip", "fp16")).Dispose();
        Assert.Equal(700, cache.Resident);

        using (cache.Acquire(new ModelKey("whisper", "q5")))
        {
            Assert.Equal(["transnet: the model memory budget was reached"], unloaded);
            Assert.Equal(["clip", "whisper"], cache.Loaded.Select(k => k.Model));
            Assert.Equal(900, cache.Resident);
        }

        Assert.Throws<InsufficientMemoryException>(() => cache.Acquire(new ModelKey("huge", "fp32")));
        Assert.Equal(2, cache.UnloadIdle());
        Assert.Equal(0, cache.Resident);
    }

    [Fact]
    public void ASessionInUseIsNeverUnloadedEvenOverBudget()
    {
        using SessionCache<FakeSession> cache = new(key => new FakeSession(key.Model), _ => 600, budget: 1000, idle: TimeSpan.FromHours(1));

        using SessionLease<FakeSession> first = cache.Acquire(new ModelKey("a", "fp16"));
        using SessionLease<FakeSession> second = cache.Acquire(new ModelKey("b", "fp16"));

        Assert.False(first.Session.Disposed);
        Assert.Equal(["a", "b"], cache.Loaded.Select(k => k.Model));
    }

    private sealed class Probe(ExecutionProvider? failing = null) : IProviderProbe
    {
        public bool TryRun(ProviderSelection candidate, out string? error)
        {
            error = candidate.Provider == failing ? "the device was removed" : null;
            return error is null;
        }
    }

    private sealed class FakeSession(string name) : IDisposable
    {
        public string Name { get; } = name;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
