using WinTimeline.Diagnostics;

namespace WinTimeline.App;

/// <summary>What the command line asked for. Every field null or default means "as a user would start it".</summary>
/// <param name="Open">A project or media file to open at start, instead of the empty window.</param>
/// <param name="Models">The AI models folder, instead of the default.</param>
/// <param name="Adapter">The GPU to run on, by index or by a part of its name, instead of the one the app picks.</param>
/// <param name="LogLevel">How much the log records.</param>
/// <param name="CrashTest">WT7: "managed" or "native" to crash on purpose once the window is up, or null.</param>
/// <param name="RunCommands">WT17: a file of edit commands, one JSON object a line, to apply to the opened document.</param>
/// <param name="EdlRate">WT158: the frame rate an EDL given with --open is read at, so no dialog asks.</param>
public sealed record AppOptions(
    string? Open = null,
    string? Models = null,
    string? Adapter = null,
    LogLevel LogLevel = LogLevel.Info,
    string? CrashTest = null,
    string? RunCommands = null,
    double? EdlRate = null);

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
    private static readonly string[] Flags = ["--open", "--models", "--adapter", "--log-level", "--crash-test", "--run-commands", "--edl-rate"];

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
                    ? UiText.UnknownOption(flag, string.Join(", ", Flags))
                    : UiText.UnexpectedArgument(arg));
            }

            if (value is null)
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Refuse(UiText.NeedsAValue(flag));
                }

                value = args[++i];
            }

            if (value.Length == 0)
            {
                return Refuse(UiText.NeedsAValue(flag));
            }

            if (!given.TryAdd(flag, value))
            {
                return Refuse(UiText.GivenTwice(flag));
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
                return Refuse(UiText.BadLogLevel(levelValue));
            }

            level = parsed.Value;
        }

        string? crash = given.GetValueOrDefault("--crash-test");
        if (crash is not null and not "managed" and not "native")
        {
            return Refuse(UiText.BadCrashTest(crash));
        }

        double? edlRate = null;
        if (given.TryGetValue("--edl-rate", out string? rateValue))
        {
            if (!double.TryParse(rateValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate) || rate <= 0 || rate > 1000)
            {
                return Refuse(UiText.BadEdlRate(rateValue));
            }

            edlRate = rate;
        }

        return new ParsedCommandLine(
            new AppOptions(
                Open: given.GetValueOrDefault("--open"),
                Models: given.GetValueOrDefault("--models"),
                Adapter: given.GetValueOrDefault("--adapter"),
                LogLevel: level,
                CrashTest: crash,
                RunCommands: given.GetValueOrDefault("--run-commands"),
                EdlRate: edlRate),
            null);
    }

    private static ParsedCommandLine Refuse(string message) => new(null, message);
}
