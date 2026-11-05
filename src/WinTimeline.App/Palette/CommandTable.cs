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
        (Command("file.open", UiText.OpenMenu, UiText.FileMenu, ["load", "import", "edl"], UiText.OpenGesture), "OpenMenuItem"),
        (Command("file.save", UiText.SaveMenu, UiText.FileMenu, ["write"], UiText.SaveGesture), "SaveMenuItem"),
        (Command("file.save-as", UiText.SaveAsMenu, UiText.FileMenu, ["copy", "bundle", "otioz"], UiText.SaveAsGesture), "SaveAsMenuItem"),
        (Command("file.relink", UiText.RelinkMenu, UiText.FileMenu, ["missing media", "offline", "reconnect"], null), "RelinkMenuItem"),
        (Command("file.exit", UiText.ExitMenu, UiText.FileMenu, ["quit", "close"], UiText.ExitGesture), "ExitMenuItem"),
        (Command("edit.undo", UiText.UndoMenu, UiText.EditMenu, ["back"], UiText.UndoGesture), "UndoMenuItem"),
        (Command("edit.redo", UiText.RedoMenu, UiText.EditMenu, ["again"], UiText.RedoGesture), "RedoMenuItem"),
        (Command("edit.duplicate-timeline", UiText.DuplicateTimelineMenu, UiText.EditMenu, ["new version", "copy timeline"], null), "DuplicateTimelineMenuItem"),
        (Command("edit.split", UiText.SplitMenu, UiText.EditMenu, ["cut", "razor", "blade", "add edit"], UiText.SplitGesture), "SplitMenuItem"),
        (Command("edit.split-all", UiText.SplitAllMenu, UiText.EditMenu, ["cut all", "add edit to all tracks"], UiText.SplitAllGesture), "SplitAllMenuItem"),
        (Command("edit.razor", UiText.RazorMenu, UiText.EditMenu, ["blade tool", "cut tool"], UiText.RazorGesture), "RazorMenuItem"),
        (Command("edit.link", UiText.LinkMenu, UiText.EditMenu, ["link audio", "sync pair"], UiText.LinkGesture), "LinkMenuItem"),
        (Command("edit.unlink", UiText.UnlinkMenu, UiText.EditMenu, ["unlink", "separate audio"], UiText.UnlinkGesture), "UnlinkMenuItem"),
        (Command("edit.group", UiText.GroupMenu, UiText.EditMenu, ["keep together"], UiText.GroupGesture), "GroupMenuItem"),
        (Command("edit.ungroup", UiText.UngroupMenu, UiText.EditMenu, ["break group"], UiText.UngroupGesture), "UngroupMenuItem"),
        (Command("edit.linked-selection", UiText.LinkedSelectionMenu, UiText.EditMenu, ["select linked", "link toggle"], UiText.LinkedSelectionGesture), "LinkedSelectionMenuItem"),
        (Command("edit.move-into-sync", UiText.MoveIntoSyncMenu, UiText.EditMenu, ["out of sync", "resync", "fix sync"], null), "MoveIntoSyncMenuItem"),
        (Command("edit.multicam-timecode", UiText.MulticamTimecodeMenu, UiText.EditMenu, ["multicam timecode", "angles"], null), "MulticamTimecodeMenuItem"),
        (Command("edit.multicam-in-points", UiText.MulticamInPointsMenu, UiText.EditMenu, ["multicam in points", "angles"], null), "MulticamInPointsMenuItem"),
        (Command("edit.angle-1", UiText.Angle1Menu, UiText.EditMenu, ["camera 1", "cut to angle 1"], "1"), "Angle1MenuItem"),
        (Command("edit.angle-2", UiText.Angle2Menu, UiText.EditMenu, ["camera 2", "cut to angle 2"], "2"), "Angle2MenuItem"),
        (Command("edit.angle-3", UiText.Angle3Menu, UiText.EditMenu, ["camera 3", "cut to angle 3"], "3"), "Angle3MenuItem"),
        (Command("edit.angle-4", UiText.Angle4Menu, UiText.EditMenu, ["camera 4", "cut to angle 4"], "4"), "Angle4MenuItem"),
        (Command("edit.make-compound", UiText.MakeCompoundMenu, UiText.EditMenu, ["nest", "compound", "group clips"], null), "MakeCompoundMenuItem"),
        (Command("edit.break-apart", UiText.BreakApartMenu, UiText.EditMenu, ["flatten", "unnest"], null), "BreakApartMenuItem"),
        (Command("edit.add-marker", UiText.AddMarkerMenu, UiText.EditMenu, ["marker", "note", "flag", "edit marker"], UiText.AddMarkerGesture), "AddMarkerMenuItem"),
        (Command("edit.copy-clips", UiText.CopyClipsMenu, UiText.EditMenu, ["copy", "duplicate clips"], UiText.CopyClipsGesture), "CopyClipsMenuItem"),
        (Command("edit.paste-clips", UiText.PasteClipsMenu, UiText.EditMenu, ["paste", "insert clips"], UiText.PasteClipsGesture), "PasteClipsMenuItem"),
        (Command("edit.paste-insert", UiText.PasteInsertMenu, UiText.EditMenu, ["insert paste", "ripple paste"], UiText.PasteInsertGesture), "PasteInsertMenuItem"),
        (Command("edit.paste-attributes", UiText.PasteAttributesMenu, UiText.EditMenu, ["paste effects", "copy look", "match"], UiText.PasteAttributesGesture), "PasteAttributesMenuItem"),
        (Command("edit.select-all", UiText.SelectAllMenu, UiText.EditMenu, ["select everything", "all clips"], UiText.SelectAllGesture), "SelectAllMenuItem"),
        (Command("edit.deselect-all", UiText.DeselectAllMenu, UiText.EditMenu, ["select none", "clear selection"], UiText.DeselectAllGesture), "DeselectAllMenuItem"),
        (Command("edit.track-forward", UiText.TrackForwardMenu, UiText.EditMenu, ["select forward", "everything after"], UiText.TrackForwardGesture), "TrackForwardMenuItem"),
        (Command("edit.track-backward", UiText.TrackBackwardMenu, UiText.EditMenu, ["select backward", "everything before"], UiText.TrackBackwardGesture), "TrackBackwardMenuItem"),
        (Command("view.palette", UiText.PaletteMenu, UiText.ViewMenu, ["search", "find command"], UiText.PaletteGesture), "PaletteMenuItem"),
        (Command("view.ruler-timecode", UiText.RulerTimecodeMenu, UiText.ViewMenu, ["ruler timecode", "smpte"], null), "RulerTimecodeMenuItem"),
        (Command("view.ruler-frames", UiText.RulerFramesMenu, UiText.ViewMenu, ["ruler frames", "frame count"], null), "RulerFramesMenuItem"),
        (Command("view.ruler-feet", UiText.RulerFeetMenu, UiText.ViewMenu, ["ruler feet", "35mm", "film"], null), "RulerFeetMenuItem"),
        (Command("view.ruler-seconds", UiText.RulerSecondsMenu, UiText.ViewMenu, ["ruler seconds"], null), "RulerSecondsMenuItem"),
        (Command("view.heat-cuts", UiText.HeatCutsMenu, UiText.ViewMenu, ["heat cuts", "cut density", "busy"], null), "HeatCutsMenuItem"),
        (Command("view.heat-problems", UiText.HeatProblemsMenu, UiText.ViewMenu, ["heat problems", "errors map"], null), "HeatProblemsMenuItem"),
        (Command("view.heat-markers", UiText.HeatMarkersMenu, UiText.ViewMenu, ["heat markers", "notes map"], null), "HeatMarkersMenuItem"),
        (Command("view.snapping", UiText.SnappingMenu, UiText.ViewMenu, ["snap", "magnetic", "guides"], UiText.SnappingGesture), "SnappingMenuItem"),
        (Command("view.follow-page", UiText.FollowPageMenu, UiText.ViewMenu, ["follow playhead page", "page scroll"], null), "FollowPageMenuItem"),
        (Command("view.follow-smooth", UiText.FollowSmoothMenu, UiText.ViewMenu, ["follow playhead smooth", "centre playhead", "center playhead"], null), "FollowSmoothMenuItem"),
        (Command("view.follow-off", UiText.FollowOffMenu, UiText.ViewMenu, ["follow playhead off", "no follow"], null), "FollowOffMenuItem"),
        (Command("view.zoom-in", UiText.ZoomInMenu, UiText.ViewMenu, ["magnify", "closer"], UiText.ZoomInGesture), "ZoomInMenuItem"),
        (Command("view.zoom-out", UiText.ZoomOutMenu, UiText.ViewMenu, ["farther", "wider"], UiText.ZoomOutGesture), "ZoomOutMenuItem"),
        (Command("view.zoom-fit", UiText.ZoomFitMenu, UiText.ViewMenu, ["whole programme", "fit all", "overview"], UiText.ZoomFitGesture), "ZoomFitMenuItem"),
        (Command("view.problems", UiText.ProblemsMenu, UiText.ViewMenu, ["validate", "errors", "check"], null), "ProblemsMenuItem"),
        (Command("view.compare", UiText.CompareMenu, UiText.ViewMenu, ["diff", "changes", "what changed"], null), "CompareMenuItem"),
        (Command("help.diagnostics", UiText.DiagnosticsMenu, UiText.HelpMenu, ["bug report", "logs", "support"], null), "DiagnosticsMenuItem"),
    ];

    /// <summary>The commands alone.</summary>
    public static IReadOnlyList<AppCommand> Commands { get; } = [.. All.Select(a => a.Command)];

    // A command titled by its menu item's header and its menu's, as the palette shows them: the
    // menu's own words from UiText, without the access-key underscore.
    private static AppCommand Command(string id, string header, string menu, IReadOnlyList<string> synonyms, string? gesture) =>
        new(id, UiText.Plain(header), UiText.Plain(menu), synonyms, gesture);
}
