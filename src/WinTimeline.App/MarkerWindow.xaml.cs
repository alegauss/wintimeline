using System.Globalization;
using System.Windows;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;

namespace WinTimeline.App;

/// <summary>WT54: edits a marker's name, colour, length in frames and note; nothing changes until Save.</summary>
public partial class MarkerWindow : Window
{
    /// <summary>Opens the editor on a marker.</summary>
    /// <param name="marker">The marker.</param>
    /// <param name="rate">The rate its length is counted in.</param>
    public MarkerWindow(Marker marker, double rate)
    {
        ArgumentNullException.ThrowIfNull(marker);
        InitializeComponent();
        MarkerName.Text = marker.Name;
        foreach (string name in MarkerColors.Names)
        {
            MarkerColor.Items.Add(name);
        }

        string color = marker.Color.ToUpperInvariant();
        if (!MarkerColors.Names.Contains(color))
        {
            MarkerColor.Items.Add(color);
        }

        MarkerColor.SelectedItem = color;
        MarkerFrames.Text = Math.Round(marker.MarkedRange.Duration.Seconds * rate).ToString(CultureInfo.InvariantCulture);
        MarkerComment.Text = marker.Comment;
        Loaded += (_, _) =>
        {
            MarkerName.Focus();
            MarkerName.SelectAll();
        };
    }

    /// <summary>The name chosen.</summary>
    public string ChosenName => MarkerName.Text;

    /// <summary>The colour chosen.</summary>
    public string ChosenColor => MarkerColor.SelectedItem as string ?? Model.MarkerColor.Green;

    /// <summary>The length chosen, in frames.</summary>
    public long ChosenFrames { get; private set; }

    /// <summary>The note chosen.</summary>
    public string ChosenComment => MarkerComment.Text;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(MarkerFrames.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long frames))
        {
            MarkerProblem.Text = UiText.MarkerFramesWrong;
            MarkerFrames.Focus();
            return;
        }

        ChosenFrames = frames;
        DialogResult = true;
    }
}
