using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WinTimeline.Diagnostics;
using WinTimeline.Model;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.App;

/// <summary>
/// WT13: the open document — File > Open, Save and Save As, and <c>--open</c> at start. The
/// title names the file; the status bar says what was opened or saved, or why it was not.
/// </summary>
public partial class MainWindow
{
    // WT14: the bundle the document came from, which knows where its media is.
    private OtioBundle? _bundle;

    /// <summary>Where extracted bundle media is kept, a folder per bundle.</summary>
    public static string BundleCache => Path.Combine(SettingsStore.DataFolder, "bundles");

    /// <summary>
    /// The bundle a path names, or null: an .otioz or .otiod itself, or the content.otio inside an
    /// .otiod, which is what a file dialog can pick.
    /// </summary>
    /// <param name="path">A picked path.</param>
    /// <returns>The bundle's path, or null.</returns>
    public static string? BundlePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (OtioBundle.IsBundle(path))
        {
            return path;
        }

        string? folder = Path.GetDirectoryName(path);
        return string.Equals(Path.GetFileName(path), "content.otio", StringComparison.OrdinalIgnoreCase) && folder is not null && OtioBundle.IsBundle(folder)
            ? folder
            : null;
    }

    /// <summary>The open document, or null.</summary>
    public ModelObject? Document { get; private set; }

    /// <summary>WT16: the open document's undo history; every edit goes through it.</summary>
    public History History { get; private set; } = new();

    /// <summary>Where the open document lives, or null when none is open.</summary>
    public string? DocumentPath { get; private set; }

    /// <summary>What the status bar says about an opened document, e.g. "Opened cut.otio: 4 clips on 1 track."</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="document">What it held.</param>
    /// <returns>The sentence.</returns>
    public static string OpenedText(string fileName, ModelObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is SerializableCollection)
        {
            int count = DocumentTimelines.Of(document).Count;
            return UiText.OpenedTimelines(fileName, count);
        }

        if (document is not Timeline timeline)
        {
            return UiText.OpenedNotATimeline(fileName, document.Schema);
        }

        return UiText.Opened(fileName, timeline.FindClips().Count(), timeline.Tracks.Children.Count);
    }

    /// <summary>Opens a file as the document, saying in the status bar how it went.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether it opened.</returns>
    public bool OpenDocument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = BundlePath(path) ?? path;
        string name = Path.GetFileName(path);

        // WT164: the layout of the file being left is kept before it goes.
        KeepLayout();
        try
        {
            OtioBundle? bundle = OtioBundle.IsBundle(path) ? OtioBundle.Open(path, BundleCache) : null;

            // WT15: anything else through the adapter its extension names; .otio when none does.
            // WT157: a format only Python reads waits for the bridge the start is still looking for.
            if (TimelineAdapters.ForPath(path) is null && !OtioBundle.IsBundle(path))
            {
                AwaitBridge();
            }

            ITimelineAdapter adapter = TimelineAdapters.ForPath(path) ?? TimelineAdapters.All[0];
            AdapterOptions? options = bundle is null ? OptionsFor(path, adapter) : new AdapterOptions();
            if (options is null)
            {
                StatusText.Text = UiText.EdlCancelled(name);
                return false;
            }

            ModelObject document = bundle?.Content ?? adapter.Read(path, options);
            _bundle = bundle;
            Document = document;
            LoadTimelines(document);

            // An imported file (an EDL) is not written back over: Save asks where the .otio goes.
            DocumentPath = bundle is not null || adapter.CanWrite ? Path.GetFullPath(path) : null;
            StartJournal(name);
            Title = UiText.DocumentTitle(name);
            string restored = RestoreProject();
            StatusText.Text = OpenedText(name, document) + MissingText()
                + (ActiveTimeline is { } opened ? ProblemsWindow.CountText(Model.Validation.Validator.Check(opened).Count) : string.Empty)
                + restored;
            Log.Write(LogLevel.Info, LogCategory.App, "Document opened.", Fields(path, document));
            Remember(Path.GetFullPath(path));
            ShowWelcome();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EdlParseException or FcpXmlException or FcpxXmlException or PythonBridgeException)
        {
            string reason = ex is FileNotFoundException or DirectoryNotFoundException ? UiText.NotFound : ex.Message;
            if (ex is EdlParseException && File.Exists(path))
            {
                // WT158: a refusal for frames beyond the rate names the rates the EDL does fit.
                reason += UiText.EdlFits([.. EdlRates.Fitting(File.ReadAllText(path)).Select(EdlRates.Label)]);
            }
            StatusText.Text = UiText.CouldNotOpen(name, reason);
            Notify(new Notices.Notice(Notices.NoticeSeverity.Error, UiText.CouldNotOpen(name), reason, [new Notices.NoticeAction(UiText.OpenAnother, AskToOpen)]));
            Log.Write(LogLevel.Warning, LogCategory.App, "Document could not be opened.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
            return false;
        }
    }

    /// <summary>Saves the document to a path, saying in the status bar how it went.</summary>
    /// <param name="path">Where.</param>
    /// <returns>Whether it saved.</returns>
    public bool SaveDocument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Document is null)
        {
            return false;
        }

        string name = Path.GetFileName(path);
        try
        {
            if (OtioBundle.IsBundle(path))
            {
                SaveBundle(path);
            }
            else
            {
                OtioFile.Save(Document, path);
            }

            DocumentPath = Path.GetFullPath(path);
            KeepProject(DocumentPath);
            _journal?.MarkSaved();
            Title = UiText.DocumentTitle(name);
            StatusText.Text = UiText.Saved(name);
            Log.Write(LogLevel.Info, LogCategory.App, "Document saved.", Fields(path, Document));
            Remember(DocumentPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BundleException)
        {
            StatusText.Text = UiText.CouldNotSave(name, ex.Message);
            Notify(new Notices.Notice(Notices.NoticeSeverity.Error, UiText.CouldNotSave(name), ex.Message, [new Notices.NoticeAction(UiText.SaveAsAction, AskToSaveAs)]));
            Log.Write(LogLevel.Error, LogCategory.App, "Document could not be saved.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
            return false;
        }
    }

    /// <summary>
    /// Writes the document as a bundle with its media: media not on disk becomes a missing
    /// reference that says why, rather than failing the save. An .otioz that exists is replaced
    /// in one step, keeping the previous as .bak, as a .otio save does.
    /// </summary>
    private void SaveBundle(string path)
    {
        if (!File.Exists(path))
        {
            OtioBundle.Write(Document!, path, MediaReferencePolicy.MissingIfNotFile, _bundle is { } b ? b.MediaPath : null);
            return;
        }

        string fresh = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + ".new" + Path.GetExtension(path));
        File.Delete(fresh);
        OtioBundle.Write(Document!, fresh, MediaReferencePolicy.MissingIfNotFile, _bundle is { } open ? open.MediaPath : null);
        File.Replace(fresh, path, path + ".bak", ignoreMetadataErrors: true);
    }

    /// <summary>WT18: what the status bar adds when media is missing, e.g. " Media is missing for 2 clips: File > Relink media."</summary>
    /// <returns>The sentence, or empty.</returns>
    private string MissingText()
    {
        string? folder = DocumentPath is { } p ? Path.GetDirectoryName(p) : null;
        int missing = ActiveTimeline is { } t && _bundle is null ? Model.Media.MediaLinks.Missing(t, folder).Count : 0;
        return UiText.MediaMissing(missing);
    }

    /// <summary>
    /// WT158: what an adapter reads with. An EDL states no rate, so it asks, offering the last
    /// choice, unless --edl-rate gave one for the file --open names; any other format needs nothing.
    /// </summary>
    /// <returns>The options, or null when the question was cancelled.</returns>
    private AdapterOptions? OptionsFor(string path, ITimelineAdapter adapter)
    {
        if (!adapter.Suffixes.Contains(".edl", StringComparer.Ordinal))
        {
            return new AdapterOptions();
        }

        if (Options.EdlRate is { } given && Options.Open is { } open && string.Equals(Path.GetFullPath(open), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return new AdapterOptions(Rate: given);
        }

        EdlOptionsWindow ask = new(Path.GetFileName(path), Settings.Current.EdlRate ?? 24, Settings.Current.EdlIgnoreTimecodeMismatch ?? false);
        if (IsLoaded)
        {
            ask.Owner = this;
        }

        if (ask.ShowDialog() != true)
        {
            return null;
        }

        Settings.Update(s => s with { EdlRate = ask.Rate, EdlIgnoreTimecodeMismatch = ask.IgnoreMismatch });
        return new AdapterOptions(Rate: ask.Rate, IgnoreTimecodeMismatch: ask.IgnoreMismatch);
    }

    private void OnRelink(object sender, RoutedEventArgs e)
    {
        if (ActiveTimeline is not { } timeline)
        {
            StatusText.Text = UiText.RelinkNeedsTimeline;
            return;
        }

        new RelinkWindow(timeline, History, DocumentPath is { } p ? Path.GetDirectoryName(p) : null) { Owner = this }.ShowDialog();
    }

    private void OnProblems(object sender, RoutedEventArgs e)
    {
        if (ActiveTimeline is not { } timeline)
        {
            StatusText.Text = UiText.ProblemsNeedTimeline;
            return;
        }

        new ProblemsWindow(timeline, History) { Owner = this }.ShowDialog();
    }

    private void BindDocumentCommands()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => AskToOpen()));
        Closing += (_, _) => KeepLayout();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => { History.Undo(); SyncTimelines(); }, (_, e) => e.CanExecute = History.CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => { History.Redo(); SyncTimelines(); },(_, e) => e.CanExecute = History.CanRedo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(), (_, e) => e.CanExecute = Document is not null));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => AskToSaveAs(), (_, e) => e.CanExecute = Document is not null));
        if (Options.Open is { } path)
        {
            OpenDocument(path);
        }

        if (Options.RunCommands is { } commands)
        {
            RunCommands(commands);
        }

        OfferRecovery();
    }

    private void AskToOpen()
    {
        OpenFileDialog dialog = new() { Title = UiText.OpenDialogTitle, Filter = UiText.OpenFilter(TimelineAdapters.All.Where(a => a.CanRead).Select(a => (a.Name, a.Suffixes))) };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void Save()
    {
        if (DocumentPath is { } path)
        {
            SaveDocument(path);
        }
        else
        {
            AskToSaveAs();
        }
    }

    private void AskToSaveAs()
    {
        SaveFileDialog dialog = new()
        {
            Title = UiText.SaveDialogTitle,
            Filter = UiText.SaveFilter,
            DefaultExt = ".otio",
            FileName = DocumentPath is { } path ? Path.GetFileName(path) : "timeline.otio",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SaveDocument(dialog.FileName);
        }
    }

    private static Dictionary<string, string> Fields(string path, ModelObject document) => new(StringComparer.Ordinal)
    {
        ["file"] = Path.GetFileName(path),
        ["schema"] = document.Schema,
        ["clips"] = document is Timeline t ? t.FindClips().Count().ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
    };
}
