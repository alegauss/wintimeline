using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WinTimeline.App;

/// <summary>
/// WT149: every word the app shows, in one place, so the UI keeps one terminology (OpenTimelineIO's:
/// clip, item, track, stack, gap, marker) and the UI cases assert on values from one source. XAML
/// binds the constants through <c>{x:Static app:UiText.Name}</c>; code calls the constants and the
/// methods, which build the sentences that carry a name or a count.
/// </summary>
public static class UiText
{
    // ---- The application ----

    /// <summary>The app's name: the window title with no document, the welcome heading and message box captions.</summary>
    public const string AppName = "WinTimeline";

    /// <summary>The name of a timeline that has not been saved yet.</summary>
    public const string Untitled = "Untitled";

    /// <summary>The window title with a document open, e.g. "cut.otio — WinTimeline".</summary>
    /// <param name="name">The document's file name, or <see cref="Untitled"/>.</param>
    /// <returns>The title.</returns>
    public static string DocumentTitle(string name) => $"{name} — {AppName}";

    /// <summary>The message box for a refused command line or <c>--adapter</c>.</summary>
    /// <param name="error">Why it was refused.</param>
    /// <returns>The message.</returns>
    public static string CommandLineRefused(string? error) => error + "\n\nSee docs/COMMAND-LINE.md.";

    /// <summary>What the status bar says when the saved GPU is gone.</summary>
    /// <param name="adapter">The saved adapter, as the settings spell it.</param>
    /// <returns>The sentence.</returns>
    public static string SavedGpuGone(string? adapter) => $"The saved GPU \"{adapter}\" is not on this PC; using the default one.";

    /// <summary>What the message box says after an error nobody expected, offering a restart.</summary>
    /// <param name="message">The error's message.</param>
    /// <returns>The message.</returns>
    public static string UnexpectedError(string message) =>
        $"WinTimeline hit an error it did not expect:\n\n{message}\n\n"
        + "Restart WinTimeline? Choose No to keep working; the error is in the log, and Help > "
        + "Create diagnostics bundle collects it for a report.";

    /// <summary>What the status bar says about a settings file that could not be read.</summary>
    /// <param name="fileName">The settings file's name.</param>
    /// <returns>The sentence.</returns>
    public static string SettingsDamaged(string fileName) => $"Settings could not be read; defaults are in use. The file was kept as {fileName}.broken.";

    /// <summary>What the status bar says about settings a newer version wrote.</summary>
    public const string SettingsNewer = "Settings were saved by a newer WinTimeline; they are read but not changed.";

    // ---- The command line ----

    /// <summary>A refused option that is not one of the known ones.</summary>
    /// <param name="flag">The option.</param>
    /// <param name="known">The known options, joined.</param>
    /// <returns>The sentence.</returns>
    public static string UnknownOption(string flag, string known) => $"Unknown option {flag}. Known: {known}.";

    /// <summary>A refused argument that is not an option.</summary>
    /// <param name="arg">The argument.</param>
    /// <returns>The sentence.</returns>
    public static string UnexpectedArgument(string arg) => $"Unexpected argument \"{arg}\". To open a file, use --open \"{arg}\".";

    /// <summary>An option given with no value.</summary>
    /// <param name="flag">The option.</param>
    /// <returns>The sentence.</returns>
    public static string NeedsAValue(string flag) => $"{flag} needs a value.";

    /// <summary>An option given twice.</summary>
    /// <param name="flag">The option.</param>
    /// <returns>The sentence.</returns>
    public static string GivenTwice(string flag) => $"{flag} is given twice.";

    /// <summary>A <c>--log-level</c> that is not a level.</summary>
    /// <param name="value">What was given.</param>
    /// <returns>The sentence.</returns>
    public static string BadLogLevel(string value) => $"--log-level {value} is not one of trace, debug, info, warning, error.";

    /// <summary>A <c>--crash-test</c> that is not a kind of crash.</summary>
    /// <param name="value">What was given.</param>
    /// <returns>The sentence.</returns>
    public static string BadCrashTest(string value) => $"--crash-test {value} is not one of managed, native.";

    /// <summary>WT158: --edl-rate given something that is not a frame rate.</summary>
    /// <param name="value">What was given.</param>
    /// <returns>The sentence.</returns>
    public static string BadEdlRate(string value) => $"--edl-rate {value} is not a frame rate, such as 25 or 29.97.";

    // ---- EDL options (WT158) ----

    /// <summary>The EDL dialog's title.</summary>
    public const string EdlTitle = "Open EDL";

    /// <summary>What the EDL dialog asks.</summary>
    /// <param name="file">The EDL's file name.</param>
    /// <returns>The sentence.</returns>
    public static string EdlQuestion(string file) => $"{file} does not say its frame rate. Read it at:";

    /// <summary>The frame rate picker's automation name.</summary>
    public const string EdlRateName = "Frame rate";

