using WinTimeline.Diagnostics;

namespace WinTimeline.App;

/// <summary>What the command line asked for. Every field null or default means "as a user would start it".</summary>
/// <param name="Open">A project or media file to open at start, instead of the empty window.</param>
/// <param name="Models">The AI models folder, instead of the default.</param>
/// <param name="Adapter">The GPU to run on, by index or by a part of its name, instead of the one the app picks.</param>
/// <param name="LogLevel">How much the log records.</param>
public sealed record AppOptions(
    string? Open = null,
    string? Models = null,
    string? Adapter = null,
    LogLevel LogLevel = LogLevel.Info);

/// <summary>A parse: the options, or why the command line was refused.</summary>
/// <param name="Options">What was asked for, when it parsed.</param>
/// <param name="Error">The message for the user, when it did not.</param>
public sealed record ParsedCommandLine(AppOptions? Options, string? Error)
{
    /// <summary>Whether the command line was accepted.</summary>
    public bool Ok => Options is not null;
}

/// <summary>
/// The command line, parsed in one place from the first day (WT1), in the sibling apps' style: a
/// winwright case reaches a state through a flag rather than a file dialog. A flag it does not
/// know, a value missing or out of range, or a flag given twice is refused with a message that
/// names it, never ignored. docs/COMMAND-LINE.md is the user's description.
/// </summary>
public static class CommandLine
{
    private static readonly string[] Flags = ["--open", "--models", "--adapter", "--log-level"];

    /// <summary>Parses the arguments after the executable's name.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options, or the refusal.</returns>
    public static ParsedCommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Dictionary<string, string> given = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string flag = arg;
            string? value = null;
            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                flag = arg[..equals];
                value = arg[(equals + 1)..];
            }

            if (!Flags.Contains(flag, StringComparer.Ordinal))
            {
                return Refuse(arg.StartsWith('-')
                    ? $"Unknown option {flag}. Known: {string.Join(", ", Flags)}."
                    : $"Unexpected argument \"{arg}\". To open a file, use --open \"{arg}\".");
            }

            if (value is null)
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Refuse($"{flag} needs a value.");
                }

                value = args[++i];
            }

            if (value.Length == 0)
            {
                return Refuse($"{flag} needs a value.");
            }

            if (!given.TryAdd(flag, value))
            {
                return Refuse($"{flag} is given twice.");
            }
        }

        LogLevel level = LogLevel.Info;
        if (given.TryGetValue("--log-level", out string? levelValue))
        {
            LogLevel? parsed = levelValue.ToUpperInvariant() switch
            {
                "TRACE" => LogLevel.Trace,
                "DEBUG" => LogLevel.Debug,
                "INFO" => LogLevel.Info,
                "WARNING" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                _ => null,
            };
            if (parsed is null)
            {
                return Refuse($"--log-level {levelValue} is not one of trace, debug, info, warning, error.");
            }

            level = parsed.Value;
        }

        return new ParsedCommandLine(
            new AppOptions(
                Open: given.GetValueOrDefault("--open"),
                Models: given.GetValueOrDefault("--models"),
                Adapter: given.GetValueOrDefault("--adapter"),
                LogLevel: level),
            null);
    }

    private static ParsedCommandLine Refuse(string message) => new(null, message);
}
