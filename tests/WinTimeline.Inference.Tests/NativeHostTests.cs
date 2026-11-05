using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using WinTimeline.Inference.Native;

namespace WinTimeline.Inference.Tests;

/// <summary>
/// WT166: a native engine runs in WinTimeline.Inference.Host, so its crash, hang or unload never
/// touches the test process, which stands in for the editor. The fake engine speaks the protocol
/// a llama.cpp or whisper.cpp binding will.
/// </summary>
public sealed class NativeHostTests : IDisposable
{
    private static readonly string Host = Path.Combine(AppContext.BaseDirectory, "WinTimeline.Inference.Host.exe");
    private readonly string _model = Path.Combine(Directory.CreateTempSubdirectory("wt-native-").FullName, "tiny.gguf");

    public NativeHostTests() => File.WriteAllBytes(_model, [0x47, 0x47, 0x55, 0x46]);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_model)!, recursive: true);

    private NativeHostSession Session() => new(Host, ["--fake"], _model);

    [Fact]
    public async Task AModelLoadedInItsHostAnswersPieceByPiece()
    {
        using NativeHostSession session = Session();
        List<string> pieces = [];

        string text = await session.RunAsync("cut to the wide", new Inline<string>(pieces.Add), TestContext.Current.CancellationToken);

        Assert.Equal("cut to the wide", text);
        Assert.Equal(["cut", " to", " the", " wide"], pieces);
        Assert.NotEqual(Environment.ProcessId, session.ProcessId);
    }

    [Fact]
    public async Task ANativeCrashEndsTheHostAloneAndTheNextRunStartsItAgain()
    {
        using NativeHostSession session = Session();
        int first = session.ProcessId!.Value;

        NativeEngineException stopped = await Assert.ThrowsAsync<NativeEngineException>(() => session.RunAsync("crash", null, TestContext.Current.CancellationToken));

        Assert.StartsWith("The native engine stopped.", stopped.Message, StringComparison.Ordinal);
        Assert.Equal("the model again", await session.RunAsync("the model again", null, TestContext.Current.CancellationToken));
        Assert.NotEqual(first, session.ProcessId);
    }

    [Fact]
    public async Task ACancelStopsTheRunAndKeepsTheHostWithItsModel()
    {
        using NativeHostSession session = Session();
        int host = session.ProcessId!.Value;
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync("hang", null, cancel.Token));

        Assert.Equal(host, session.ProcessId);
        Assert.Equal("still here", await session.RunAsync("still here", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEngineThatIgnoresTheCancelHasItsHostEnded()
    {
        using NativeHostSession session = Session();
        session.CancelGrace = TimeSpan.FromMilliseconds(300);
        using Process host = Process.GetProcessById(session.ProcessId!.Value);
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync("stuck", null, cancel.Token));

        Assert.True(host.HasExited);
        Assert.Equal("and back", await session.RunAsync("and back", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AModelTheEngineCannotOpenIsRefusedWithItsReasonAndNoHostIsLeft()
    {
        NativeEngineException refused = Assert.Throws<NativeEngineException>(() => new NativeHostSession(Host, ["--fake"], _model + ".missing"));

        Assert.Contains("There is no model at", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEngineThisBuildHasNoBindingForSaysSo()
    {
        NativeEngineException refused = Assert.Throws<NativeEngineException>(() => new NativeHostSession(Host, ["--engine", "llama.cpp", "--engines", "C:\\nowhere"], _model));

        Assert.Contains("no binding to llama.cpp", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCacheHoldsANativeSessionBesideAnOnnxOneAndUnloadingEndsTheHost()
    {
        using SessionCache<IDisposable> cache = new(
            key => key.Model == "probe" ? new InferenceSession(ProbeModel.Build()) : Session(),
            _ => 100,
            budget: 1000,
            idle: TimeSpan.FromHours(1));
        int host;
        using (SessionLease<IDisposable> llm = cache.Acquire(new ModelKey("tiny", "q4_k_m")))
        using (SessionLease<IDisposable> vision = cache.Acquire(new ModelKey("probe", "fp32")))
        {
            host = Assert.IsType<NativeHostSession>(llm.Session).ProcessId!.Value;
            Assert.IsType<InferenceSession>(vision.Session);
        }

        Assert.Equal(2, cache.Loaded.Count);
        using Process process = Process.GetProcessById(host);

        Assert.Equal(2, cache.UnloadIdle());

        Assert.Empty(cache.Loaded);
        Assert.True(process.HasExited);
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
