using System.Windows;
using System.Windows.Controls;
using WinTimeline.Model;
using WinTimeline.Model.Compare;

namespace WinTimeline.App;

/// <summary>
/// WT21: the change list from another timeline of the document to the one shown, as a list of
/// sentences; the earlier version is chosen from the document's other timelines.
/// </summary>
public partial class CompareWindow : Window
{
    private readonly Timeline _shown;

    /// <summary>Opens the comparison.</summary>
    /// <param name="timelines">The document's timelines.</param>
    /// <param name="shown">The one shown, compared as the later version.</param>
    public CompareWindow(IReadOnlyList<Timeline> timelines, Timeline shown)
    {
        ArgumentNullException.ThrowIfNull(timelines);
        _shown = shown;
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
        ChangeList.Items.Clear();
        foreach (ClipChange change in diff.Changes)
        {
            ChangeList.Items.Add(new ListBoxItem { Content = change.Text });
        }

        CompareSummary.Text = diff.Summary;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
