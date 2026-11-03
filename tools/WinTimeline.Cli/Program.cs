using System.Reflection;

// The headless tool, with no command yet (WT147 adds open, validate and render). It answers
// --version and --help, and refuses anything else with exit code 2, as the app does.
string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

switch (args)
{
    case ["--version"]:
        Console.WriteLine($"wintimeline {version}");
        return 0;
    case [] or ["--help"]:
        Console.WriteLine($"wintimeline {version}");
        Console.WriteLine("Usage: wintimeline --version | --help");
        Console.WriteLine("No commands yet: open, validate and render arrive with the command-line tool.");
        return 0;
    default:
        Console.Error.WriteLine($"Unknown arguments: {string.Join(' ', args)}. See wintimeline --help.");
        return 2;
}