    /// <summary>The mismatch option.</summary>
    public const string EdlIgnoreMismatch = "Place clips by source timecode where the record timecode disagrees";

    /// <summary>A dialog's Cancel button.</summary>
    public const string Cancel = "Cancel";

    /// <summary>The EDL dialog's Open button.</summary>
    public const string EdlOpen = "Open";

    /// <summary>What the status bar says when the EDL dialog is cancelled.</summary>
    /// <param name="file">The EDL's file name.</param>
    /// <returns>The sentence.</returns>
    public static string EdlCancelled(string file) => $"{file} was not opened.";

    /// <summary>What a refusal for frames beyond the rate adds: the rates the EDL fits.</summary>
    /// <param name="rates">The rates, as labels.</param>
    /// <returns>The sentence, or empty when none fits.</returns>
    public static string EdlFits(IReadOnlyList<string> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        return rates.Count == 0 ? string.Empty : $" Its timecodes fit {string.Join(", ", rates)} fps.";
    }

    // ---- Main window: menus ----

    /// <summary>The File menu.</summary>
    public const string FileMenu = "_File";

    /// <summary>File > Open.</summary>
    public const string OpenMenu = "_Open…";

    /// <summary>File > Save.</summary>
    public const string SaveMenu = "_Save";

    /// <summary>File > Save As.</summary>
    public const string SaveAsMenu = "Save _As…";

    /// <summary>File > Relink media.</summary>
    public const string RelinkMenu = "_Relink media…";

    /// <summary>File > Exit.</summary>
    public const string ExitMenu = "E_xit";

    /// <summary>The Edit menu.</summary>
    public const string EditMenu = "_Edit";

    /// <summary>Edit > Undo.</summary>
    public const string UndoMenu = "_Undo";

    /// <summary>Edit > Redo.</summary>
    public const string RedoMenu = "_Redo";

    /// <summary>Edit > Duplicate timeline as version.</summary>
    public const string DuplicateTimelineMenu = "_Duplicate timeline as version";

    /// <summary>The View menu.</summary>
    public const string ViewMenu = "_View";

    /// <summary>View > Command palette.</summary>
    public const string PaletteMenu = "Command _palette…";

    /// <summary>View > Problems.</summary>
    public const string ProblemsMenu = "_Problems…";

    /// <summary>View > Compare with another version.</summary>
    public const string CompareMenu = "_Compare with another version…";

    // ---- Track headers (WT50) ----

    /// <summary>A track name box's automation name.</summary>
    /// <param name="label">The lane, e.g. V1.</param>
    /// <returns>The name.</returns>
    public static string TrackNameName(string label) => $"Name of {label}";

    /// <summary>Lock's name.</summary>
    public const string TrackLock = "Lock";

    /// <summary>Lock's glyph.</summary>
    public const string TrackLockGlyph = "\uE72E";

    /// <summary>Mute's name, for audio.</summary>
    public const string TrackMute = "Mute";

    /// <summary>Mute's glyph.</summary>
    public const string TrackMuteGlyph = "\uE74F";

    /// <summary>Disable's name, for video.</summary>
    public const string TrackDisable = "Disable";

    /// <summary>Disable's glyph.</summary>
    public const string TrackDisableGlyph = "\uE7BA";

    /// <summary>Solo's name.</summary>
    public const string TrackSolo = "Solo";

    /// <summary>Solo's glyph.</summary>
    public const string TrackSoloGlyph = "\uE720";

    /// <summary>Hide's name: out of the viewer.</summary>
    public const string TrackHide = "Hide in viewer";

    /// <summary>Hide's glyph.</summary>
    public const string TrackHideGlyph = "\uED1A";

    /// <summary>Add's name.</summary>
    /// <param name="audio">Whether the track is audio.</param>
    /// <returns>The name.</returns>
    public static string TrackAdd(bool audio) => audio ? "Add an audio track below" : "Add a video track above";

    /// <summary>Add's glyph.</summary>
    public const string TrackAddGlyph = "\uE710";

    /// <summary>Delete's name.</summary>
    public const string TrackDelete = "Delete track";

    /// <summary>Delete's glyph.</summary>
    public const string TrackDeleteGlyph = "\uE74D";

    /// <summary>What deleting a track with items asks.</summary>
    /// <param name="name">The track's name.</param>
    /// <param name="count">How many items it holds.</param>
    /// <returns>The question.</returns>
    public static string TrackDeleteQuestion(string name, int count) => $"Delete {name} and the {count} {(count == 1 ? "item" : "items")} on it? Undo brings them back.";

    /// <summary>WT49: View > Ruler.</summary>
    public const string RulerMenu = "_Ruler";

    /// <summary>WT49: the ruler in timecode.</summary>
    public const string RulerTimecodeMenu = "_Timecode";

    /// <summary>WT49: the ruler in frames.</summary>
    public const string RulerFramesMenu = "_Frames";

