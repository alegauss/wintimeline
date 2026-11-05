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
        if (ActiveTimeline is null || (Keyboard.FocusedElement != Timeline && Keyboard.FocusedElement != Viewer))
        {
            return;
        }

        Action? act = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.C, ModifierKeys.Control) => CopyClips,
            (Key.V, ModifierKeys.Control) => () => PasteClips(insert: false),
            (Key.V, ModifierKeys.Control | ModifierKeys.Shift) => () => PasteClips(insert: true),
            (Key.V, ModifierKeys.Control | ModifierKeys.Alt) => PasteClipAttributes,
            _ => null,
        };
        if (act is not null)
        {
            act();
            e.Handled = true;
        }
    }

    private void OnCopyClips(object sender, RoutedEventArgs e) => CopyClips();

    private void OnPasteClips(object sender, RoutedEventArgs e) => PasteClips(insert: false);

    private void OnPasteInsert(object sender, RoutedEventArgs e) => PasteClips(insert: true);

    private void OnPasteAttributes(object sender, RoutedEventArgs e) => PasteClipAttributes();

    /// <summary>WT78: what the clipboard holds as text, this window's last copy when another app holds the clipboard.</summary>
    private string? ClipboardText()
    {
        string? text = null;
        bool read = Retry(() => text = Clipboard.ContainsData(ClipFragment.Format) ? Clipboard.GetData(ClipFragment.Format) as string
            : Clipboard.ContainsText() ? Clipboard.GetText() : null);
        return read ? text : _copied;
    }

    /// <summary>WT78: puts chosen attributes of the clip on the clipboard onto the selected clips, as Ctrl+Alt+V does.</summary>
    public void PasteClipAttributes()
    {
        if (TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<Item> onto = [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().Select(i => i.Item).OfType<Item>()];
        if (ClipboardText() is not { } text || ClipFragment.Read(text)?.SelectMany(l => l.Items).OfType<Item>().FirstOrDefault(i => i is not Gap) is not { } from)
        {
            StatusText.Text = UiText.NothingToPaste;
            return;
        }

        if (onto.Count == 0)
        {
            StatusText.Text = UiText.PasteAttributesNeedsSelection;
            return;
        }

        PasteAttributesWindow choose = new(from.Name) { Owner = this };
        if (choose.ShowDialog() != true || choose.Chosen == ClipAttributes.None)
        {
            return;
        }

        int changed;
        using (History.BeginTransaction(UiText.Plain(UiText.PasteAttributesMenu)))
        {
            changed = PasteAttributes.Apply(History, from, onto, choose.Chosen);
        }

        StatusText.Text = UiText.AttributesPasted(from.Name, changed);
    }

    /// <summary>Puts the selected clips on the clipboard.</summary>
    public void CopyClips()
    {
        if (ActiveTimeline is not { } timeline || TimelinePanel.Layout is not { } layout)
        {
            return;
        }

        List<Composable> items = [.. TimelinePanel.Selection.Ids.Select(layout.Find).OfType<LaneItem>().Select(i => i.Item)];
        if (ClipFragment.Copy(timeline, items, DocumentPath is { } path ? Path.GetDirectoryName(path) : null, TimelinePanel.Shown) is not { } fragment)
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

    /// <summary>Pastes what the clipboard holds at the playhead, over what is there or pushing it later.</summary>
    /// <param name="insert">Whether to insert (Ctrl+Shift+V) rather than overwrite (Ctrl+V).</param>
    public void PasteClips(bool insert)
    {
        if (ActiveTimeline is not { } timeline)
        {
            return;
        }

        if (ClipboardText() is not { } fragment)
        {
            StatusText.Text = UiText.NothingToPaste;
            return;
        }

        try
        {
            PastePlan plan;
            using (History.TransactionScope paste = History.BeginTransaction(UiText.PasteStep))
            {
                try
                {
                    plan = ClipFragment.Paste(History, timeline, fragment, TimelinePanel.PlayheadSeconds, insert, TimelinePanel.Shown);
                }
                catch (EditRefusedException)
                {
                    paste.Cancel();
                    throw;
                }
            }

            StatusText.Text = UiText.Pasted(plan.Items, string.Join(", ", plan.Lanes), TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds), insert);
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
