// Named, because the implicit usings of a Windows-targeted project do not include them all.
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace WinTimeline.Architecture.Tests;

/// <summary>
/// WT2: each architecture rule as a function from what it reads to the violations it finds, each
/// one a sentence naming the offending reference. The tests apply them to this repository, and
/// to a deliberately broken input, so a rule that can no longer fail is caught too.
/// </summary>
internal static class Rules
{
    /// <summary>The project references a project file makes that its layer does not allow.</summary>
    public static IReadOnlyList<string> Layering(string project, XDocument file, IReadOnlyCollection<string> allowed)
    {
        return ProjectReferences(file)
            .Where(reference => !allowed.Contains(reference, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(reference =>
                $"{project} references {reference}, which inverts the layering; it may reference "
                + (allowed.Count == 0 ? "nothing." : string.Join(", ", allowed) + "."))
            .ToArray();
    }

    /// <summary>
    /// A project that must stay pure: no package or framework reference, no WPF or Windows Forms,
    /// and, where it must build on any OS, no Windows target framework.
    /// </summary>
    public static IReadOnlyList<string> Purity(string project, XDocument file, bool anyOs)
    {
        List<string> found = [];
        found.AddRange(file.Descendants("PackageReference")
            .Select(p => $"{project} references the package {p.Attribute("Include")?.Value}; it may use the BCL alone."));
        found.AddRange(file.Descendants("FrameworkReference")
            .Select(p => $"{project} references the framework {p.Attribute("Include")?.Value}; it may use the BCL alone."));
        found.AddRange(file.Descendants()
            .Where(e => e.Name.LocalName is "UseWPF" or "UseWindowsForms" && e.Value.Trim() == "true")
            .Select(e => $"{project} sets {e.Name.LocalName}; it must not reference a UI framework."));
        if (anyOs)
        {
            found.AddRange(file.Descendants()
                .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks" && e.Value.Contains("-windows", StringComparison.Ordinal))
                .Select(e => $"{project} targets {e.Value.Trim()}; it must build on any OS, so net10.0 alone."));
        }

        return found;
    }

    /// <summary>The UI framework switches a project file sets.</summary>
    public static IReadOnlyList<string> UiFramework(string project, XDocument file) =>
        file.Descendants()
            .Where(e => e.Name.LocalName is "UseWPF" or "UseWindowsForms" && e.Value.Trim() == "true")
            .Select(e => $"{project} sets {e.Name.LocalName}; only the app and the render surfaces may reference a UI framework.")
            .ToArray();

    /// <summary>
    /// Public setters on the model's public types outside its Commands namespace: the document
    /// changes through the command pipeline (WT16) alone. An init accessor is construction, not
    /// mutation, and is allowed.
    /// </summary>
    public static IReadOnlyList<string> SettersOutsideCommands(IEnumerable<Type> exported)
    {
        return exported
            .Where(type => type.Namespace is not { } ns || !ns.Split('.').Contains("Commands", StringComparer.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInit(setter))
                .Select(property => $"{type.FullName}.{property.Name} has a public setter; the document changes through a command in Model.Commands."))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] ProjectReferences(XDocument file) =>
        file.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();

    private static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
