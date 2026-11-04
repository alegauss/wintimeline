using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using WinTimeline.Diagnostics;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT17: the open document's recovery journal, the idle autosave behind it, and the offer, at
/// start, of edits a session that did not close cleanly left behind.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan IdleBeforeAutosave = TimeSpan.FromSeconds(60);

    private RecoveryJournal? _journal;
    private RecoveryCandidate? _offered;
    private DispatcherTimer? _autosaveTimer;
    private DateTime _lastChange = DateTime.MaxValue;

    /// <summary>recovery in the data folder.</summary>
    public static string RecoveryFolder => Path.Combine(SettingsStore.DataFolder, "recovery");

    /// <summary>What the offer says, e.g. "Edits to cut.otio were not saved when WinTimeline last closed: 3 edits."</summary>
    /// <param name="candidate">The session.</param>
    /// <returns>The sentence.</returns>
    public static string RecoveryOfferText(RecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return UiText.RecoveryOffer(candidate.Name, candidate.Edits);
    }

    /// <summary>What the status bar says after a recovery, e.g. "Recovered cut.otio: 3 clips on 1 track. Save to keep the edits."</summary>
    /// <param name="name">The document's file name.</param>
    /// <param name="recovered">What was recovered.</param>
    /// <returns>The sentence.</returns>
    public static string RecoveredText(string name, RecoveredDocument recovered)
    {
        ArgumentNullException.ThrowIfNull(recovered);
        return UiText.Recovered(name, recovered.Timeline.FindClips().Count(), recovered.Timeline.Tracks.Children.Count, recovered.FromAutosave);
    }

    private void StartJournal(string name)
    {
        _journal?.Discard();
        _journal = null;
        if (Document is not Timeline timeline)
        {
            return;
        }

        try
        {
            _journal = RecoveryJournal.Start(RecoveryFolder, timeline, History, name, DocumentPath);
            WatchForAutosave();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "The recovery journal could not be started; edits will not survive a crash.", error: ex);
        }
    }

    private void WatchForAutosave()
    {
        History.Changed += (_, _) => _lastChange = DateTime.UtcNow;
        if (_autosaveTimer is not null)
        {
            return;
        }

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autosaveTimer.Tick += (_, _) =>
        {
            if (_journal is not null && DateTime.UtcNow - _lastChange >= IdleBeforeAutosave)
            {
                _lastChange = DateTime.MaxValue;
                try
                {
                    _journal.Autosave();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Write(LogLevel.Warning, LogCategory.App, "Autosave failed.", error: ex);
                }
            }
        };
        _autosaveTimer.Start();
        Closed += (_, _) =>
        {
            _autosaveTimer.Stop();

            // A clean close: nothing to recover.
            _journal?.Discard();
        };
    }

    private void OfferRecovery()
    {
        IReadOnlyList<RecoveryCandidate> pending = RecoveryJournal.Pending(RecoveryFolder, _journal?.Id);
        if (pending.Count == 0)
        {
            return;
        }

        _offered = pending[0];
        EditsRecoveryText.Text = RecoveryOfferText(_offered);
        EditsRecoveryBar.Visibility = Visibility.Visible;
    }

    private void OnRecoverEdits(object sender, RoutedEventArgs e)
    {
        if (_offered is not { } candidate)
        {
            return;
        }

        EditsRecoveryBar.Visibility = Visibility.Collapsed;
        try
        {
            RecoveredDocument recovered = RecoveryJournal.Resume(RecoveryFolder, candidate, out RecoveryJournal journal);
            _journal?.Discard();
            _journal = journal;
            _bundle = null;
            Document = recovered.Timeline;
            LoadTimelines(recovered.Timeline);
            _histories[recovered.Timeline] = History = recovered.History;
            History.Guard = TrackCommands.LockGuard(recovered.Timeline);
            ShowWelcome();
            DocumentPath = candidate.SourcePath;
            WatchForAutosave();
            Title = UiText.DocumentTitle(candidate.Name);
            StatusText.Text = RecoveredText(candidate.Name, recovered);
            Log.Write(LogLevel.Info, LogCategory.App, "Edits recovered.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = candidate.Name, ["edits"] = candidate.Edits.ToString(System.Globalization.CultureInfo.InvariantCulture), ["autosave"] = recovered.FromAutosave ? "true" : "false" });
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = UiText.CouldNotRecover(candidate.Name, ex.Message);
            Notify(new Notices.Notice(Notices.NoticeSeverity.Error, UiText.CouldNotRecover(candidate.Name), ex.Message, []));
            Log.Write(LogLevel.Error, LogCategory.App, "Edits could not be recovered.", error: ex);
        }
    }

    private void OnDiscardEdits(object sender, RoutedEventArgs e)
    {
        EditsRecoveryBar.Visibility = Visibility.Collapsed;
        if (_offered is { } candidate)
        {
            RecoveryJournal.Remove(RecoveryFolder, candidate.Id);
        }
    }

    /// <summary>
    /// <c>--run-commands</c>: applies a file of commands, one JSON object per line as
    /// <see cref="IEditCommand.ToJson"/> writes them, to the open document through its history.
    /// </summary>
    private void RunCommands(string path)
    {
        if (Document is not Timeline timeline)
        {
            StatusText.Text = UiText.RunCommandsNeedsTimeline;
            return;
        }

        int ran = 0;
        try
        {
            foreach (string line in File.ReadLines(path).Where(l => l.Trim().Length > 0))
            {
                History.Execute(CommandJson.Parse(timeline, JsonNode.Parse(line)!.AsObject()));
                ran++;
            }

            StatusText.Text = UiText.RanCommands(ran, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            StatusText.Text = UiText.CommandsStopped(Path.GetFileName(path), ran + 1, ex.Message);
            Notify(new Notices.Notice(Notices.NoticeSeverity.Warning, UiText.CommandsStopped(Path.GetFileName(path), ran + 1), ex.Message, []));
        }
    }
}