    /// <summary>WT49: the ruler in feet and frames.</summary>
    public const string RulerFeetMenu = "F_eet+frames";

    /// <summary>WT49: the ruler in seconds.</summary>
    public const string RulerSecondsMenu = "_Seconds";

    /// <summary>WT49: a ruler unit's name as the surface's status says it.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>e.g. "feet+frames".</returns>
    public static string RulerUnitName(WinTimeline.Engine.Timeline.RulerUnit unit) => unit switch
    {
        WinTimeline.Engine.Timeline.RulerUnit.Frames => "frames",
        WinTimeline.Engine.Timeline.RulerUnit.FeetFrames => "feet+frames",
        WinTimeline.Engine.Timeline.RulerUnit.Seconds => "seconds",
        _ => "timecode",
    };

    /// <summary>WT49: what the timeline surface's status adds about the ruler.</summary>
    /// <param name="unit">The unit's name.</param>
    /// <returns>e.g. "ruler frames".</returns>
    public static string RulerStatus(string unit) => $"ruler {unit}";

    /// <summary>WT48: View > Zoom in.</summary>
    public const string ZoomInMenu = "Zoom _in";

    /// <summary>WT48: View > Zoom out.</summary>
    public const string ZoomOutMenu = "Zoom _out";

    /// <summary>WT48: View > Zoom to fit.</summary>
    public const string ZoomFitMenu = "Zoom to _fit";

    /// <summary>The Help menu.</summary>
    public const string HelpMenu = "_Help";

    /// <summary>Help > Create diagnostics bundle.</summary>
    public const string DiagnosticsMenu = "Create _diagnostics bundle…";

    /// <summary>Open's shortcut.</summary>
    public const string OpenGesture = "Ctrl+O";

    /// <summary>Save's shortcut.</summary>
    public const string SaveGesture = "Ctrl+S";

    /// <summary>Save As's shortcut.</summary>
    public const string SaveAsGesture = "Ctrl+Shift+S";

    /// <summary>Exit's shortcut.</summary>
    public const string ExitGesture = "Alt+F4";

    /// <summary>Undo's shortcut.</summary>
    public const string UndoGesture = "Ctrl+Z";

    /// <summary>Redo's shortcut.</summary>
    public const string RedoGesture = "Ctrl+Y";

    /// <summary>The command palette's shortcut.</summary>
    public const string PaletteGesture = "Ctrl+Shift+P";

    /// <summary>WT48: Zoom in's key.</summary>
    public const string ZoomInGesture = "+";

    /// <summary>WT48: Zoom out's key.</summary>
    public const string ZoomOutGesture = "-";

    /// <summary>WT48: Zoom to fit's key.</summary>
    public const string ZoomFitGesture = "Shift+Z";

    /// <summary>A menu header as the palette and the status bar show it, without its access-key underscore.</summary>
    /// <param name="header">e.g. "Save _As…".</param>
    /// <returns>e.g. "Save As…".</returns>
    [return: NotNullIfNotNull(nameof(header))]
    public static string? Plain(string? header) => header?.Replace("_", string.Empty, StringComparison.Ordinal);

    // ---- Main window: bars and status ----

    /// <summary>The crash bar's button that saves the diagnostics bundle.</summary>
    public const string SaveDiagnostics = "Save diagnostics bundle…";

    /// <summary>A button that closes a bar or a notice.</summary>
    public const string Dismiss = "Dismiss";

    /// <summary>The recovery bar's button that takes the edits back.</summary>
    public const string RecoverEdits = "Recover edits";

    /// <summary>The recovery bar's button that throws the edits away.</summary>
    public const string Discard = "Discard";

    /// <summary>What the status bar says before anything happens.</summary>
    public const string Ready = "Ready";

    /// <summary>What the crash bar says about a crashed run.</summary>
    /// <param name="when">When that run started, formatted for the person.</param>
    /// <param name="dumpFile">The crash dump's file name, or null when none was kept.</param>
    /// <returns>The sentence.</returns>
    public static string CrashedLastTime(string when, string? dumpFile) => dumpFile is { } dump
        ? $"WinTimeline closed unexpectedly last time (started {when}). A crash dump was kept ({dump}); the diagnostics bundle includes it."
        : $"WinTimeline closed unexpectedly last time (started {when}). The diagnostics bundle holds its log.";

    /// <summary>What the status bar says about the GPU, e.g. "GPU: NVIDIA GeForce RTX 4060, 8 GB".</summary>
    /// <param name="label">The adapter's label.</param>
    /// <returns>The text.</returns>
    public static string Gpu(string label) => "GPU: " + label;

    /// <summary>The GPU status's tooltip.</summary>
    /// <param name="vendor">The adapter's vendor.</param>
    /// <param name="driver">Its driver version.</param>
    /// <param name="featureLevel">The device's feature level.</param>
    /// <param name="videoDecode">Whether it decodes video.</param>
    /// <returns>The text.</returns>
    public static string GpuDetail(string vendor, string driver, string featureLevel, bool videoDecode) =>
        $"{vendor}, driver {driver}, feature level {featureLevel}" + (videoDecode ? ", video decode" : ", no video decode");

