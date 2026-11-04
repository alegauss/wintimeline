using System.IO;
using System.Text.RegularExpressions;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT149: every word the app shows comes from UiText, so the large UI keeps one terminology
/// (OTIO's: clip, item, track, stack, gap, marker) and the winwright cases assert on values from
/// one source. XAML takes them through x:Static, code through UiText's constants and methods.
/// </summary>
public sealed partial class UiTextTests
{
    private static readonly string App = Path.Combine(Repository.Root, "src", "WinTimeline.App");

    [Fact]
    public void NoXamlAttributeShowsALiteralWord()
    {
        string[] found = [.. Files("*.xaml").SelectMany(f => XamlLiterals(File.ReadAllText(f)).Select(l => $"{Path.GetFileName(f)}: {l}"))];

        Assert.True(found.Length == 0, "Move these to UiText and bind them with {x:Static app:UiText.Name}:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void NoCodeBehindHandsALiteralWordToTheScreen()
    {
        string[] found = [.. Files("*.cs")
            .Where(f => Path.GetFileName(f) != "UiText.cs")
            .SelectMany(f => CodeLiterals(File.ReadAllText(f)).Select(l => $"{Path.GetFileName(f)}: {l}"))];

        Assert.True(found.Length == 0, "Move these to UiText:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void TheRulesCatchALiteralAndLetUiTextThrough()
    {
        Assert.Equal(["Header=\"_File\"", "Text=\"Ready\""], XamlLiterals("""<MenuItem Header="_File" /><TextBlock Text="Ready" /><Button Content="{x:Static app:UiText.Close}" /><TextBlock Text="&#xE711;" />"""));
        Assert.Equal([".Text = $\"", "Content = \""], CodeLiterals("""StatusText.Text = $"Saved {name}."; b.Content = "Run"; StatusText.Text = UiText.Saved(name);"""));
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(App, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IReadOnlyList<string> XamlLiterals(string xaml) => [.. XamlWord().Matches(xaml).Select(m => m.Value)];

    private static IReadOnlyList<string> CodeLiterals(string code) => [.. CodeWord().Matches(code).Select(m => m.Value)];

    // A visible attribute whose value is not a markup extension and holds a letter (a glyph entity does not).
    [GeneratedRegex(@"\b(Header|Content|Text|Title|ToolTip|AutomationProperties\.Name|InputGestureText)=""(?!\{)(?!&#)[^""]*[A-Za-z][^""]*""")]
    private static partial Regex XamlWord();

    // A string literal handed straight to something the screen shows.
    [GeneratedRegex(@"(\.Text|\bContent|\bHeader|\bTitle|\bToolTip)\s*=\s*\$?""|new\s+(Notices\.)?Notice\(\s*(Notices\.)?NoticeSeverity\.\w+,\s*\$?""|new\s+(Notices\.)?NoticeAction\(\s*\$?""|SetName\([^,()]+,\s*\$?""")]
    private static partial Regex CodeWord();
}
