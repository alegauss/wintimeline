namespace WinTimeline.App.Palette;

/// <summary>
/// WT66: every command the app has, once. Each one backs the menu item whose automation id it
/// names, which the palette runs as if the item were chosen, so a command added to a menu and
/// missing here (or the other way round) fails the test that compares the two.
/// </summary>
public static class CommandTable
{
    /// <summary>The commands, in menu order, each with the menu item it backs.</summary>
    public static IReadOnlyList<(AppCommand Command, string MenuItem)> All { get; } =
    [
        (new("file.open", "Open…", "File", ["load", "import", "edl"], "Ctrl+O"), "OpenMenuItem"),
        (new("file.save", "Save", "File", ["write"], "Ctrl+S"), "SaveMenuItem"),
        (new("file.save-as", "Save As…", "File", ["copy", "bundle", "otioz"], "Ctrl+Shift+S"), "SaveAsMenuItem"),
        (new("file.relink", "Relink media…", "File", ["missing media", "offline", "reconnect"], null), "RelinkMenuItem"),
        (new("file.exit", "Exit", "File", ["quit", "close"], "Alt+F4"), "ExitMenuItem"),
        (new("edit.undo", "Undo", "Edit", ["back"], "Ctrl+Z"), "UndoMenuItem"),
        (new("edit.redo", "Redo", "Edit", ["again"], "Ctrl+Y"), "RedoMenuItem"),
        (new("edit.duplicate-timeline", "Duplicate timeline as version", "Edit", ["new version", "copy timeline"], null), "DuplicateTimelineMenuItem"),
        (new("view.palette", "Command palette…", "View", ["search", "find command"], "Ctrl+Shift+P"), "PaletteMenuItem"),
        (new("view.problems", "Problems…", "View", ["validate", "errors", "check"], null), "ProblemsMenuItem"),
        (new("view.compare", "Compare with another version…", "View", ["diff", "changes", "what changed"], null), "CompareMenuItem"),
        (new("help.diagnostics", "Create diagnostics bundle…", "Help", ["bug report", "logs", "support"], null), "DiagnosticsMenuItem"),
    ];

    /// <summary>The commands alone.</summary>
    public static IReadOnlyList<AppCommand> Commands { get; } = [.. All.Select(a => a.Command)];
}
