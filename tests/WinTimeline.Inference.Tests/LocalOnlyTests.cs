using System.IO;
using System.Net;
using WinTimeline.Inference.Network;

namespace WinTimeline.Inference.Tests;

/// <summary>WT103: nothing leaves the PC but a model download or the update check, and what ran is logged.</summary>
public sealed class LocalOnlyTests
{
    [Fact]
    public async Task AHostThatIsNeitherAModelHostNorTheUpdateFeedIsRefusedBeforeAByteIsSent()
    {
        Recorder sent = new();
        using HttpClient client = new(new GuardedHttp([.. GuardedHttp.ModelHosts, .. GuardedHttp.UpdateHosts], () => false, sent));

        NetworkRefusedException refused = await Assert.ThrowsAsync<NetworkRefusedException>(() =>
            client.PostAsync("https://api.example-ai.com/v1/transcribe", new StringContent("the interview's audio"), TestContext.Current.CancellationToken));

        Assert.Equal("WinTimeline does not send anything to api.example-ai.com: api.example-ai.com is neither a model host nor the update feed.", refused.Message);
        Assert.Empty(sent.Requests);
    }

    [Fact]
    public async Task AModelDownloadFollowsItsRedirectToTheCdnAndEveryHopIsChecked()
    {
        Recorder sent = new()
        {
            Answers =
            {
                ["huggingface.co"] = Redirect("https://us.aws.cdn.hf.co/xet-bridge-us/abc"),
                ["us.aws.cdn.hf.co"] = new HttpResponseMessage(HttpStatusCode.OK),
            },
        };
        using HttpClient client = new(new GuardedHttp(GuardedHttp.ModelHosts, () => false, sent));

        using HttpResponseMessage response = await client.GetAsync("https://huggingface.co/onnx-community/silero-vad/resolve/e71c/onnx/model_int8.onnx", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["huggingface.co", "us.aws.cdn.hf.co"], sent.Requests.Select(u => u.Host));
    }

    [Fact]
    public async Task ARedirectToAnotherHostIsRefusedAtThatHop()
    {
        Recorder sent = new() { Answers = { ["huggingface.co"] = Redirect("https://tracker.example.net/collect") } };
        using HttpClient client = new(new GuardedHttp(GuardedHttp.ModelHosts, () => false, sent));

        await Assert.ThrowsAsync<NetworkRefusedException>(() => client.GetAsync("https://huggingface.co/x/y/resolve/z/f", TestContext.Current.CancellationToken));
        Assert.Equal(["huggingface.co"], sent.Requests.Select(u => u.Host));
    }

    [Fact]
    public async Task AirGappedModeRefusesEvenAModelHostAndPlainHttpIsNeverAllowed()
    {
        bool airGapped = true;
        Recorder sent = new() { Answers = { ["huggingface.co"] = new HttpResponseMessage(HttpStatusCode.OK) } };
        using HttpClient client = new(new GuardedHttp(GuardedHttp.ModelHosts, () => airGapped, sent));

        Assert.Contains("air-gapped", (await Assert.ThrowsAsync<NetworkRefusedException>(() => client.GetAsync("https://huggingface.co/a", TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        airGapped = false;
        Assert.Contains("http is not https", (await Assert.ThrowsAsync<NetworkRefusedException>(() => client.GetAsync("http://huggingface.co/a", TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.Empty(sent.Requests);
    }

    [Theory]
    [InlineData("huggingface.co", true)]
    [InlineData("cdn-lfs.huggingface.co", true)]
    [InlineData("cas-bridge.xethub.hf.co", true)]
    [InlineData("evilhuggingface.co", false)]
    [InlineData("hf.co.example.com", false)]
    public void AWildcardMatchesSubdomainsOnly(string host, bool allowed)
    {
        Assert.Equal(allowed, GuardedHttp.Allows(host, GuardedHttp.ModelHosts));
    }

    [Fact]
    public async Task EachInferenceIsLoggedByNameAndOutcomeAndNeverByContent()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("wt-activity-").FullName, "ai-activity.jsonl");
        AiActivity log = new(path);

        int words = await log.TrackAsync("transcription", "whisper-base/base", "CPU", @"D:\shoot\interview.mov", () => Task.FromResult(42));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            log.TrackAsync<int>("semantic-search", "clip-vit-b32/fp16", "DirectML", null, () => throw new InvalidOperationException("the model is not installed")));

        Assert.Equal(42, words);
        IReadOnlyList<AiActivityRecord> records = log.Read();
        Assert.Equal(["semantic-search", "transcription"], records.Select(r => r.Feature));
        Assert.Equal(("interview.mov", "ok"), (records[1].Media, records[1].Outcome));
        Assert.Equal("the model is not installed", records[0].Outcome);
        Assert.DoesNotContain(@"D:\shoot", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    /// <summary>Stands in for the socket: records each request and answers by host.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public Dictionary<string, HttpResponseMessage> Answers { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(Answers.TryGetValue(request.RequestUri!.Host, out HttpResponseMessage? answer) ? answer : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
