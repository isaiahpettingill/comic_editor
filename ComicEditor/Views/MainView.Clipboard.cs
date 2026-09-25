using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ComicEditor.Editing;

namespace ComicEditor.Views;

public partial class MainView
{
    private MenuItem? canvasCopyItem, canvasCutItem, canvasPasteItem;
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
        canvas.ContextMenu = new ContextMenu { Items = { canvasCopyItem, canvasCutItem, canvasPasteItem } };
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

    private void CopyFrame(int? index = null)
    {
        FinishPath();
        var selected = index ?? editor.FrameIndex;
        if (selected < 0 || selected >= editor.Scene.Frames.Count) return;
        clipboardFrame = new FrameClipboard(editor.Scene, selected); clipboardArtwork = null;
    }

    private void PasteFrame(int? afterIndex = null)
    {
        if (clipboardFrame is null) return;
        FinishPath(); editor.BeforeChange();
        var copy = clipboardFrame.Paste(editor.Scene, out var paletteChanged);
        var insertion = Math.Clamp((afterIndex ?? editor.FrameIndex) + 1, 0, editor.Scene.Frames.Count);
        editor.Scene.Frames.Insert(insertion, copy); editor.SelectFrame(insertion);
        if (paletteChanged) editor.RememberPalette();
        RefreshAll(); RefreshTools();
    }

    private void PasteClipboard()
    {
        if (clipboardFrame is not null) PasteFrame();
        else PasteSelection();
    }
}
