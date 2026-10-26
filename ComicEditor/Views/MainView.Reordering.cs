using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
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
    private KeyModifiers listSelectModifiers;
    private IBrush? markerBrush;
    private Thickness markerThickness;

    private void AttachReorder(Border row, StackPanel rows, int index, bool frame, Action activate)
    {
        row.PointerPressed += (_, e) =>
        {
            if (e.Source is Avalonia.Visual visual && (visual is Avalonia.Controls.Button or TextBox or NumericUpDown || visual.GetVisualAncestors().Any(v => v is Avalonia.Controls.Button or TextBox or NumericUpDown))) return;
            if (listDragPointer is not null || e.Pointer.Type == PointerType.Touch ||
                !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            row.Focus();
            listSelectModifiers = e.KeyModifiers;
            listDragPointer = e.Pointer; listDragSource = row; listDragRows = rows;
            listDragIndex = index; listDragFrame = frame; listDragActive = false;
            listDragStart = e.GetPosition(rows);
            e.Pointer.Capture(row);
            if (e.Pointer.Type == PointerType.Pen) e.PreventGestureRecognition();
        };
        row.PointerMoved += (_, e) =>
        {
            if (e.Pointer != listDragPointer || listDragRows != rows) return;
            if ((listSelectModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0) return;
            if (!listDragActive && ((Vector)(e.GetPosition(rows) - listDragStart)).Length < 5) return;
            listDragActive = true;
            var horizontal = rows.Orientation == Orientation.Horizontal;
            var scroll = rows.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null)
            {
                var point = e.GetPosition(scroll); var edge = horizontal ? point.X : point.Y;
                var extent = horizontal ? scroll.Bounds.Width : scroll.Bounds.Height;
                var step = horizontal ? new Vector(16, 0) : new Vector(0, 16);
                if (edge < 24) scroll.Offset -= step;
                else if (edge > extent - 24) scroll.Offset += step;
            }
            var items = rows.Children.OfType<Border>().ToArray();
            var position = horizontal ? e.GetPosition(rows).X : e.GetPosition(rows).Y;
            var insertion = 0;
            while (insertion < items.Length && position >= (horizontal ? items[insertion].Bounds.Center.X : items[insertion].Bounds.Center.Y)) insertion++;
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
                ReorderSelectedItems(from, insertion, wasFrame);
                RefreshAll(); RefreshTools();
            }
            else SelectListItem(index, frame, listSelectModifiers, activate);
            e.Handled = true;
        };
        row.PointerCaptureLost += (_, e) => { if (e.Pointer == listDragPointer) ClearListDrag(); };
        row.Tapped += (_, e) => { if (e.Source is Visual v && (v is TextBox or Avalonia.Controls.Button || v.GetVisualAncestors().Any(a => a is TextBox or Avalonia.Controls.Button))) return; if (e.Pointer.Type == PointerType.Touch) SelectListItem(index, frame, e.KeyModifiers, activate); };
        row.KeyDown += (_, e) =>
        {
            if (e.Source != row) return;
            if (e.Key is Key.Enter or Key.Space) { SelectListItem(index, frame, e.KeyModifiers, activate); e.Handled = true; }
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
        marker.BorderThickness = listDragRows?.Orientation == Orientation.Horizontal
            ? insertion == rows.Length ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0)
            : insertion == rows.Length ? new Thickness(0, 0, 0, 3) : new Thickness(0, 3, 0, 0);
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