    /// <summary>The viewer surface's automation name.</summary>
    public const string ViewerName = "Viewer";

    /// <summary>WT51: the View menu's playhead follow submenu.</summary>
    public const string FollowMenu = "_Follow playhead";

    /// <summary>WT51: follow by jumping a page when the playhead leaves the view.</summary>
    public const string FollowPageMenu = "_Page";

    /// <summary>WT51: follow keeping the playhead centred.</summary>
    public const string FollowSmoothMenu = "_Smooth";

    /// <summary>WT51: never follow.</summary>
    public const string FollowOffMenu = "_Off";

    /// <summary>WT51: what the viewer's status says of the frame it is on.</summary>
    /// <param name="timecode">The frame's timecode.</param>
    /// <param name="frame">The frame, from the timeline's start.</param>
    /// <returns>The status.</returns>
    public static string ViewerFrame(string timecode, long frame) => string.Create(CultureInfo.InvariantCulture, $"frame {frame} at {timecode}");

    /// <summary>The timeline surface's automation name.</summary>
    public const string TimelineName = "Timeline";

    // ---- Documents: open, save, recover ----

    /// <summary>The Open dialog's title.</summary>
    public const string OpenDialogTitle = "Open timeline";

    /// <summary>The Save As dialog's title.</summary>
    public const string SaveDialogTitle = "Save timeline as";

    /// <summary>The Open dialog's file types; an .otiod is opened by picking the content.otio inside it.</summary>
    /// <param name="adapters">Every reading adapter, with the extensions it takes (WT157 adds the bridged ones).</param>
    /// <returns>The filter: every timeline first, then one entry per format, then all files.</returns>
    public static string OpenFilter(IEnumerable<(string Name, IReadOnlyList<string> Suffixes)> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        (string Name, string[] Patterns)[] formats = [.. adapters.Select(a => (a.Name, a.Suffixes.Where(s => s != ".fcpxmld").Select(s => "*" + s).ToArray()))];
        formats[0].Patterns = ["*.otio", "*.otioz"];
        string all = string.Join(";", formats.SelectMany(f => f.Patterns).Distinct(StringComparer.OrdinalIgnoreCase));
        return string.Join("|", [$"Timelines ({all})", all, .. formats.SelectMany(f => new[] { $"{f.Name} ({string.Join(";", f.Patterns)})", string.Join(";", f.Patterns) }), "All files (*.*)", "*.*"]);
    }

    /// <summary>The Save As dialog's file types.</summary>
    public const string SaveFilter = "OpenTimelineIO (*.otio)|*.otio|OpenTimelineIO bundle with media (*.otioz)|*.otioz";

    /// <summary>After opening a collection, e.g. "Opened reels.otio: 3 timelines."</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="timelines">How many timelines it holds.</param>
    /// <returns>The sentence.</returns>
    public static string OpenedTimelines(string fileName, int timelines) =>
        timelines == 0 ? $"Opened {fileName}: no timelines." : $"Opened {fileName}: {Count(timelines, "timeline", "timelines")}.";

    /// <summary>After opening a file that holds something else than a timeline.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="schema">What it holds.</param>
    /// <returns>The sentence.</returns>
    public static string OpenedNotATimeline(string fileName, string schema) => $"Opened {fileName}: a {schema}, not a timeline.";

    /// <summary>After opening a timeline, e.g. "Opened cut.otio: 4 clips on 1 track."</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="clips">How many clips.</param>
    /// <param name="tracks">On how many tracks.</param>
    /// <returns>The sentence.</returns>
    public static string Opened(string fileName, int clips, int tracks) => $"Opened {fileName}: {ClipsOnTracks(clips, tracks)}.";

    /// <summary>Why a file that is not there could not be opened.</summary>
    public const string NotFound = "it was not found";

    /// <summary>The notice title, and the start of the status sentence, when a file could not be opened.</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The title.</returns>
    public static string CouldNotOpen(string name) => $"{name} could not be opened";

    /// <summary>What the status bar says when a file could not be opened.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string CouldNotOpen(string name, string reason) => $"{CouldNotOpen(name)}: {reason}";

    /// <summary>The notice action that opens another file.</summary>
    public const string OpenAnother = "Open another";

    /// <summary>After a save, e.g. "Saved cut.otio."</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The sentence.</returns>
    public static string Saved(string name) => $"Saved {name}.";

    /// <summary>The notice title, and the start of the status sentence, when a file could not be saved.</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The title.</returns>
    public static string CouldNotSave(string name) => $"{name} could not be saved";

    /// <summary>What the status bar says when a file could not be saved.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string CouldNotSave(string name, string reason) => $"{CouldNotSave(name)}: {reason}";

