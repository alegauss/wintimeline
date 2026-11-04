using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using WinTimeline.Engine.Timeline;
using WinTimeline.Model;
using WinTimeline.Model.Commands;

namespace WinTimeline.App;

/// <summary>
/// WT160: the selected clips copied to the clipboard as an OTIO fragment (with the same JSON as
/// plain text, for other tools), and pasted at the playhead into the shown timeline, which may be
/// another tab of the collection, as one undo step.
/// </summary>
public partial class MainWindow
{
    // What this window last copied, for when the clipboard cannot be opened (another app holds it).
    private string? _copied;

    private void BindClipboard() => PreviewKeyDown += OnClipboardKey;

    // Only while a surface has the focus: anywhere else Ctrl+C and Ctrl+V are the text box's.
    private void OnClipboardKey(object sender, KeyEventArgs e)
    {
        if (ActiveTimeline is null || Keyboard.Modifiers != ModifierKeys.Control || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        if (e.Key == Key.C)
        {
            CopyClips();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            PasteClips();
            e.Handled = true;
        }
    }

    private void OnCopyClips(object sender, RoutedEventArgs e) => CopyClips();

    private void OnPasteClips(object sender, RoutedEventArgs e) => PasteClips();

    /// <summary>Puts the selected clips on the clipboard.</summary>
    public void CopyClips()
    {
        if (ActiveTimeline is not { } timeline || TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<Composable> items = [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().Select(i => i.Item)];
        if (ClipFragment.Copy(timeline, items, DocumentPath is { } path ? Path.GetDirectoryName(path) : null) is not { } fragment)
        {
            StatusText.Text = UiText.NothingToCopy;
            return;
        }

        _copied = fragment;
        DataObject data = new();
        data.SetData(ClipFragment.Format, fragment);
        data.SetText(fragment);
        bool placed = Retry(() => Clipboard.SetDataObject(data, copy: true));
        StatusText.Text = UiText.Copied(items.Count(i => i is not Gap), placed);
    }

    /// <summary>Pastes what the clipboard holds at the playhead.</summary>
    public void PasteClips()
    {
        if (ActiveTimeline is not { } timeline)
        {
            return;
        }

        string? fragment = null;
        bool read = Retry(() => fragment = Clipboard.ContainsData(ClipFragment.Format) ? Clipboard.GetData(ClipFragment.Format) as string
            : Clipboard.ContainsText() ? Clipboard.GetText() : null);
        fragment = read ? fragment : _copied;
        if (fragment is null)
        {
            StatusText.Text = UiText.NothingToPaste;
            return;
        }

        try
        {
            PastePlan plan = ClipFragment.Plan(timeline, fragment, TimelinePanel.PlayheadSeconds);
            using History.TransactionScope paste = History.BeginTransaction(UiText.PasteStep);
            try
            {
                foreach (IEditCommand command in plan.Commands)
                {
                    History.Execute(command);
                }
            }
            catch (EditRefusedException)
            {
                paste.Cancel();
                throw;
            }

            StatusText.Text = UiText.Pasted(plan.Items, string.Join(", ", plan.Lanes), TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds));
        }
        catch (EditRefusedException refused)
        {
            StatusText.Text = refused.Message;
        }
    }

    // The clipboard is shared with every app, and one holding it open makes a call throw; a few
    // tries a moment apart get past that. False when it never opened.
    private static bool Retry(Action act)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                act();
                return true;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
            catch (COMException)
            {
                return false;
            }
        }
    }
}
