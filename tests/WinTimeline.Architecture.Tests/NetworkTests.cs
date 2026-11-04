using System.IO;
using System.Text.RegularExpressions;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT103: the local-only promise holds only if GuardedHttp is the one way onto the network. Any
/// other client, socket or web request in the app's source would bypass it, so naming one fails
/// here rather than in a review somebody has to remember.
/// </summary>
public sealed partial class NetworkTests
{
    private const string Guard = "GuardedHttp.cs";

    [Fact]
    public void NothingButTheGuardMakesAnHttpClientOrASocket()
    {
        string[] offenders = [.. Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Repository.Root, "tools"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && Path.GetFileName(f) != Guard)
            .SelectMany(f => Offences(File.ReadAllText(f)).Select(o => $"{Path.GetRelativePath(Repository.Root, f)}: {o}"))];

        Assert.True(offenders.Length == 0, "Only GuardedHttp may reach the network:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheRuleCatchesEachWayOut()
    {
        Assert.Equal(
            ["new HttpClient(", "new SocketsHttpHandler", "WebRequest.Create", "new TcpClient("],
            Offences("var a = new HttpClient(); var b = new SocketsHttpHandler(); var c = WebRequest.Create(u); var d = new TcpClient(h, 1);"));
        Assert.Empty(Offences("HttpClient client = GuardedHttp.CreateClient(() => false);"));
    }

    private static IReadOnlyList<string> Offences(string source) =>
        [.. WaysOut().Matches(source).Select(m => m.Value)];

    [GeneratedRegex(@"new\s+HttpClient\s*\(|new\s+(SocketsHttpHandler|HttpClientHandler|ClientWebSocket)\b|WebRequest\.Create|new\s+(TcpClient|UdpClient|Socket)\s*\(")]
    private static partial Regex WaysOut();
}
