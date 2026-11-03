// Named, because a WPF test project's implicit usings do not include it.
using System.IO;

namespace WinTimeline.Tests;

/// <summary>
/// The repository a test was built from, found once for every test assembly. A test that reads
/// a project file, a fixture or a golden frame does it from here. Linked into every test project
/// from <c>tests/Directory.Build.props</c>.
/// </summary>
internal static class Repository
{
    private static readonly Lazy<string> Found = new(Walk);

    /// <summary>The folder holding <c>WinTimeline.slnx</c>, above wherever the test runs from.</summary>
    /// <exception cref="DirectoryNotFoundException">The test is not running from inside the repository.</exception>
    public static string Root => Found.Value;

    private static string Walk()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WinTimeline.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException(
                   $"WinTimeline.slnx is not above {AppContext.BaseDirectory}, so this test is not running "
                   + "from inside the repository it was built from.");
    }
}
