// Named, because a WPF test project's implicit usings do not include it.
using System.IO;
using WinTimeline.Diagnostics;

namespace WinTimeline.App.Tests;

/// <summary>WT1: every flag parses both ways, and anything else is refused with a message that names it.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void NoArgumentsIsAPlainStart()
    {
        ParsedCommandLine parsed = CommandLine.Parse([]);

        Assert.True(parsed.Ok);
        Assert.Equal(new AppOptions(), parsed.Options);
        Assert.Equal(LogLevel.Info, parsed.Options!.LogLevel);
    }

    [Fact]
    public void EveryFlagTakesItsValueAfterASpaceOrAnEqualsSign()
    {
        AppOptions spaced = CommandLine.Parse(["--open", "cut.otio", "--models", "m", "--adapter", "1", "--log-level", "debug"]).Options!;
        AppOptions joined = CommandLine.Parse(["--open=cut.otio", "--models=m", "--adapter=1", "--log-level=debug"]).Options!;

        AppOptions expected = new(Open: "cut.otio", Models: "m", Adapter: "1", LogLevel: LogLevel.Debug);
        Assert.Equal(expected, spaced);
        Assert.Equal(expected, joined);
    }

    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("INFO", LogLevel.Info)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    public void LogLevelIsReadWhateverItsCase(string value, LogLevel expected)
    {
        Assert.Equal(expected, CommandLine.Parse(["--log-level", value]).Options!.LogLevel);
    }

    [Fact]
    public void AnAdapterIsKeptAsWrittenForTheDeviceToMatch()
    {
        // An index or a part of the adapter's name: the shared device (WT3) resolves it.
        Assert.Equal("NVIDIA", CommandLine.Parse(["--adapter", "NVIDIA"]).Options!.Adapter);
    }

    [Theory]
    [InlineData(new[] { "--frobnicate" }, "Unknown option --frobnicate. Known: --open, --models, --adapter, --log-level.")]
    [InlineData(new[] { "cut.otio" }, "Unexpected argument \"cut.otio\". To open a file, use --open \"cut.otio\".")]
    [InlineData(new[] { "--open" }, "--open needs a value.")]
    [InlineData(new[] { "--open", "--models", "m" }, "--open needs a value.")]
    [InlineData(new[] { "--open=" }, "--open needs a value.")]
    [InlineData(new[] { "--open", "a", "--open", "b" }, "--open is given twice.")]
    [InlineData(new[] { "--log-level", "loud" }, "--log-level loud is not one of trace, debug, info, warning, error.")]
    public void AnythingElseIsRefusedByName(string[] args, string error)
    {
        ParsedCommandLine parsed = CommandLine.Parse(args);

        Assert.False(parsed.Ok);
        Assert.Equal(error, parsed.Error);
    }

    [Fact]
    public void TheUserDocumentNamesEveryFlag()
    {
        string document = File.ReadAllText(Path.Combine(Repository.Root, "docs", "COMMAND-LINE.md"));

        foreach (string flag in new[] { "--open", "--models", "--adapter", "--log-level" })
        {
            Assert.Contains($"`{flag}", document, StringComparison.Ordinal);
        }
    }
}