    /// <summary>The notice action that saves elsewhere.</summary>
    public const string SaveAsAction = "Save As";

    /// <summary>What the status bar adds when media is missing, e.g. " Media is missing for 2 clips: File > Relink media."</summary>
    /// <param name="missing">How many clips miss their media; zero adds nothing.</param>
    /// <returns>The sentence, or empty.</returns>
    public static string MediaMissing(int missing) =>
        missing == 0 ? string.Empty : $" Media is missing for {Count(missing, "clip", "clips")}: File > Relink media.";

    /// <summary>File > Relink media with no timeline open.</summary>
    public const string RelinkNeedsTimeline = "Open a timeline to relink its media.";

    /// <summary>View > Problems with no timeline open.</summary>
    public const string ProblemsNeedTimeline = "Open a timeline to check it for problems.";

    /// <summary>What the status bar adds after an open that found a sidecar, e.g. " Restored the project from cut.otio.wtproj."</summary>
    /// <param name="sidecar">The sidecar's name.</param>
    /// <returns>The sentence.</returns>
    public static string RestoredProject(string sidecar) => $" Restored the project from {sidecar}.";

    /// <summary>What the status bar adds when the sidecar could not be read.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string ProjectNotRestored(string reason) => $" Its project state was not restored: {reason}";

    /// <summary>The recovery offer, e.g. "Edits to cut.otio were not saved when WinTimeline last closed: 3 edits."</summary>
    /// <param name="name">The document's file name.</param>
    /// <param name="edits">How many edits were left.</param>
    /// <returns>The sentence.</returns>
    public static string RecoveryOffer(string name, int edits) =>
        $"Edits to {name} were not saved when WinTimeline last closed: {Count(edits, "edit", "edits")}.";

    /// <summary>After a recovery, e.g. "Recovered cut.otio: 3 clips on 1 track. Save to keep the edits."</summary>
    /// <param name="name">The document's file name.</param>
    /// <param name="clips">How many clips.</param>
    /// <param name="tracks">On how many tracks.</param>
    /// <param name="fromAutosave">Whether it came from the last autosave.</param>
    /// <returns>The sentence.</returns>
    public static string Recovered(string name, int clips, int tracks, bool fromAutosave) =>
        $"Recovered {name}: {ClipsOnTracks(clips, tracks)}."
        + (fromAutosave ? " From the last autosave." : string.Empty) + " Save to keep the edits.";

    /// <summary>The notice title, and the start of the status sentence, when edits could not be recovered.</summary>
    /// <param name="name">The document's file name.</param>
    /// <returns>The title.</returns>
    public static string CouldNotRecover(string name) => $"The edits to {name} could not be recovered";

    /// <summary>What the status bar says when edits could not be recovered.</summary>
    /// <param name="name">The document's file name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string CouldNotRecover(string name, string reason) => $"{CouldNotRecover(name)}: {reason}";

    /// <summary><c>--run-commands</c> with no timeline open.</summary>
    public const string RunCommandsNeedsTimeline = "--run-commands needs a timeline open.";

    /// <summary>After <c>--run-commands</c>, e.g. "Ran 3 commands from edits.jsonl."</summary>
    /// <param name="ran">How many ran.</param>
    /// <param name="fileName">The commands file's name.</param>
    /// <returns>The sentence.</returns>
    public static string RanCommands(int ran, string fileName) => $"Ran {Count(ran, "command", "commands")} from {fileName}.";

    /// <summary>The notice title, and the start of the status sentence, when a command file stopped.</summary>
    /// <param name="fileName">The commands file's name.</param>
    /// <param name="command">The command it stopped at, from 1.</param>
    /// <returns>The title.</returns>
    public static string CommandsStopped(string fileName, int command) => $"{fileName} stopped at command {command}";

    /// <summary>What the status bar says when a command file stopped.</summary>
    /// <param name="fileName">The commands file's name.</param>
    /// <param name="command">The command it stopped at, from 1.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string CommandsStopped(string fileName, int command, string reason) => $"{CommandsStopped(fileName, command)}: {reason}";

    // ---- Diagnostics ----

    /// <summary>The diagnostics Save dialog's title.</summary>
    public const string DiagnosticsDialogTitle = "Create diagnostics bundle";

    /// <summary>The diagnostics Save dialog's file types.</summary>
    public const string ZipFilter = "Zip archive (*.zip)|*.zip";

    /// <summary>After the bundle was saved, e.g. "Diagnostics saved to diag.zip."</summary>
    /// <param name="fileName">The bundle's name.</param>
    /// <returns>The sentence.</returns>
    public static string DiagnosticsSavedTo(string fileName) => $"Diagnostics saved to {fileName}.";

    /// <summary>The notice title after the bundle was saved.</summary>
    public const string DiagnosticsSaved = "Diagnostics saved";

    /// <summary>The notice action that shows a file in Explorer.</summary>
    public const string ShowFile = "Show file";

