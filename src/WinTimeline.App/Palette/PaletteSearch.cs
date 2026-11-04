using System.Globalization;

namespace WinTimeline.App.Palette;

/// <summary>A command the app can run, as the palette and the menus know it.</summary>
/// <param name="Id">Its stable name, e.g. "file.open"; the menu item it backs carries the same in its automation id.</param>
/// <param name="Title">What it is called, e.g. "Open…".</param>
/// <param name="Menu">The menu it is in, e.g. "File".</param>
/// <param name="Synonyms">Other words someone may type for it.</param>
/// <param name="Gesture">Its shortcut, e.g. "Ctrl+O", or null.</param>
public sealed record AppCommand(string Id, string Title, string Menu, IReadOnlyList<string> Synonyms, string? Gesture);

/// <summary>What kind of thing a palette result is.</summary>
public enum PaletteKind
{
    /// <summary>A command.</summary>
    Command,

    /// <summary>A clip in the shown timeline: choosing it moves the playhead there.</summary>
    Clip,

    /// <summary>A marker: choosing it moves the playhead there.</summary>
    Marker,

    /// <summary>A timecode: choosing it moves the playhead there.</summary>
    Timecode,

    /// <summary>A question for the assistant.</summary>
    Ask,
}

/// <summary>One result: what it is, how it reads, and what choosing it does.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Title">The line it shows.</param>
/// <param name="Detail">The second column: a menu and shortcut, or a timecode.</param>
/// <param name="CommandId">The command it runs, for a command.</param>
/// <param name="Seconds">Where it moves the playhead, for a clip, marker or timecode.</param>
public sealed record PaletteResult(PaletteKind Kind, string Title, string Detail, string? CommandId = null, double Seconds = 0)
{
    /// <summary>How the result reads in the list, e.g. "Open… — File, Ctrl+O".</summary>
    public string Text => Detail.Length == 0 ? Title : $"{Title} — {Detail}";
}

/// <summary>A clip or marker the palette can jump to.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Seconds">Where it starts on the timeline.</param>
/// <param name="Where">Its lane and timecode, as the result shows it.</param>
public sealed record PalettePlace(string Name, double Seconds, string Where);

/// <summary>What the palette searches.</summary>
/// <param name="Commands">Every registered command.</param>
/// <param name="Recent">Command ids, most recent first.</param>
/// <param name="Clips">The shown timeline's clips.</param>
/// <param name="Markers">Its markers.</param>
/// <param name="Timecode">Parses a typed timecode into seconds on the shown timeline, or null.</param>
public sealed record PaletteSources(
    IReadOnlyList<AppCommand> Commands,
    IReadOnlyList<string> Recent,
    IReadOnlyList<PalettePlace> Clips,
    IReadOnlyList<PalettePlace> Markers,
    Func<string, double?> Timecode);

/// <summary>
/// WT66: what the command palette offers for what was typed. A prefix narrows it: '&gt;' to
/// commands, '@' to clips, '#' to markers, '?' asks the assistant. Without one, a timecode jumps
/// and everything else is ranked together by a fuzzy match; nothing typed lists recent commands
/// first, then the rest by menu.
/// </summary>
public static class PaletteSearch
{
    /// <summary>The most results shown.</summary>
    public const int Limit = 50;

    /// <summary>Ranks what the palette offers for a query.</summary>
    /// <param name="typed">What was typed.</param>
    /// <param name="sources">What is searched.</param>
    /// <returns>The results, best first.</returns>
    public static IReadOnlyList<PaletteResult> Search(string typed, PaletteSources sources)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(sources);
        string text = typed.Trim();
        char prefix = text.Length > 0 && ">@#?".Contains(text[0], StringComparison.Ordinal) ? text[0] : '\0';
        string query = prefix == '\0' ? text : text[1..].Trim();

        if (prefix == '?')
        {
            return query.Length == 0 ? [] : [new PaletteResult(PaletteKind.Ask, $"Ask: {query}", "the assistant arrives with Block L")];
        }

        if (query.Length == 0)
        {
            return prefix switch
            {
                '@' => [.. sources.Clips.Take(Limit).Select(Place(PaletteKind.Clip))],
                '#' => [.. sources.Markers.Take(Limit).Select(Place(PaletteKind.Marker))],
                _ => [.. Browse(sources)],
            };
        }

