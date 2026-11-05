using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT20: every timeline a document holds, one tab each over the timeline panel, each with its
/// own undo history. A file of one timeline shows no tabs.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<Timeline, History> _histories = new(ReferenceEqualityComparer.Instance);

    /// <summary>The timeline the panels show, or null when no document is open.</summary>
    public Timeline? ActiveTimeline { get; private set; }

    /// <summary>The document's timelines, in file order.</summary>
    public IReadOnlyList<Timeline> Timelines { get; private set; } = [];

    /// <summary>What the status bar says when a tab is chosen, e.g. "Showing reel2: 3 clips on 1 track."</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The sentence.</returns>
    public static string ShowingText(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return UiText.Showing(timeline.Name, timeline.FindClips().Count(), timeline.Tracks.Children.Count);
    }

    private void LoadTimelines(ModelObject document)
    {
        _histories.Clear();
        Timelines = DocumentTimelines.Of(document);
        RebuildTabs();
        Activate(Timelines.Count > 0 ? Timelines[0] : null);
    }

    private void Activate(Timeline? timeline)
    {
        ActiveTimeline = timeline;
        TimelinePanel.Show(timeline);
        TrackHeaders.Show(timeline);
        if (timeline is null)
        {
            History = new History();
            return;
        }

        if (!_histories.TryGetValue(timeline, out History? history))
        {
            _histories[timeline] = history = new History { Guard = TrackCommands.LockGuard(timeline) };

            // WT161: a reel shown for the first time is journaled from now on.
            _journal?.Watch(timeline, history);
        }

        History = history;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RebuildTabs()
    {
        TimelineTabs.SelectionChanged -= OnTabChosen;
        TimelineTabs.Items.Clear();
        for (int i = 0; i < Timelines.Count; i++)
        {
            TabItem tab = new() { Header = Timelines[i].Name, Tag = Timelines[i] };
            AutomationProperties.SetAutomationId(tab, $"TimelineTab{i}");
            AutomationProperties.SetName(tab, Timelines[i].Name);
            TimelineTabs.Items.Add(tab);
        }

        TimelineTabs.Visibility = Timelines.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        TimelineTabs.SelectedIndex = ActiveTimeline is { } active && Timelines.ToList().IndexOf(active) is var at and >= 0 ? at : 0;
        TimelineTabs.SelectionChanged += OnTabChosen;
    }

    /// <summary>
    /// After an undo or redo: a version added or taken away changes the tabs. When the shown
    /// timeline is gone, the one its version was made from is shown.
    /// </summary>
    private void SyncTimelines()
    {
        if (Document is not SerializableCollection)
        {
            return;
        }

        IReadOnlyList<Timeline> now = DocumentTimelines.Of(Document);
        if (now.SequenceEqual(Timelines, ReferenceEqualityComparer.Instance))
        {
            return;
        }

        int at = ActiveTimeline is { } active ? Timelines.ToList().IndexOf(active) : 0;
        Timelines = now;
        if (ActiveTimeline is null || !now.Contains(ActiveTimeline, ReferenceEqualityComparer.Instance))
        {
            Activate(now.Count == 0 ? null : now[Math.Clamp(at - 1, 0, now.Count - 1)]);
        }

        RebuildTabs();
    }

    private void OnTabChosen(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineTabs.SelectedItem is TabItem { Tag: Timeline timeline } && !ReferenceEquals(timeline, ActiveTimeline))
        {
            Activate(timeline);
            StatusText.Text = ShowingText(timeline);
        }
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        if (ActiveTimeline is not { } shown)
        {
            StatusText.Text = UiText.CompareNeedsTimeline;
            return;
        }

        if (Timelines.Count < 2)
        {
            StatusText.Text = UiText.CompareNeedsSecond;
            return;
        }

        new CompareWindow(Timelines, shown) { Owner = this }.ShowDialog();
    }

    private void OnDuplicateTimeline(object sender, RoutedEventArgs e)
    {
        if (ActiveTimeline is not { } timeline)
        {
            return;
        }

        if (timeline.Owner is not SerializableCollection)
        {
            StatusText.Text = UiText.OnlyTimeline(timeline.Name);
            return;
        }

        AddToCollection command = DuplicateTimeline.Command(timeline);
        History.Execute(command);
        Timeline copy = (Timeline)command.Child;
        Timelines = DocumentTimelines.Of(Document!);
        Activate(copy);
        RebuildTabs();
        StatusText.Text = UiText.Added(copy.Name) + ShowingText(copy);
    }
}