    /// <summary>The notice title when the bundle could not be saved.</summary>
    public const string DiagnosticsNotSaved = "Diagnostics could not be saved";

    /// <summary>What the status bar says when the bundle could not be saved.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The sentence.</returns>
    public static string DiagnosticsNotSavedBecause(string reason) => $"{DiagnosticsNotSaved}: {reason}";

    /// <summary>The notice action that tries again.</summary>
    public const string TryAgain = "Try again";

    // ---- Welcome page ----

    /// <summary>The welcome page's line under the heading.</summary>
    public const string WelcomeLine = "Open a timeline, or drop media to start one.";

    /// <summary>The drop zone's automation name.</summary>
    public const string DropZoneName = "Drop an .otio, an EDL or video and audio files here";

    /// <summary>What the drop zone says.</summary>
    public const string DropZoneText = "Drop an .otio, an EDL, or video and audio files here";

    /// <summary>The welcome page's Open button.</summary>
    public const string OpenButton = "Open…";

    /// <summary>The welcome page's New button.</summary>
    public const string NewEmptyTimeline = "New empty timeline";

    /// <summary>The heading over the recent documents.</summary>
    public const string Recent = "Recent";

    /// <summary>What shows when no document was opened yet.</summary>
    public const string NothingOpened = "Nothing opened yet.";

    /// <summary>A recent document's button: its name, its folder, and whether it is gone.</summary>
    /// <param name="fileName">The document's name.</param>
    /// <param name="folder">Its folder.</param>
    /// <param name="found">Whether it is still there.</param>
    /// <returns>The text.</returns>
    public static string RecentEntry(string fileName, string? folder, bool found) =>
        $"{fileName}    {folder}{(found ? string.Empty : "  (not found)")}";

    /// <summary>After New empty timeline.</summary>
    public const string CreatedEmpty = "Created an empty timeline: 0 clips on 2 tracks.";

    /// <summary>After media was dropped, e.g. "Created a timeline from 2 files: 2 clips on 2 tracks."</summary>
    /// <param name="files">How many files became clips.</param>
    /// <param name="clips">How many clips the timeline has.</param>
    /// <param name="tracks">On how many tracks.</param>
    /// <param name="refused">The files that were not media, with why.</param>
    /// <returns>The sentence.</returns>
    public static string Created(int files, int clips, int tracks, IReadOnlyList<string> refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        string made = $"Created a timeline from {Count(files, "file", "files")}: {Count(clips, "clip", "clips")} on {tracks} tracks.";
        return refused.Count == 0 ? made : $"{made} Not media: {string.Join("; ", refused)}.";
    }

    /// <summary>The notice title when nothing dropped was media.</summary>
    public const string NothingDroppedUsable = "Nothing dropped could be used";

    /// <summary>What the status bar says when nothing dropped was media.</summary>
    /// <param name="refused">Why each file was refused, joined.</param>
    /// <returns>The sentence.</returns>
    public static string NothingDroppedUsableBecause(string refused) => $"{NothingDroppedUsable}: {refused}.";

    // ---- Timelines and versions ----

    /// <summary>When a tab is chosen, e.g. "Showing reel2: 3 clips on 1 track."</summary>
    /// <param name="name">The timeline's name.</param>
    /// <param name="clips">How many clips.</param>
    /// <param name="tracks">On how many tracks.</param>
    /// <returns>The sentence.</returns>
    public static string Showing(string name, int clips, int tracks) => $"Showing {name}: {ClipsOnTracks(clips, tracks)}.";

    /// <summary>View > Compare with no timeline open.</summary>
    public const string CompareNeedsTimeline = "Open a timeline to compare it.";

    /// <summary>View > Compare on a file of one timeline.</summary>
    public const string CompareNeedsSecond = "Compare needs a second timeline: open a file of several, or use Edit > Duplicate timeline as version.";

    /// <summary>Duplicate timeline on a file that is not a collection.</summary>
    /// <param name="name">The timeline's name.</param>
    /// <returns>The sentence.</returns>
    public static string OnlyTimeline(string name) => $"{name} is the file's only timeline; a version can be added to a file that holds a collection.";

    /// <summary>After a version was added, before what it shows.</summary>
    /// <param name="name">The new version's name.</param>
    /// <returns>The sentence, ending in a space.</returns>
    public static string Added(string name) => $"Added {name}. ";

    // ---- Compare window ----

    /// <summary>The Compare window's title.</summary>
    public const string CompareTitle = "Compare";

    /// <summary>The label before the earlier version.</summary>
    public const string ChangesFrom = "Changes from";

    /// <summary>The earlier version's automation name.</summary>
    public const string CompareFrom = "Compare from";

    /// <summary>The later version, after the choice, e.g. "to reel2".</summary>
    /// <param name="name">The shown timeline's name.</param>
    /// <returns>The text.</returns>
    public static string CompareTo(string name) => $"to {name}";

