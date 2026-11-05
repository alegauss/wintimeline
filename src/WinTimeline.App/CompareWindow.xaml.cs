using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Compare;

namespace WinTimeline.App;

/// <summary>
/// WT21: the change list from another timeline of the document to the one shown, as a list of
/// sentences; the earlier version is chosen from the document's other timelines.
/// </summary>
public partial class CompareWindow : Window
{
    private readonly Timeline _shown;
    private readonly History _history;
    private readonly TimelinePanel? _panel;
    private TimelineDiff? _diff;

    /// <summary>Opens the comparison.</summary>
    /// <param name="timelines">The document's timelines.</param>
    /// <param name="shown">The one shown, compared as the later version.</param>
    /// <param name="history">WT162: the shown timeline's history, which marking the changes runs through.</param>
    /// <param name="panel">WT163: the timeline the comparison is shown on as bands, and whose selection picks a row; null for none.</param>
    public CompareWindow(IReadOnlyList<Timeline> timelines, Timeline shown, History history, TimelinePanel? panel = null)
    {
        ArgumentNullException.ThrowIfNull(timelines);
        ArgumentNullException.ThrowIfNull(history);
        _shown = shown;
        _history = history;
        _panel = panel;
        if (panel is not null)
        {
            panel.Selection.Changed += OnTimelineSelection;
            Closed += (_, _) =>
            {
                panel.Selection.Changed -= OnTimelineSelection;
                panel.ShowComparison(null);
            };
        }
        InitializeComponent();
        ToName.Text = UiText.CompareTo(shown.Name);
        List<Timeline> others = [.. timelines.Where(t => !ReferenceEquals(t, shown))];
        foreach (Timeline other in others)
        {
            CompareWith.Items.Add(new ComboBoxItem { Content = other.Name, Tag = other });
        }

        CompareWith.SelectedIndex = DefaultFrom(timelines, shown) is { } from ? others.IndexOf(from) : -1;
    }

    /// <summary>The version a timeline is compared with first: the one before it, else the one after.</summary>
    /// <param name="timelines">The document's timelines.</param>
    /// <param name="shown">The one shown.</param>
    /// <returns>The earlier version, or null when there is no other.</returns>
    public static Timeline? DefaultFrom(IReadOnlyList<Timeline> timelines, Timeline shown)
    {
        ArgumentNullException.ThrowIfNull(timelines);
        int at = timelines.ToList().IndexOf(shown);
        return at > 0 ? timelines[at - 1] : timelines.FirstOrDefault(t => !ReferenceEquals(t, shown));
    }

    private void OnChosen(object sender, SelectionChangedEventArgs e)
    {
        if (CompareWith.SelectedItem is not ComboBoxItem { Tag: Timeline from })
        {
            return;
        }

        TimelineDiff diff = TimelineDiff.Compare(from, _shown);
        _diff = diff;
        ChangeList.Items.Clear();
        foreach (ClipChange change in diff.Changes)
        {
            ChangeList.Items.Add(new ListBoxItem { Content = change.Text });
        }

        CompareSummary.Text = diff.Summary;
        _panel?.ShowComparison(diff, Colour);
    }

    // WT163: a clip selected on the timeline picks its change's row.
    private void OnTimelineSelection(object? sender, EventArgs e)
    {
        if (_diff is not { } diff || _panel?.Layout is not { } layout)
        {
            return;
        }

        foreach (string id in _panel.Selection.Ids)
        {
            if (layout.Find(id) is { } item && _panel.ChangeOf(item) is { } change)
            {
                ChangeList.SelectedIndex = diff.Changes.ToList().IndexOf(change);
                ChangeList.ScrollIntoView(ChangeList.SelectedItem);
                return;
            }
        }
    }

    /// <summary>WT163: a change kind's colour, from the Fluent theme's semantic brushes so it reads in light and dark.</summary>
    private Vortice.Mathematics.Color4 Colour(ChangeKind kind)
    {
        (string key, System.Windows.Media.Color fallback) = kind switch
        {
            ChangeKind.Removed => ("SystemFillColorCriticalBrush", System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C)),
            ChangeKind.Added => ("SystemFillColorSuccessBrush", System.Windows.Media.Color.FromRgb(0x0F, 0x7B, 0x0F)),
            ChangeKind.Replaced => ("AccentFillColorDefaultBrush", System.Windows.Media.Color.FromRgb(0x00, 0x5F, 0xB8)),
            ChangeKind.Moved => ("SystemFillColorAttentionBrush", System.Windows.Media.Color.FromRgb(0x00, 0x5F, 0xB7)),
            _ => ("SystemFillColorCautionBrush", System.Windows.Media.Color.FromRgb(0x9D, 0x5D, 0x00)),
        };
        System.Windows.Media.Color c = TryFindResource(key) is System.Windows.Media.SolidColorBrush brush ? brush.Color : fallback;
        return new Vortice.Mathematics.Color4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    // WT162: the changes as a CMX 3600 change list, for an online or sound editor.
    private void OnExportChangeList(object sender, RoutedEventArgs e)
    {
        if (_diff is not { Changes.Count: > 0 } diff)
        {
            CompareSummary.Text = UiText.NoChangesToHandOn;
            return;
        }

        Microsoft.Win32.SaveFileDialog ask = new()
        {
            Title = UiText.ExportChangeListTitle,
            Filter = UiText.EdlFilter,
            FileName = UiText.ChangeListFileName(_shown.Name),
            DefaultExt = ".edl",
        };
        if (ask.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(ask.FileName, Model.Compare.ChangeList.Edl(diff, _shown).Replace("\n", "\r\n", StringComparison.Ordinal));
            CompareSummary.Text = UiText.ChangeListWritten(diff.Changes.Count, Path.GetFileName(ask.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CompareSummary.Text = UiText.ChangeListNotWritten(Path.GetFileName(ask.FileName), ex.Message);
        }
    }

    // WT162: a marker per change on the shown timeline, one undo step in its history.
    private void OnMarkChanges(object sender, RoutedEventArgs e)
    {
        if (_diff is not { Changes.Count: > 0 } diff)
        {
            CompareSummary.Text = UiText.NoChangesToHandOn;
            return;
        }

        int marked;
        using (_history.BeginTransaction(UiText.MarkChanges))
        {
            marked = Model.Compare.ChangeList.Mark(_history, diff, _shown);
        }

        CompareSummary.Text = UiText.ChangesMarked(marked, _shown.Name);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
