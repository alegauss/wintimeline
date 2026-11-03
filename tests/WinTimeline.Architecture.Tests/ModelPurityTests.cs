using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT2: the model stays pure so it is tested headless and reused whole by the CLI. It references
/// nothing but the BCL and targets no Windows framework, and its public types have no setter
/// outside Model.Commands, so the document changes through the command pipeline alone (WT16).
/// </summary>
public sealed class ModelPurityTests
{
    private static readonly string ModelProject =
        Path.Combine(Repository.Root, "src", "WinTimeline.Model", "WinTimeline.Model.csproj");

    // Loaded by name: the project reference copies it beside this assembly, and the model may
    // have no type yet for a typeof to name.
    private static Assembly ModelAssembly => Assembly.Load(new AssemblyName("WinTimeline.Model"));

    [Fact]
    public void TheModelReferencesNothingButTheBcl()
    {
        XDocument model = XDocument.Load(ModelProject);

        IReadOnlyList<string> violations = [.. Rules.Purity("WinTimeline.Model", model, anyOs: true), .. Rules.Layering("WinTimeline.Model", model, [])];

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TheModelsAssemblyLoadsNoWindowsAssembly()
    {
        // What the compiled model actually references, beyond what its project file says.
        string[] forbidden = ["PresentationCore", "PresentationFramework", "WindowsBase", "System.Windows.Forms", "Vortice", "Microsoft.Windows.SDK.NET"];
        string[] referenced = ModelAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(referenced.Length == 0, "WinTimeline.Model references " + string.Join(", ", referenced));
    }

    [Fact]
    public void TheModelHasNoPublicSetterOutsideItsCommands()
    {
        IReadOnlyList<string> violations = Rules.SettersOutsideCommands(ModelAssembly.GetExportedTypes());

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void APublicSetterIsNamedAndAnInitIsNot()
    {
        Assert.Equal(
            [$"{typeof(BrokenClip).FullName}.Name has a public setter; the document changes through a command in Model.Commands."],
            Rules.SettersOutsideCommands([typeof(BrokenClip)]));
    }

    [Fact]
    public void APackageInTheModelIsNamed()
    {
        XDocument broken = XDocument.Parse("""
            <Project>
              <PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Vortice.Direct3D11" Version="3.8.3" /></ItemGroup>
            </Project>
            """);

        Assert.Equal(
            [
                "WinTimeline.Model references the package Vortice.Direct3D11; it may use the BCL alone.",
                "WinTimeline.Model targets net10.0-windows; it must build on any OS, so net10.0 alone.",
            ],
            Rules.Purity("WinTimeline.Model", broken, anyOs: true));
    }

    /// <summary>A model type the way the rule forbids: one property settable from anywhere, one only at construction.</summary>
    public sealed class BrokenClip
    {
        public string Name { get; set; } = string.Empty;

        public double Rate { get; init; }
    }
}
