using System.IO;
using System.Xml.Linq;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT2: the analyzer gate. Directory.Build.props turns warnings into errors and the performance
/// analyzers on for every project; these fail when the root file stops doing so, or when one
/// project file quietly turns a gate back off for itself.
/// </summary>
public sealed class BuildGateTests
{
    private static readonly string[] Gated =
    [
        "TreatWarningsAsErrors",
        "Nullable",
        "EnableNETAnalyzers",
        "AnalysisLevel",
        "AnalysisModePerformance",
        "PlatformTarget",
        "WarningsNotAsErrors",
        "RunAnalyzers",
    ];

    public static TheoryData<string, string> RootGates => new()
    {
        { "TreatWarningsAsErrors", "true" },
        { "Nullable", "enable" },
        { "EnableNETAnalyzers", "true" },
        { "AnalysisLevel", "latest" },
        { "AnalysisModePerformance", "All" },
        { "PlatformTarget", "x64" },
    };

    [Theory]
    [MemberData(nameof(RootGates))]
    public void RootPropsSetsTheGate(string property, string value)
    {
        string? actual = XDocument.Load(Path.Combine(Repository.Root, "Directory.Build.props"))
            .Descendants(property)
            .Select(element => element.Value.Trim())
            .LastOrDefault();

        Assert.Equal(value, actual);
    }

    [Fact]
    public void NoProjectOverridesAGate()
    {
        string[] overrides = Directory.GetFiles(Repository.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Skipped(Path.GetRelativePath(Repository.Root, path)))
            .SelectMany(path => Overrides(Path.GetRelativePath(Repository.Root, path), XDocument.Load(path)))
            .ToArray();

        Assert.True(overrides.Length == 0, "Gates are set in Directory.Build.props only:\n" + string.Join("\n", overrides));
    }

    [Fact]
    public void AProjectTurningAGateOffIsNamed()
    {
        XDocument broken = XDocument.Parse("""<Project><PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Project>""");

        Assert.Equal(["src/X/X.csproj: TreatWarningsAsErrors"], Overrides("src/X/X.csproj", broken));
    }

    private static IEnumerable<string> Overrides(string relative, XDocument project) =>
        project.Descendants()
            .Where(element => Gated.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(element => $"{relative.Replace('\\', '/')}: {element.Name.LocalName}");

    // Build output, and the third-party checkouts the spikes clone into .cache (WT11).
    private static bool Skipped(string relative)
    {
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj", StringComparer.Ordinal) || parts.Contains("bin", StringComparer.Ordinal) || parts[0] == ".cache";
    }
}
