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

    /// <summary>WT160: copies the selected clips.</summary>
    public const string CopyClipsMenu = "_Copy clips";

    /// <summary>WT160: the gesture shown beside Copy clips.</summary>
    public const string CopyClipsGesture = "Ctrl+C";

    /// <summary>WT160: pastes clips at the playhead.</summary>
    public const string PasteClipsMenu = "_Paste clips";

    /// <summary>WT160: the gesture shown beside Paste clips.</summary>
    public const string PasteClipsGesture = "Ctrl+V";

    /// <summary>WT160: what the history calls a paste.</summary>
    public const string PasteStep = "Paste clips";

    /// <summary>WT160: Copy with nothing selected.</summary>
    public const string NothingToCopy = "Select clips on the timeline to copy them.";

    /// <summary>WT160: Paste with nothing copied.</summary>
    public const string NothingToPaste = "Nothing has been copied to paste.";

    /// <summary>WT160: what a copy did.</summary>
    /// <param name="items">How many items.</param>
    /// <param name="placed">Whether the clipboard took them; when not, only this window can paste them.</param>
    /// <returns>The status.</returns>
    public static string Copied(int items, bool placed) =>
        string.Create(CultureInfo.InvariantCulture, $"Copied {items} {(items == 1 ? "item" : "items")}.{(placed ? string.Empty : " The clipboard was busy, so only this window can paste them.")}");

    /// <summary>WT160: what a paste did.</summary>
    /// <param name="items">How many items.</param>
    /// <param name="lanes">The lanes, e.g. "V1, A1".</param>
    /// <param name="timecode">The playhead's timecode.</param>
    /// <param name="insert">WT78: whether it inserted rather than overwrote.</param>
    /// <returns>The status.</returns>
    public static string Pasted(int items, string lanes, string timecode, bool insert) =>
        string.Create(CultureInfo.InvariantCulture, $"{(insert ? "Inserted" : "Pasted")} {items} {(items == 1 ? "item" : "items")} {(insert ? "into" : "over")} {lanes} at {timecode}.");

    /// <summary>WT78: pastes clips pushing what follows later.</summary>
    public const string PasteInsertMenu = "Paste _insert";

    /// <summary>WT78: the gesture shown beside Paste insert.</summary>
    public const string PasteInsertGesture = "Ctrl+Shift+V";

    /// <summary>WT78: pastes a copied clip's chosen attributes onto the selection.</summary>
    public const string PasteAttributesMenu = "Paste a_ttributes...";

    /// <summary>WT78: the gesture shown beside Paste attributes.</summary>
    public const string PasteAttributesGesture = "Ctrl+Alt+V";

    /// <summary>WT78: Paste attributes with nothing selected.</summary>
    public const string PasteAttributesNeedsSelection = "Select the clips to paste attributes onto.";

    /// <summary>WT78: the attributes dialog's title.</summary>
    public const string PasteAttributesTitle = "Paste attributes";

    /// <summary>WT78: the dialog's effects choice.</summary>
    public const string AttributeEffects = "Effects and speed";

    /// <summary>WT78: the dialog's markers choice.</summary>
    public const string AttributeMarkers = "Markers";

    /// <summary>WT78: the dialog's colour choice.</summary>
    public const string AttributeColor = "Colour";

    /// <summary>WT78: the dialog's metadata choice.</summary>
    public const string AttributeMetadata = "Metadata (text, numbers and switches)";

    /// <summary>WT78: the dialog's paste button.</summary>
    public const string AttributePaste = "Paste";

    /// <summary>WT78: what the dialog asks.</summary>
    /// <param name="from">The clip copied.</param>
    /// <returns>The question.</returns>
    public static string PasteAttributesQuestion(string from) => $"Which of {from}'s attributes go onto the selected clips?";

    /// <summary>WT78: what a paste of attributes did.</summary>
    /// <param name="from">The clip copied.</param>
    /// <param name="changed">How many clips changed.</param>
    /// <returns>The status.</returns>
    public static string AttributesPasted(string from, int changed) =>
        string.Create(CultureInfo.InvariantCulture, $"Pasted {from}'s attributes onto {changed} {(changed == 1 ? "clip" : "clips")}.");

    /// <summary>WT52: selects every item on the timeline.</summary>
    public const string SelectAllMenu = "Select _all";

    /// <summary>WT52: the gesture shown beside Select all.</summary>
    public const string SelectAllGesture = "Ctrl+A";

    /// <summary>WT52: selects nothing.</summary>
    public const string DeselectAllMenu = "Dese_lect all";

    /// <summary>WT52: the gesture shown beside Deselect all.</summary>
    public const string DeselectAllGesture = "Ctrl+Shift+A";

    /// <summary>WT52: selects every item from the playhead on, on every track.</summary>
    public const string TrackForwardMenu = "Select track _forward";

    /// <summary>WT52: the gesture shown beside Select track forward.</summary>
    public const string TrackForwardGesture = "A";

    /// <summary>WT52: selects every item up to the playhead, on every track.</summary>
    public const string TrackBackwardMenu = "Select track _backward";

    /// <summary>WT52: the gesture shown beside Select track backward.</summary>
    public const string TrackBackwardGesture = "Shift+A";

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

    /// <summary>WT169: the colour strip's name, e.g. "Colour of V1".</summary>
    /// <param name="track">The track's label.</param>
    /// <returns>The name.</returns>
    public static string TrackColour(string track) => $"Colour of {track}";

    /// <summary>WT169: the colour menu's entry that takes a track's colour away.</summary>
    public const string TrackColourNone = "None";

    /// <summary>WT169: a colour name as a menu shows it, e.g. "BLUE" as "Blue".</summary>
    /// <param name="name">An OTIO colour name.</param>
    /// <returns>The label.</returns>
    public static string ColourName(string name) =>
        string.IsNullOrEmpty(name) ? name : name[..1].ToUpperInvariant() + name[1..].ToLowerInvariant();

    /// <summary>WT169: what a track header publishes about its colour, e.g. "colour Blue" or "no colour".</summary>
    /// <param name="name">The colour's name, or null.</param>
    /// <returns>The status.</returns>
    public static string TrackColourStatus(string? name) => name is null ? "no colour" : $"colour {ColourName(name)}";

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

    /// <summary>WT81: the Edit menu's multicam submenu.</summary>
    public const string MulticamMenu = "Create _multicam";

    /// <summary>WT163: the suffix a changed clip's status carries while a comparison is shown.</summary>
    /// <param name="kind">The change's kind, e.g. Trimmed.</param>
    /// <returns>e.g. ", trimmed".</returns>
    public static string ChangeShown(string kind) => ", " + kind.ToLowerInvariant();

    /// <summary>WT163: a removed clip's ghost as UI Automation reads it.</summary>
    /// <param name="track">Its track, e.g. V1.</param>
    /// <param name="timecode">Where it was.</param>
    /// <returns>e.g. "removed from V1 at 01:00:04:00".</returns>
    public static string GhostStatus(string track, string timecode) => $"removed from {track} at {timecode}";

    /// <summary>WT162: writes the comparison as a CMX 3600 change list.</summary>
    public const string ExportChangeList = "Export change list...";

    /// <summary>WT162: marks each change on the shown timeline.</summary>
    public const string MarkChanges = "Mark changes";

    /// <summary>WT162: the change list's save dialog's title.</summary>
    public const string ExportChangeListTitle = "Export change list";

    /// <summary>WT162: the change list's file types.</summary>
    public const string EdlFilter = "CMX 3600 EDL (*.edl)|*.edl";

    /// <summary>WT162: exporting or marking with nothing changed.</summary>
    public const string NoChangesToHandOn = "There are no changes to hand on.";

    /// <summary>WT162: the file a change list is offered as.</summary>
    /// <param name="timeline">The later version's name.</param>
    /// <returns>e.g. "reel2 v002 changes.edl".</returns>
    public static string ChangeListFileName(string timeline) => $"{timeline} changes.edl";

    /// <summary>WT162: what exporting the change list did.</summary>
    /// <param name="events">How many events.</param>
    /// <param name="file">The file's name.</param>
    /// <returns>The summary.</returns>
    public static string ChangeListWritten(int events, string file) =>
        string.Create(CultureInfo.InvariantCulture, $"Wrote {events} {(events == 1 ? "event" : "events")} to {file}.");

    /// <summary>WT162: the change list could not be written.</summary>
    /// <param name="file">The file's name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The summary.</returns>
    public static string ChangeListNotWritten(string file, string reason) => $"{file} could not be written: {reason}";

    /// <summary>WT162: what marking the changes did.</summary>
    /// <param name="markers">How many markers.</param>
    /// <param name="timeline">The timeline marked.</param>
    /// <returns>The summary.</returns>
    public static string ChangesMarked(int markers, string timeline) =>
        string.Create(CultureInfo.InvariantCulture, $"Marked {markers} {(markers == 1 ? "change" : "changes")} on {timeline}; Edit > Undo takes them off.");

    /// <summary>WT121: marks the beats of the selected music clips.</summary>
    public const string DetectBeatsMenu = "_Detect beats";

    /// <summary>WT121: the Edit menu's cut to beats submenu.</summary>
    public const string CutToBeatsMenu = "Cut to _beats";

    /// <summary>WT121: lays clips on every beat.</summary>
    public const string EveryBeatMenu = "_Every beat";

    /// <summary>WT121: on every second beat.</summary>
    public const string EverySecondBeatMenu = "Every _second beat";

    /// <summary>WT121: on every fourth beat.</summary>
    public const string EveryFourthBeatMenu = "Every _fourth beat";

    /// <summary>WT121: Detect beats with no clip of media selected.</summary>
    public const string BeatsNeedMusic = "Select a music clip whose media is on this machine to detect its beats.";

    /// <summary>WT121: what Detect beats says while it listens.</summary>
    public const string DetectingBeats = "Detecting beats...";

    /// <summary>WT121: Cut to beats without clips on one track selected.</summary>
    public const string CutToBeatsNeedsClips = "Select the clips to lay on the beats, all on one track.";

    /// <summary>WT121: what Detect beats did.</summary>
    /// <param name="count">How many beats it marked.</param>
    /// <param name="clip">The first clip's name.</param>
    /// <param name="bpm">Its tempo.</param>
    /// <returns>The status.</returns>
    public static string BeatsMarked(int count, string clip, double bpm) =>
        string.Create(CultureInfo.InvariantCulture, $"Marked {count} beats on {clip} at {bpm:0} BPM.");

    /// <summary>WT121: what Cut to beats did.</summary>
    /// <param name="count">How many clips it laid.</param>
    /// <returns>The status.</returns>
    public static string LaidOnBeats(int count) => string.Create(CultureInfo.InvariantCulture, $"Laid {count} {(count == 1 ? "clip" : "clips")} on the beats.");

    /// <summary>WT81: the Edit menu's cut to angle submenu.</summary>
    public const string CutToAngleMenu = "Cut to _angle";

    /// <summary>WT81: cuts the multicam at the playhead to its first angle.</summary>
    public const string Angle1Menu = "Angle _1";

    /// <summary>WT81: to its second angle.</summary>
    public const string Angle2Menu = "Angle _2";

    /// <summary>WT81: to its third angle.</summary>
    public const string Angle3Menu = "Angle _3";

    /// <summary>WT81: to its fourth angle.</summary>
    public const string Angle4Menu = "Angle _4";

    /// <summary>WT81: a cut to an angle with no multicam at the playhead.</summary>
    public const string NoMulticamHere = "No multicam clip plays at the playhead.";

    /// <summary>WT81: a multicam lined up by timecode.</summary>
    public const string MulticamTimecodeMenu = "Synced by _timecode";

    /// <summary>WT81: a multicam lined up by in points.</summary>
    public const string MulticamInPointsMenu = "Synced by _in points";

    /// <summary>WT81: the name a new multicam clip gets.</summary>
    /// <param name="number">How many the timeline will hold with it.</param>
    /// <returns>e.g. "Multicam 1".</returns>
    public static string NewMulticamName(int number) => string.Create(CultureInfo.InvariantCulture, $"Multicam {number}");

    /// <summary>WT81: what making a multicam did.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="angles">How many angles.</param>
    /// <returns>The status.</returns>
    public static string MulticamMade(string name, int angles) =>
        string.Create(CultureInfo.InvariantCulture, $"Made {name} of {angles} angles; 1 to {angles} cut to an angle at the playhead.");

    /// <summary>WT81: what a cut to an angle did.</summary>
    /// <param name="angle">The angle, 1 for the first.</param>
    /// <param name="timecode">Where.</param>
    /// <returns>The status.</returns>
    public static string CutToAngle(int angle, string timecode) => string.Create(CultureInfo.InvariantCulture, $"Cut to angle {angle} at {timecode}.");

    /// <summary>WT81: a cut to the angle already shown.</summary>
    /// <param name="angle">The angle.</param>
    /// <returns>The status.</returns>
    public static string AlreadyAngle(int angle) => string.Create(CultureInfo.InvariantCulture, $"Angle {angle} is already shown here.");

    /// <summary>WT81: the suffix a multicam clip's status carries.</summary>
    /// <param name="angle">The angle shown, 1 for the first.</param>
    /// <returns>e.g. ", angle 2".</returns>
    public static string AngleShown(int angle) => string.Create(CultureInfo.InvariantCulture, $", angle {angle}");

    /// <summary>WT79: links the selected clips.</summary>
    public const string LinkMenu = "Lin_k clips";

    /// <summary>WT79: the gesture shown beside Link clips.</summary>
    public const string LinkGesture = "Ctrl+L";

    /// <summary>WT79: breaks the selected clips' link.</summary>
    public const string UnlinkMenu = "Brea_k link";

    /// <summary>WT79: the gesture shown beside Break link.</summary>
    public const string UnlinkGesture = "Ctrl+Shift+L";

    /// <summary>WT79: groups the selected clips.</summary>
    public const string GroupMenu = "_Group clips";

    /// <summary>WT79: the gesture shown beside Group clips.</summary>
    public const string GroupGesture = "Ctrl+G";

    /// <summary>WT79: takes the selected clips out of their group.</summary>
    public const string UngroupMenu = "Ungro_up";

    /// <summary>WT79: the gesture shown beside Ungroup.</summary>
    public const string UngroupGesture = "Ctrl+Shift+G";

    /// <summary>WT79: whether a clip's link comes with it when clicked.</summary>
    public const string LinkedSelectionMenu = "Linked selectio_n";

    /// <summary>WT79: the gesture shown beside Linked selection.</summary>
    public const string LinkedSelectionGesture = "L";

    /// <summary>WT79: moves the selected clips that slid back into sync with their link.</summary>
    public const string MoveIntoSyncMenu = "Move into s_ync";

    /// <summary>WT79: what linked selection on says.</summary>
    public const string LinkedSelectionOn = "Linked selection is on: a clip brings its link.";

    /// <summary>WT79: what linked selection off says.</summary>
    public const string LinkedSelectionOff = "Linked selection is off: a clip moves alone.";

    /// <summary>WT79: Move into sync with nothing slid selected.</summary>
    public const string NothingOutOfSync = "None of the selected clips is out of sync.";

    /// <summary>WT79: the suffix a slid clip's status carries.</summary>
    /// <param name="frames">How far, in frames.</param>
    /// <returns>e.g. ", out of sync +12f".</returns>
    public static string OutOfSync(long frames) => ", out of sync " + SnapDelta(frames);

    /// <summary>WT79: what linking did.</summary>
    /// <param name="count">How many clips.</param>
    /// <returns>The status.</returns>
    public static string Linked(int count) => string.Create(CultureInfo.InvariantCulture, $"Linked {count} clips.");

    /// <summary>WT79: what breaking a link did.</summary>
    /// <param name="count">How many clips.</param>
    /// <returns>The status.</returns>
    public static string Unlinked(int count) => string.Create(CultureInfo.InvariantCulture, $"Broke the link of {count} {(count == 1 ? "clip" : "clips")}.");

    /// <summary>WT79: what grouping did.</summary>
    /// <param name="count">How many clips.</param>
    /// <returns>The status.</returns>
    public static string Grouped(int count) => string.Create(CultureInfo.InvariantCulture, $"Grouped {count} clips.");

    /// <summary>WT79: what ungrouping did.</summary>
    /// <param name="count">How many clips.</param>
    /// <returns>The status.</returns>
    public static string Ungrouped(int count) => string.Create(CultureInfo.InvariantCulture, $"Ungrouped {count} {(count == 1 ? "clip" : "clips")}.");

    /// <summary>WT79: what Move into sync did.</summary>
    /// <param name="count">How many clips.</param>
    /// <returns>The status.</returns>
    public static string MovedIntoSync(int count) => string.Create(CultureInfo.InvariantCulture, $"Moved {count} {(count == 1 ? "clip" : "clips")} into sync.");

    /// <summary>WT71: splits at the playhead.</summary>
    public const string SplitMenu = "_Split at playhead";

    /// <summary>WT71: the gesture shown beside Split at playhead.</summary>
    public const string SplitGesture = "Ctrl+K";

    /// <summary>WT71: splits every track at the playhead.</summary>
    public const string SplitAllMenu = "Split _every track";

    /// <summary>WT71: the gesture shown beside Split all tracks.</summary>
    public const string SplitAllGesture = "Ctrl+Shift+K";

    /// <summary>WT71: the razor tool.</summary>
    public const string RazorMenu = "_Razor tool";

    /// <summary>WT71: the gesture shown beside Razor tool.</summary>
    public const string RazorGesture = "B";

    /// <summary>WT71: what choosing the razor says.</summary>
    public const string RazorOn = "Razor: press a clip to split it there. V goes back to selecting.";

    /// <summary>WT71: what going back to selecting says.</summary>
    public const string RazorOff = "Selecting.";

    /// <summary>WT71: what a split did.</summary>
    /// <param name="cut">How many items it cut.</param>
    /// <param name="timecode">Where.</param>
    /// <returns>The status.</returns>
    public static string Split(int cut, string timecode) => cut == 0
        ? $"Nothing to split at {timecode}."
        : string.Create(CultureInfo.InvariantCulture, $"Split {cut} {(cut == 1 ? "clip" : "clips")} at {timecode}.");

    /// <summary>WT70: a swap needs one clip dropped on another.</summary>
    public const string SwapNeedsOne = "Drop one clip on another to swap them.";

    /// <summary>WT70: a clip dropped on a track of the other kind.</summary>
    /// <param name="name">The clip.</param>
    /// <returns>The refusal.</returns>
    public static string MoveWrongTrack(string name) => $"{name} can only move to a track of its own kind.";

    /// <summary>WT70: what the history calls a drag of clips.</summary>
    /// <param name="mode">Overwrite, insert or swap.</param>
    /// <param name="duplicate">Whether it dropped copies.</param>
    /// <returns>The step's name.</returns>
    public static string MoveStep(MoveMode mode, bool duplicate) => duplicate ? "Duplicate clips" : mode switch
    {
        MoveMode.Insert => "Insert clips",
        MoveMode.Swap => "Swap clips",
        _ => "Move clips",
    };

    /// <summary>WT70: what a drag of clips did.</summary>
    /// <param name="count">How many clips.</param>
    /// <param name="mode">Overwrite, insert or swap.</param>
    /// <param name="duplicate">Whether it dropped copies.</param>
    /// <returns>The status.</returns>
    public static string Moved(int count, MoveMode mode, bool duplicate)
    {
        string clips = count == 1 ? "1 clip" : string.Create(CultureInfo.InvariantCulture, $"{count} clips");
        return duplicate ? $"Copied {clips} by overwrite." : mode switch
        {
            MoveMode.Insert => $"Moved {clips} by insert.",
            MoveMode.Swap => $"Swapped {clips}.",
            _ => $"Moved {clips} by overwrite.",
        };
    }

    /// <summary>WT58: the item the keyboard's focus is on, as the surface's status says it.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="lane">Its lane, e.g. "V1".</param>
    /// <returns>e.g. "focus c2 on V1".</returns>
    public static string FocusStatus(string name, string lane) => $"focus {name} on {lane}";

    /// <summary>WT58: what a screen reader reads after an item's name: what it is, where it plays and on which track.</summary>
    /// <param name="kind">e.g. "clip".</param>
    /// <param name="start">Where it starts, as timecode.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="audio">Whether its track carries sound.</param>
    /// <param name="number">The track's number, e.g. "2".</param>
    /// <returns>e.g. "clip, 00:01:12:04 to 00:01:20:00, video track 2".</returns>
    public static string ItemReading(string kind, string start, string end, bool audio, string number) =>
        $"{kind}, {start} to {end}, {(audio ? "audio" : "video")} track {number}";

    /// <summary>WT57: the View menu's heat lanes submenu.</summary>
    public const string HeatMenu = "_Heat lanes";

    /// <summary>WT57: the cut density lane.</summary>
    public const string HeatCutsMenu = "_Cuts";

    /// <summary>WT57: the problems lane.</summary>
    public const string HeatProblemsMenu = "_Problems";

    /// <summary>WT57: the markers lane.</summary>
    public const string HeatMarkersMenu = "_Markers";

    /// <summary>WT57: a heat lane's name as the surface's status says it.</summary>
    /// <param name="kind">The lane.</param>
    /// <returns>e.g. "markers".</returns>
    public static string HeatName(Engine.Timeline.HeatKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>WT57: the heat lanes shown, e.g. "heat cuts 7, markers 3".</summary>
    /// <param name="lanes">Each lane's name and how many things it counts.</param>
    /// <returns>The status.</returns>
    public static string HeatStatus(IEnumerable<(string Name, int Count)> lanes) =>
        "heat " + string.Join(", ", lanes.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Name} {l.Count}")));

    /// <summary>WT56: the breadcrumb's button back up to what holds the compound shown.</summary>
    public const string BreadcrumbUp = "Back up (Esc)";

    /// <summary>WT56: the breadcrumb's back arrow, in Segoe Fluent Icons.</summary>
    public const string BreadcrumbUpGlyph = "";

    /// <summary>WT56: the path to the compound shown, e.g. "cut v1 > Compound 1".</summary>
    /// <param name="names">The timeline's name, then each compound's, outermost first.</param>
    /// <returns>The path.</returns>
    public static string BreadcrumbPath(IEnumerable<string> names) => string.Join(" > ", names);

    /// <summary>WT56: what opening a compound says.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The status.</returns>
    public static string CompoundOpened(string name) => $"Showing {name}; Esc goes back up.";

    /// <summary>WT56: nests the selected clips into a compound clip.</summary>
    public const string MakeCompoundMenu = "Make c_ompound clip";

    /// <summary>WT56: flattens the selected compound clip.</summary>
    public const string BreakApartMenu = "_Break apart compound clip";

    /// <summary>WT56: Make compound without a run of clips on one track selected.</summary>
    public const string CompoundNeedsRun = "Select clips on one track to make a compound clip of them.";

    /// <summary>WT56: Break apart without one compound selected.</summary>
    public const string BreakApartNeedsCompound = "Select one compound clip to break it apart.";

    /// <summary>WT56: the name a new compound gets.</summary>
    /// <param name="number">How many compounds the timeline will hold with it.</param>
    /// <returns>e.g. "Compound 1".</returns>
    public static string NewCompoundName(int number) => string.Create(CultureInfo.InvariantCulture, $"Compound {number}");

    /// <summary>WT56: what making a compound did.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="items">How many items went in.</param>
    /// <returns>The status.</returns>
    public static string CompoundMade(string name, int items) =>
        string.Create(CultureInfo.InvariantCulture, $"Nested {items} {(items == 1 ? "item" : "items")} into {name}.");

    /// <summary>WT56: what breaking a compound apart did.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The status.</returns>
    public static string CompoundBroken(string name) => $"Broke {name} apart onto its track.";

    /// <summary>WT54: adds a marker at the playhead, or edits the one there.</summary>
    public const string AddMarkerMenu = "Add _marker";

    /// <summary>WT54: the gesture shown beside Add marker.</summary>
    public const string AddMarkerGesture = "M";

    /// <summary>WT54: the marker editor's title.</summary>
    public const string MarkerTitle = "Marker";

    /// <summary>WT54: the editor's name field.</summary>
    public const string MarkerNameLabel = "Name";

    /// <summary>WT54: the editor's colour field.</summary>
    public const string MarkerColorLabel = "Colour";

    /// <summary>WT54: the editor's length field.</summary>
    public const string MarkerFramesLabel = "Length in frames (0 for a point)";

    /// <summary>WT54: the editor's note field.</summary>
    public const string MarkerCommentLabel = "Note";

    /// <summary>WT54: the editor's save button.</summary>
    public const string MarkerSave = "Save";

    /// <summary>WT54: a length that is not a whole number of frames.</summary>
    public const string MarkerFramesWrong = "The length is a whole number of frames, 0 or more.";

    /// <summary>WT54: what a marker with no name is called.</summary>
    public const string UnnamedMarker = "Marker";

    /// <summary>WT54: the name a new marker gets.</summary>
    /// <param name="number">How many the timeline will have with it.</param>
    /// <returns>e.g. "Marker 4".</returns>
    public static string NewMarkerName(int number) => string.Create(CultureInfo.InvariantCulture, $"Marker {number}");

    /// <summary>WT54: a marker as UI Automation reads it.</summary>
    /// <param name="color">Its colour, e.g. RED.</param>
    /// <param name="timecode">Where it is.</param>
    /// <param name="lane">The lane it is on, or null for the timeline's own.</param>
    /// <param name="frames">How long it is, in frames.</param>
    /// <returns>e.g. "RED marker on V1 at 01:00:03:18, 15 frames".</returns>
    public static string MarkerStatus(string color, string timecode, string? lane, long frames) =>
        string.Create(CultureInfo.InvariantCulture, $"{color} marker {(lane is null ? "on the timeline" : "on " + lane)} at {timecode}{(frames > 0 ? $", {frames} frames" : string.Empty)}");

    /// <summary>WT54: what adding a marker did.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="timecode">Where.</param>
    /// <returns>The status.</returns>
    public static string MarkerAdded(string name, string timecode) => $"Added {name} at {timecode}; M again edits it.";

    /// <summary>WT53: snapping on or off, from the View menu or S.</summary>
    public const string SnappingMenu = "S_napping";

    /// <summary>WT53: the gesture shown beside Snapping.</summary>
    public const string SnappingGesture = "S";

    /// <summary>WT53: the label beside a snap guide: how far the snap moved the drag, e.g. "+2f".</summary>
    /// <param name="frames">The frames.</param>
    /// <returns>The label.</returns>
    public static string SnapDelta(long frames) => string.Create(CultureInfo.InvariantCulture, $"{(frames > 0 ? "+" : string.Empty)}{frames}f");

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
