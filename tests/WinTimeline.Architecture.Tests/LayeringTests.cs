using System.IO;
using System.Xml.Linq;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT2: the direction of the project references, read out of the .csproj files. The model is the
/// bottom layer and stands on nothing; the media, render and inference layers stand below the
/// engine, the exporter on the engine, and only the app sees everything. A reference that inverts
/// this fails here rather than in a review somebody has to remember.
/// </summary>
public sealed class LayeringTests
{
    private const string Model = "WinTimeline.Model";
    private const string Media = "WinTimeline.Media";
    private const string Render = "WinTimeline.Render";
    private const string Inference = "WinTimeline.Inference";
    private const string Engine = "WinTimeline.Engine";
    private const string Export = "WinTimeline.Export";
    private const string Diagnostics = "WinTimeline.Diagnostics";

    // project, the projects it is allowed to reference. Decoders and inference share the render
    // layer's one D3D11 device (WT3), so both may stand on Render. The log (WT6) is below every
    // layer but the model, which stays pure.
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        [Model] = [],
        [Diagnostics] = [],
        [Render] = [Diagnostics],
        [Media] = [Model, Render, Diagnostics],
        [Inference] = [Render, Diagnostics],
        [Engine] = [Model, Media, Render, Inference, Diagnostics],
        [Export] = [Model, Media, Render, Engine, Diagnostics],
        ["WinTimeline.App"] = [Model, Media, Render, Inference, Engine, Export, Diagnostics],
    };

    // Only these may reference a UI framework: the shell, and the render layer's surface host (WT4).
    private static readonly string[] MayUseUi = ["WinTimeline.App", Render];

    public static TheoryData<string> SourceProjects => [.. Allowed.Keys];

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void ProjectReferencesOnlyWhatItsLayerAllows(string project)
    {
        IReadOnlyList<string> violations = Rules.Layering(project, SourceProject(project), Allowed[project]);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void OnlyTheAppAndTheRenderLayerReferenceAUiFramework(string project)
    {
        IReadOnlyList<string> violations = MayUseUi.Contains(project, StringComparer.Ordinal)
            ? []
            : Rules.UiFramework(project, SourceProject(project));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TheLogStandsOnTheBclAloneOnAnyOs()
    {
        // WT6: every layer writes to it, so it must pull nothing in after it.
        IReadOnlyList<string> violations = Rules.Purity(Diagnostics, SourceProject(Diagnostics), anyOs: true);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TheCommandLineToolNeverReferencesTheApp()
    {
        XDocument cli = XDocument.Load(Path.Combine(Repository.Root, "tools", "WinTimeline.Cli", "WinTimeline.Cli.csproj"));

        Assert.DoesNotContain("WinTimeline.App", Rules.ProjectReferences(cli));
        Assert.Empty(Rules.UiFramework("WinTimeline.Cli", cli));
    }

    [Fact]
    public void EverySourceProjectHasALayer()
    {
        string[] onDisk = Directory.GetDirectories(Path.Combine(Repository.Root, "src"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Allowed.Keys.Order(StringComparer.Ordinal), onDisk);
    }

    [Fact]
    public void EveryProjectIsInTheSolution()
    {
        string[] inSolution = XDocument.Load(Path.Combine(Repository.Root, "WinTimeline.slnx"))
            .Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        // spike/ holds measurements kept to re-run (WT11), never built with the app.
        string[] onDisk = Directory.GetFiles(Repository.Root, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal) || path.StartsWith("tools/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(onDisk, inSolution);
    }

    [Fact]
    public void TheUiCasesReferenceNeitherTheAppNorWpf()
    {
        // A case asserts against what a user launches, not against types it was compiled with.
        XDocument cases = XDocument.Load(Path.Combine(Repository.Root, "tests", "WinTimeline.Cases", "WinTimeline.Cases.csproj"));

        Assert.Empty(Rules.ProjectReferences(cases));
        Assert.Empty(Rules.UiFramework("WinTimeline.Cases", cases));
        Assert.Equal(["Winwright"], cases.Descendants("PackageReference").Select(p => p.Attribute("Include")?.Value));
    }

    [Fact]
    public void AnInvertedReferenceIsNamed()
    {
        // The rule itself, on a model project that reaches up into the engine.
        XDocument broken = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\WinTimeline.Engine\WinTimeline.Engine.csproj" /></ItemGroup>
            </Project>
            """);

        Assert.Equal(
            ["WinTimeline.Model references WinTimeline.Engine, which inverts the layering; it may reference nothing."],
            Rules.Layering(Model, broken, Allowed[Model]));
    }

    [Fact]
    public void AUiFrameworkOutsideTheAppIsNamed()
    {
        XDocument broken = XDocument.Parse("""<Project><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>""");

        Assert.Equal(
            ["WinTimeline.Engine sets UseWPF; only the app and the render surfaces may reference a UI framework."],
            Rules.UiFramework(Engine, broken));
    }

    private static XDocument SourceProject(string project) =>
        XDocument.Load(Path.Combine(Repository.Root, "src", project, project + ".csproj"));
}
