using System.IO;
using System.Xml.Linq;
using WinTimeline.App.Palette;

namespace WinTimeline.App.Tests;

/// <summary>WT66: every command is reachable from the palette, which ranks what was typed sensibly.</summary>
public sealed class PaletteTests
{
    private static readonly PaletteSources Sources = new(
        CommandTable.Commands,
        [],
        [new PalettePlace("V1 shot 012", 42, "V1, 01:00:42:00"), new PalettePlace("interview wide", 90, "V2, 01:01:30:00")],
        [new PalettePlace("fix the colour", 12, "V1, 01:00:12:00")],
        text => text == "01:00:04:12" ? 4.5 : null);

    [Fact]
    public void EveryLeafMenuItemIsACommandAndEveryCommandIsAMenuItem()
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XDocument xaml = XDocument.Load(Path.Combine(Repository.Root, "src", "WinTimeline.App", "MainWindow.xaml"));
        string[] leaves = [.. xaml.Descendants(wpf + "MenuItem")
            .Where(m => !m.Elements(wpf + "MenuItem").Any())
            .Select(m => m.Attribute("AutomationProperties.AutomationId")?.Value ?? $"(a menu item headed {m.Attribute("Header")?.Value} has no automation id)")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(leaves, CommandTable.All.Select(a => a.MenuItem).Order(StringComparer.Ordinal));
        Assert.Equal(CommandTable.Commands.Count, CommandTable.Commands.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCommandIsTheFirstResultForItsOwnTitle()
    {
        foreach (AppCommand command in CommandTable.Commands)
        {
            PaletteResult first = PaletteSearch.Search(">" + command.Title, Sources)[0];
            Assert.True(command.Id == first.CommandId, $"'{command.Title}' found {first.Text} first");
        }
    }

    [Fact]
    public void AFewLettersOrASynonymFindTheCommand()
    {
        Assert.Equal("view.compare", PaletteSearch.Search("compar", Sources)[0].CommandId);
        Assert.Equal("view.compare", PaletteSearch.Search("what changed", Sources)[0].CommandId);
        Assert.Equal("file.relink", PaletteSearch.Search("offline", Sources)[0].CommandId);
        Assert.Equal("file.save-as", PaletteSearch.Search("sas", Sources)[0].CommandId);
    }

    [Fact]
    public void PrefixesNarrowToClipsMarkersOrTheAssistantAndATimecodeJumps()
    {
        PaletteResult clip = Assert.Single(PaletteSearch.Search("@interview", Sources));
        Assert.Equal((PaletteKind.Clip, 90.0), (clip.Kind, clip.Seconds));
        Assert.Equal("fix the colour — V1, 01:00:12:00", Assert.Single(PaletteSearch.Search("#colour", Sources)).Text);
        Assert.Equal(PaletteKind.Ask, Assert.Single(PaletteSearch.Search("? where is the wide shot", Sources)).Kind);

        PaletteResult jump = PaletteSearch.Search("01:00:04:12", Sources)[0];
        Assert.Equal((PaletteKind.Timecode, 4.5), (jump.Kind, jump.Seconds));
        Assert.Empty(PaletteSearch.Search(">interview", Sources));
    }

    [Fact]
    public void NothingTypedListsRecentCommandsFirstThenEveryCommand()
    {
        IReadOnlyList<PaletteResult> results = PaletteSearch.Search(string.Empty, Sources with { Recent = ["view.problems", "file.open"] });

        Assert.Equal(["view.problems", "file.open"], results.Take(2).Select(r => r.CommandId));
        Assert.Equal(CommandTable.Commands.Count, results.Count);
        Assert.Equal("Open… — File, Ctrl+O", results[1].Text);
    }

    [Fact]
    public void LettersOutOfOrderDoNotMatchAndAWordStartBeatsAMiddle()
    {
        Assert.Null(PaletteSearch.Score("xq", "Save As…"));
        Assert.True(PaletteSearch.Score("re", "Relink media…") > PaletteSearch.Score("re", "Create diagnostics bundle…"));
    }
}
