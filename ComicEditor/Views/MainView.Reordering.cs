using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ComicEditor.Views;

public partial class MainView
{
    private IPointer? listDragPointer;
    private Border? listDragSource, listDropMarker;
    private StackPanel? listDragRows;
    private bool listDragFrame, listDragActive;
    private int listDragIndex, listDropInsertion;
    private Point listDragStart;
    private IBrush? markerBrush;
    private Thickness markerThickness;

    private void AttachReorder(Border row, StackPanel rows, int index, bool frame, Action activate)
    {
        row.PointerPressed += (_, e) =>
        {
            if (listDragPointer is not null || e.Pointer.Type == PointerType.Touch ||
                !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            listDragPointer = e.Pointer; listDragSource = row; listDragRows = rows;
            listDragIndex = index; listDragFrame = frame; listDragActive = false;
            listDragStart = e.GetPosition(rows);
            e.Pointer.Capture(row);
            if (e.Pointer.Type == PointerType.Pen) e.PreventGestureRecognition();
        };
        row.PointerMoved += (_, e) =>
        {
            if (e.Pointer != listDragPointer || listDragRows != rows) return;
            if (!listDragActive && ((Vector)(e.GetPosition(rows) - listDragStart)).Length < 5) return;
            listDragActive = true;
            var scroll = rows.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null)
            {
                var y = e.GetPosition(scroll).Y;
                if (y < 24) scroll.Offset -= new Vector(0, 16);
                else if (y > scroll.Bounds.Height - 24) scroll.Offset += new Vector(0, 16);
            }
            var items = rows.Children.OfType<Border>().ToArray();
            var position = e.GetPosition(rows).Y;
            var insertion = 0;
            while (insertion < items.Length && position >= items[insertion].Bounds.Center.Y) insertion++;
            ShowListDrop(items, insertion);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (e.Pointer != listDragPointer || listDragRows != rows) return;
            var moved = listDragActive; var insertion = listDropInsertion;
            var from = listDragIndex; var wasFrame = listDragFrame;
            ClearListDrag(); e.Pointer.Capture(null);
            if (moved)
            {
                var count = wasFrame ? editor.Scene.Frames.Count : editor.Frame.Layers.Count;
                var visualFrom = wasFrame ? from : count - 1 - from;
                var visualTarget = insertion > visualFrom ? insertion - 1 : insertion;
                var target = wasFrame ? visualTarget : count - 1 - visualTarget;
                if (wasFrame) editor.ReorderFrame(from, target);
                else
                {
                    editor.ReorderLayer(from, target);
                    if (editor.Tool == Editing.Tool.Text) editor.Tool = Editing.Tool.Pixel;
                }
                if (target == from) activate();
                RefreshAll(); RefreshTools();
            }
            else activate();
            e.Handled = true;
        };
        row.PointerCaptureLost += (_, e) => { if (e.Pointer == listDragPointer) ClearListDrag(); };
        row.Tapped += (_, e) => { if (e.Pointer.Type == PointerType.Touch) activate(); };
        row.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; }
        };
    }

    private void ShowListDrop(Border[] rows, int insertion)
    {
        RestoreListMarker();
        listDropInsertion = insertion;
        if (rows.Length == 0) return;
        listDragSource!.Opacity = .55;
        var marker = rows[Math.Min(insertion, rows.Length - 1)];
        listDropMarker = marker; markerBrush = marker.BorderBrush; markerThickness = marker.BorderThickness;
        marker.BorderBrush = Brush(UiTheme.Accent);
        marker.BorderThickness = insertion == rows.Length ? new Thickness(0, 0, 0, 3) : new Thickness(0, 3, 0, 0);
    }

    private void RestoreListMarker()
    {
        if (listDropMarker is null) return;
        listDropMarker.BorderBrush = markerBrush;
        listDropMarker.BorderThickness = markerThickness;
        listDropMarker = null;
    }

    private void ClearListDrag()
    {
        RestoreListMarker();
        if (listDragSource is not null) listDragSource.Opacity = 1;
        listDragPointer = null; listDragSource = null; listDragRows = null; listDragActive = false;
    }
}
