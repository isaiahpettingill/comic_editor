using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ComicEditor.Editing;

namespace ComicEditor.Views;

public partial class MainView
{
    private List<FrameClipboard>? clipboardFrames;
    private MenuItem? canvasCopyItem, canvasCutItem, canvasPasteItem;
    private ContextMenu? canvasClipboardMenu;
    private Point canvasContextPoint;

    private void AttachCanvasClipboardMenu()
    {
        if (canvas is null) return;
        canvasCopyItem = new MenuItem { Header = "Copy artwork", IsEnabled = false };
        canvasCutItem = new MenuItem { Header = "Cut artwork", IsEnabled = false };
        canvasPasteItem = new MenuItem { Header = "Paste artwork here", IsEnabled = clipboardArtwork is not null };
        canvasCopyItem.Click += (_, _) => CopySelection(false);
        canvasCutItem.Click += (_, _) => CopySelection(true);
        canvasPasteItem.Click += (_, _) => PasteSelectionAt((int)canvasContextPoint.X, (int)canvasContextPoint.Y);
        // Open this explicitly for mouse right clicks. Avalonia's attached menu
        // also opens for a touch/pen hold, which interrupts drawing.
        canvasClipboardMenu = new ContextMenu { Items = { canvasCopyItem, canvasCutItem, canvasPasteItem } };
    }

    private void PrepareCanvasClipboardMenu(Point point)
    {
        canvasContextPoint = point;
        if (canvasCopyItem is not null) canvasCopyItem.IsEnabled = selection?.Owner == editor.Layer;
        if (canvasCutItem is not null) canvasCutItem.IsEnabled = selection?.Owner == editor.Layer;
        if (canvasPasteItem is not null) canvasPasteItem.IsEnabled = clipboardArtwork is not null;
    }

    private void AttachFrameClipboardMenu(Border card, int index)
    {
        var copy = new MenuItem { Header = "Copy frame" };
        var paste = new MenuItem { Header = "Paste frame after this", IsEnabled = clipboardFrame is not null };
        copy.Click += (_, _) => CopyFrame(index);
        paste.Click += (_, _) => PasteFrame(index);
        card.ContextMenu = new ContextMenu { Items = { copy, paste } };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsRightButtonPressed) return;
            card.Focus(); paste.IsEnabled = clipboardFrame is not null;
        };
    }

    private int? FocusedFrameIndex(object? source)
    {
        if (source is not Visual visual || storyboard is null) return null;
        var row = new[] { visual }.Concat(visual.GetVisualAncestors()).OfType<Border>()
            .FirstOrDefault(item => item.Name?.StartsWith("FrameRow", StringComparison.Ordinal) == true);
        if (row is null || !int.TryParse(row.Name!["FrameRow".Length..], out var index) || index < 0 || index >= storyboard.Children.Count)
            return null;
        return ReferenceEquals(storyboard.Children[index], row) ? index : null;
    }

    private void CopySelectedFrames()
    {
        if (SelectedFrameTarget() is { } target) CopyFrame(editor.Scene.Frames.IndexOf(target));
    }

    private bool CopyFocusedFrames(object? source)
    {
        if (FocusedFrameIndex(source) is null) return false;
        CopySelectedFrames(); return true;
    }

    private void CopyFrame(int? index = null)
    {
        FinishPath();
        _ = MarkInternalClipboard();
        var selected = index ?? editor.FrameIndex;
        if (selected < 0 || selected >= editor.Scene.Frames.Count) return;
        var targets = FrameTargets(editor.Scene.Frames[selected]);
        clipboardFrames = targets.Select(frame => new FrameClipboard(editor.Scene, editor.Scene.Frames.IndexOf(frame))).ToList();
        clipboardFrame = clipboardFrames.FirstOrDefault(); clipboardArtwork = null;
    }

    private void PasteFrame(int? afterIndex = null)
    {
        if (clipboardFrame is null) return;
        FinishPath(); editor.BeforeChange();
        var insertion = Math.Clamp((afterIndex ?? editor.FrameIndex) + 1, 0, editor.Scene.Frames.Count);
        var first = insertion; var paletteChanged = false;
        var copies = clipboardFrames is { Count: > 0 } ? clipboardFrames : [clipboardFrame];
        var insertedIds = new List<string>();
        foreach (var copied in copies)
        {
            var frame = copied.Paste(editor.Scene, out var changed); paletteChanged |= changed;
            editor.Scene.Frames.Insert(insertion++, frame); insertedIds.Add(frame.Id);
        }
        editor.SelectFrame(first); selectionPrimaryFrame = editor.Frame.Id;
        selectedFrameIds.Clear(); selectedFrameIds.UnionWith(insertedIds); frameAnchor = editor.Frame.Id;
        if (paletteChanged) editor.RememberPalette();
        RefreshAll(); RefreshTools();
    }

    private async void PasteClipboard()
    {
        if (await TryPasteExternalImage()) return;
        if (clipboardFrame is not null) PasteFrame();
        else PasteSelection();
    }
}