    /// <summary>A button that closes a window.</summary>
    public const string Close = "Close";

    // ---- Problems window ----

    /// <summary>The Problems window's title.</summary>
    public const string ProblemsTitle = "Problems";

    /// <summary>The button that fixes the selected problem.</summary>
    public const string Fix = "Fix";

    /// <summary>The button that fixes every fixable problem.</summary>
    public const string FixAll = "Fix all";

    /// <summary>The history step Fix all makes.</summary>
    public const string FixProblemsStep = "Fix problems";

    /// <summary>How a problem is listed, e.g. "Error at 0/1: Transition "x" reaches further than a and b last. (fixable)".</summary>
    /// <param name="severity">How serious it is.</param>
    /// <param name="path">Where it is.</param>
    /// <param name="message">What it is.</param>
    /// <param name="fixable">Whether a fix is known.</param>
    /// <returns>The line.</returns>
    public static string ProblemLine(string severity, string path, string message, bool fixable) =>
        $"{severity} at {path}: {message}{(fixable ? " (fixable)" : string.Empty)}";

    /// <summary>What the status bar adds after an open, e.g. " 2 problems: View > Problems."</summary>
    /// <param name="count">How many problems; zero adds nothing.</param>
    /// <returns>The sentence, or empty.</returns>
    public static string ProblemCount(int count) =>
        count == 0 ? string.Empty : $" {Count(count, "problem", "problems")}: View > Problems.";

    /// <summary>What the panel says after fixing, e.g. "Fixed 1 problem. Nothing else is wrong."</summary>
    /// <param name="fixedCount">How many were fixed.</param>
    /// <param name="left">How many are left.</param>
    /// <returns>The sentence.</returns>
    public static string Fixed(int fixedCount, int left) =>
        $"Fixed {Count(fixedCount, "problem", "problems")}. "
        + (left == 0 ? "Nothing else is wrong." : $"{left} {(left == 1 ? "needs" : "need")} a person.");

    // ---- Relink window ----

    /// <summary>The Relink window's title.</summary>
    public const string RelinkTitle = "Relink media";

    /// <summary>What the Relink window explains.</summary>
    public const string RelinkHelp = "Pick a clip and locate its file. Every other missing clip whose file sits the same way under the new folder, or beside the file you pick, is relinked with it.";

    /// <summary>The button that locates the selected clip's file.</summary>
    public const string Locate = "Locate…";

    /// <summary>The Locate dialog's title.</summary>
    /// <param name="clip">The clip's name.</param>
    /// <returns>The title.</returns>
    public static string LocateTitle(string clip) => $"Locate {clip}";

    /// <summary>Where a missing clip's media was, when nothing says.</summary>
    public const string NoFile = "no file";

    /// <summary>How a missing clip is listed.</summary>
    /// <param name="clip">The clip's name.</param>
    /// <param name="where">Where its media was.</param>
    /// <returns>The line.</returns>
    public static string MissingLine(string clip, string where) => $"{clip} — {where}";

    /// <summary>The history step a relink makes.</summary>
    /// <param name="clips">How many clips it relinks.</param>
    /// <returns>The step's name.</returns>
    public static string RelinkStep(int clips) => $"Relink {clips} clips";

    /// <summary>After a relink, e.g. "Relinked 3 clips. 1 is still missing."</summary>
    /// <param name="relinked">How many were relinked.</param>
    /// <param name="stillMissing">How many are still missing.</param>
    /// <returns>The sentence.</returns>
    public static string Relinked(int relinked, int stillMissing) =>
        $"Relinked {Count(relinked, "clip", "clips")}. "
        + (stillMissing == 0 ? "Nothing is missing." : $"{stillMissing} {(stillMissing == 1 ? "is" : "are")} still missing.");

    // ---- Command palette ----

    /// <summary>The palette window's title.</summary>
    public const string PaletteTitle = "Command palette";

    /// <summary>The palette query's automation name.</summary>
    public const string PaletteQueryName = "Search commands, @clips, #markers, or a timecode";

    /// <summary>The palette's button that runs the selected result.</summary>
    public const string Run = "Run";

    /// <summary>The palette's hint at its prefixes.</summary>
    public const string PaletteHint = "> commands   @ clips   # markers   ? ask";

    /// <summary>How a result reads in the list, e.g. "Open… — File, Ctrl+O".</summary>
    /// <param name="title">The result's line.</param>
    /// <param name="detail">Its second column, or empty.</param>
    /// <returns>The text.</returns>
    public static string PaletteLine(string title, string detail) => detail.Length == 0 ? title : $"{title} — {detail}";

    /// <summary>A command's detail: its menu, and its shortcut when it has one.</summary>
    /// <param name="menu">The menu.</param>
    /// <param name="gesture">The shortcut, or null.</param>
    /// <returns>The detail.</returns>
    public static string CommandDetail(string menu, string? gesture) => gesture is { } g ? $"{menu}, {g}" : menu;

