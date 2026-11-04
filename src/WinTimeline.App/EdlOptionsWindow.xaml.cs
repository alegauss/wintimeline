using System.Windows;
using WinTimeline.Model.Adapters;

namespace WinTimeline.App;

/// <summary>WT158: asks the frame rate an EDL is read at, and whether to ignore record timecode mismatches.</summary>
public partial class EdlOptionsWindow : Window
{
    /// <summary>Opens the question for a file.</summary>
    /// <param name="file">The EDL's file name.</param>
    /// <param name="rate">The rate offered first.</param>
    /// <param name="ignoreMismatch">Whether the mismatch option starts ticked.</param>
    public EdlOptionsWindow(string file, double rate, bool ignoreMismatch)
    {
        InitializeComponent();
        EdlQuestion.Text = UiText.EdlQuestion(file);
        foreach (double standard in EdlRates.Standard)
        {
            EdlRate.Items.Add(EdlRates.Label(standard));
        }

        EdlRate.SelectedItem = EdlRates.Label(rate);
        if (EdlRate.SelectedIndex < 0)
        {
            EdlRate.SelectedItem = EdlRates.Label(24);
        }

        EdlIgnoreMismatch.IsChecked = ignoreMismatch;
    }

    /// <summary>The rate chosen.</summary>
    public double Rate => EdlRates.Standard[Math.Max(0, EdlRate.SelectedIndex)];

    /// <summary>Whether to ignore record timecode mismatches.</summary>
    public bool IgnoreMismatch => EdlIgnoreMismatch.IsChecked == true;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
