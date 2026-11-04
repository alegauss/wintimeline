using System.Windows;
using Microsoft.Win32;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Media;

namespace WinTimeline.App;

/// <summary>
/// WT18: lists the clips whose media is missing; locating one file relinks every clip the
/// choice implies, as one undoable step in the document's history.
/// </summary>
public partial class RelinkWindow : Window
{
    private readonly Timeline _timeline;
    private readonly History _history;
    private readonly string? _documentFolder;
    private IReadOnlyList<MissingMedia> _missing = [];

    /// <summary>Opens the dialog over a timeline.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="history">Its history, which the relink goes through.</param>
    /// <param name="documentFolder">The document's folder, for relative references.</param>
    public RelinkWindow(Timeline timeline, History history, string? documentFolder)
    {
        _timeline = timeline;
        _history = history;
        _documentFolder = documentFolder;
        InitializeComponent();
        Refresh();
    }

    /// <summary>What the dialog says after a relink, e.g. "Relinked 3 clips. 1 is still missing."</summary>
    /// <param name="relinked">How many were relinked.</param>
    /// <param name="stillMissing">How many are still missing.</param>
    /// <returns>The sentence.</returns>
    public static string ResultText(int relinked, int stillMissing) =>
        UiText.Relinked(relinked, stillMissing);

    /// <summary>How a missing clip is listed.</summary>
    /// <param name="missing">The clip.</param>
    /// <returns>The line.</returns>
    public static string Line(MissingMedia missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        string where = missing.Expected ?? (missing.Reference as ExternalReference)?.TargetUrl ?? UiText.NoFile;
        return UiText.MissingLine(missing.Clip.Name, where);
    }

    private void Refresh()
    {
        _missing = MediaLinks.Missing(_timeline, _documentFolder);
        MissingList.ItemsSource = _missing.Select(Line).ToList();
        if (_missing.Count > 0)
        {
            MissingList.SelectedIndex = 0;
        }

        LocateButton.IsEnabled = _missing.Count > 0;
    }

    private void OnLocate(object sender, RoutedEventArgs e)
    {
        if (MissingList.SelectedIndex < 0 || MissingList.SelectedIndex >= _missing.Count)
        {
            return;
        }

        MissingMedia located = _missing[MissingList.SelectedIndex];
        OpenFileDialog dialog = new() { Title = UiText.LocateTitle(located.Clip.Name), FileName = located.Expected is { } path ? System.IO.Path.GetFileName(path) : string.Empty };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        RelinkPlan plan = RelinkPlan.From(_missing, located, dialog.FileName);
        using (_history.BeginTransaction(UiText.RelinkStep(plan.Found.Count)))
        {
            foreach ((MissingMedia m, string file) in plan.Found)
            {
                _history.Execute(new Relink(m.Clip, file));
            }
        }

        Refresh();
        RelinkStatus.Text = ResultText(plan.Found.Count, _missing.Count);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