    /// <summary>A clip's or marker's detail: its lane and timecode.</summary>
    /// <param name="lane">The lane's label.</param>
    /// <param name="timecode">Where it is.</param>
    /// <returns>The detail.</returns>
    public static string LaneAt(string lane, string timecode) => $"{lane}, {timecode}";

    /// <summary>A question for the assistant, as a result.</summary>
    /// <param name="query">The question.</param>
    /// <returns>The line.</returns>
    public static string Ask(string query) => $"Ask: {query}";

    /// <summary>The detail of a question for the assistant.</summary>
    public const string AssistantArrives = "the assistant arrives with Block L";

    /// <summary>What the status bar says when a question is chosen.</summary>
    public const string AssistantNotHere = "The assistant is not here yet; it arrives with Block L.";

    /// <summary>A timecode jump, as a result.</summary>
    /// <param name="query">The typed timecode.</param>
    /// <returns>The line.</returns>
    public static string GoTo(string query) => $"Go to {query}";

    /// <summary>The detail of a timecode jump.</summary>
    public const string TimecodeDetail = "timecode";

    /// <summary>After a jump, e.g. "Playhead at 01:00:04:00: Go to 01:00:04:00."</summary>
    /// <param name="timecode">Where the playhead is.</param>
    /// <param name="title">What was chosen.</param>
    /// <returns>The sentence.</returns>
    public static string PlayheadAt(string timecode, string title) => $"Playhead at {timecode}: {title}.";

    /// <summary>A command that cannot run now, e.g. "Save cannot be done now."</summary>
    /// <param name="command">The command, as its menu reads without the access key.</param>
    /// <returns>The sentence.</returns>
    public static string CannotBeDoneNow(string? command) => $"{command} cannot be done now.";

    /// <summary>The palette command's text.</summary>
    public const string PaletteCommandText = "Command palette";

    // ---- Notices ----

    /// <summary>The glyph of the button that dismisses a notice.</summary>
    public const string DismissGlyph = "";

    /// <summary>The glyph of an error notice.</summary>
    public const string ErrorGlyph = "";

    /// <summary>The glyph of a warning notice.</summary>
    public const string WarningGlyph = "";

    /// <summary>The glyph of a success notice.</summary>
    public const string SuccessGlyph = "";

    /// <summary>The glyph of an informational notice.</summary>
    public const string InfoGlyph = "";

    /// <summary>A failed job's notice title, e.g. "Proxy for A001.mov failed".</summary>
    /// <param name="job">The job's title.</param>
    /// <returns>The title.</returns>
    public static string JobFailed(string job) => $"{job} failed";

    /// <summary>A failed job's message when it gave no error.</summary>
    public const string JobStopped = "It stopped with an error.";

    /// <summary>The notice action that queues a job again.</summary>
    public const string Retry = "Retry";

    /// <summary>A finished job's notice title.</summary>
    /// <param name="job">The job's title.</param>
    /// <returns>The title.</returns>
    public static string JobFinished(string job) => $"{job} finished";

    // ---- Timeline panel ----

    /// <summary>An item's place on the timeline as UI Automation reads it, e.g. "V1, 00:00:00:00 to 00:00:03:00".</summary>
    /// <param name="lane">The lane's label.</param>
    /// <param name="start">Its start timecode.</param>
    /// <param name="end">Its end timecode.</param>
    /// <returns>The text.</returns>
    public static string ItemSpan(string lane, string start, string end) => $"{lane}, {start} to {end}";

    /// <summary>A time in seconds where no timecode applies, e.g. "12.500 s".</summary>
    /// <param name="seconds">The time.</param>
    /// <returns>The text.</returns>
    public static string Seconds(double seconds) => string.Create(CultureInfo.InvariantCulture, $"{seconds:F3} s");

    /// <summary>How long the timeline surface takes to draw, as its status reads.</summary>
    /// <param name="median">The median frame, in milliseconds.</param>
    /// <param name="p95">The 95th percentile frame, in milliseconds.</param>
    /// <param name="frames">Over how many frames.</param>
    /// <returns>The text.</returns>
    public static string DrawTimes(double median, double p95, int frames) =>
        string.Create(CultureInfo.InvariantCulture, $"draw {median:F1} ms median, {p95:F1} ms p95 over {frames} frames");

    /// <summary>WT48: what the timeline surface shows, as its status adds it, e.g. "view 01:00:00:00 to 01:02:30:00".</summary>
    /// <param name="from">The timecode at the left edge.</param>
    /// <param name="to">The timecode at the right edge.</param>
    /// <returns>The phrase.</returns>
    public static string ViewSpan(string from, string to) => $"view {from} to {to}";

    // ---- Shared ----

    private static string ClipsOnTracks(int clips, int tracks) => $"{Count(clips, "clip", "clips")} on {Count(tracks, "track", "tracks")}";

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
