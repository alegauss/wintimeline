using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinTimeline.App.Palette;

/// <summary>
/// WT66: the palette. Typing re-ranks the results; Up and Down move through them; Enter (or Run,
/// or a double click) chooses the selected one and closes the palette; Escape closes it.
/// </summary>
public partial class PaletteWindow : Window
{
    private readonly Func<string, IReadOnlyList<PaletteResult>> _search;

    /// <summary>Opens the palette.</summary>
    /// <param name="search">What it offers for what was typed.</param>
    public PaletteWindow(Func<string, IReadOnlyList<PaletteResult>> search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        InitializeComponent();
        Show(string.Empty);
        Loaded += (_, _) => PaletteQuery.Focus();
    }

    /// <summary>What was chosen, or null when the palette was closed without a choice.</summary>
    public PaletteResult? Chosen { get; private set; }

    private void Show(string typed)
    {
        PaletteResults.Items.Clear();
        foreach (PaletteResult result in _search(typed))
        {
            PaletteResults.Items.Add(new ListBoxItem { Content = result.Text, Tag = result });
        }

        PaletteResults.SelectedIndex = PaletteResults.Items.Count > 0 ? 0 : -1;
        PaletteRun.IsEnabled = PaletteResults.Items.Count > 0;
    }

    private void OnTyped(object sender, TextChangedEventArgs e) => Show(PaletteQuery.Text);

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        int move = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (move == 0 || PaletteResults.Items.Count == 0)
        {
            return;
        }

        PaletteResults.SelectedIndex = Math.Clamp(PaletteResults.SelectedIndex + move, 0, PaletteResults.Items.Count - 1);
        PaletteResults.ScrollIntoView(PaletteResults.SelectedItem);
        e.Handled = true;
    }

    private void OnRun(object sender, RoutedEventArgs e)
    {
        if (PaletteResults.SelectedItem is ListBoxItem { Tag: PaletteResult result })
        {
            Chosen = result;
            DialogResult = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
