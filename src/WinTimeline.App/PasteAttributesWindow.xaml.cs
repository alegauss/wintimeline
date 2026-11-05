using System.Windows;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>WT78: asks which of a copied clip's attributes go onto the selected clips.</summary>
public partial class PasteAttributesWindow : Window
{
    /// <summary>Opens the question for a clip.</summary>
    /// <param name="from">The copied clip's name.</param>
    public PasteAttributesWindow(string from)
    {
        InitializeComponent();
        AttributesQuestion.Text = UiText.PasteAttributesQuestion(from);
    }

    /// <summary>The attributes ticked.</summary>
    public ClipAttributes Chosen =>
        (AttributeEffects.IsChecked == true ? ClipAttributes.Effects : ClipAttributes.None)
        | (AttributeMarkers.IsChecked == true ? ClipAttributes.Markers : ClipAttributes.None)
        | (AttributeColor.IsChecked == true ? ClipAttributes.Color : ClipAttributes.None)
        | (AttributeMetadata.IsChecked == true ? ClipAttributes.Metadata : ClipAttributes.None);

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