        List<(int Score, PaletteResult Result)> ranked = [];
        if (prefix == '\0' && sources.Timecode(query) is { } seconds)
        {
            ranked.Add((int.MaxValue, new PaletteResult(PaletteKind.Timecode, $"Go to {query}", "timecode", Seconds: seconds)));
        }

        if (prefix is '\0' or '>')
        {
            foreach (AppCommand command in sources.Commands)
            {
                int? best = new[] { command.Title, command.Menu + " " + command.Title }.Concat(command.Synonyms)
                    .Select(candidate => Score(query, candidate)).Max();
                if (best is { } score)
                {
                    // A recent command wins a tie with one never used.
                    int recency = sources.Recent.ToList().IndexOf(command.Id);
                    ranked.Add((score * 4 + (recency < 0 ? 0 : 3 - Math.Min(recency, 3)), Command(command)));
                }
            }
        }

        if (prefix is '\0' or '@')
        {
            ranked.AddRange(Places(query, sources.Clips, PaletteKind.Clip, prefix == '@' ? 0 : -8));
        }

        if (prefix is '\0' or '#')
        {
            ranked.AddRange(Places(query, sources.Markers, PaletteKind.Marker, prefix == '#' ? 0 : -8));
        }

        return [.. ranked.OrderByDescending(r => r.Score).ThenBy(r => r.Result.Title, StringComparer.CurrentCultureIgnoreCase).Take(Limit).Select(r => r.Result)];
    }

    /// <summary>
    /// How well a query matches a candidate, or null when its letters do not all appear in order.
    /// Letters that follow one another, that start a word, or that start the candidate score more.
    /// </summary>
    /// <param name="query">What was typed.</param>
    /// <param name="candidate">What it is matched against.</param>
    /// <returns>The score, or null.</returns>
    public static int? Score(string query, string candidate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);
        int score = 0;
        int at = 0;
        int previous = -2;
        foreach (char wanted in query.Where(c => !char.IsWhiteSpace(c)))
        {
            int found = -1;
            for (int i = at; i < candidate.Length; i++)
            {
                if (char.ToUpperInvariant(candidate[i]) == char.ToUpperInvariant(wanted))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            score += 1;
            if (found == previous + 1)
            {
                score += 5;
            }

            if (found == 0)
            {
                score += 8;
            }
            else if (!char.IsLetterOrDigit(candidate[found - 1]))
            {
                score += 6;
            }

            previous = found;
            at = found + 1;
        }

        // A shorter candidate with the same letters is the closer match.
        return score * 8 - Math.Min(candidate.Length, 60) / 4;
    }

    private static IEnumerable<PaletteResult> Browse(PaletteSources sources)
    {
        Dictionary<string, AppCommand> byId = sources.Commands.ToDictionary(c => c.Id, StringComparer.Ordinal);
        IEnumerable<AppCommand> recent = sources.Recent.Where(byId.ContainsKey).Select(id => byId[id]);
        IEnumerable<AppCommand> rest = sources.Commands.Where(c => !sources.Recent.Contains(c.Id, StringComparer.Ordinal));
        return recent.Concat(rest).Select(Command);
    }

    private static PaletteResult Command(AppCommand command) =>
        new(PaletteKind.Command, command.Title, command.Gesture is { } g ? $"{command.Menu}, {g}" : command.Menu, command.Id);

    private static Func<PalettePlace, PaletteResult> Place(PaletteKind kind) =>
        place => new PaletteResult(kind, place.Name, place.Where, Seconds: place.Seconds);

    private static IEnumerable<(int, PaletteResult)> Places(string query, IReadOnlyList<PalettePlace> places, PaletteKind kind, int penalty) =>
        places.Select(p => (Score(query, p.Name), p)).Where(s => s.Item1 is not null)
            .Select(s => ((s.Item1!.Value * 4) + penalty, Place(kind)(s.p)));

    /// <summary>Formats seconds for a result's detail, for callers without a timeline.</summary>
    /// <param name="seconds">The time.</param>
    /// <returns>e.g. "12.500 s".</returns>
    public static string Seconds(double seconds) => string.Create(CultureInfo.InvariantCulture, $"{seconds:F3} s");
}
