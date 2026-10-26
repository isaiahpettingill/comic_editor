using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private readonly List<Border> paneHosts = [];
    private Canvas? dockingOverlay;
    private IPointer? dockingPointer;
    private int dockingTarget = -1;
    private bool paneDragActive;

    private void RestorePaneLayout()
    {
        var p = editor.Preferences;
        if (p.PaneLayout is { Length: 4 } order && order.Distinct().Count() == 4 && order.All(x => Enum.TryParse<Pane>(x, out var pane) && Enum.IsDefined(pane)))
            for (var i = 0; i < 4; i++) paneOrder[i] = Enum.Parse<Pane>(order[i]);
        if (p.PaneWidths is { Length: 3 } widths && widths.All(x => double.IsFinite(x) && x >= 0))
            for (var i = 0; i < 3; i++) paneWidths[i] = widths[i] == 0 ? new(1, GridUnitType.Star) : new(Math.Clamp(widths[i], 180, 1200));
        if (double.IsFinite(p.BottomPaneHeight)) paletteHeight = Math.Clamp(p.BottomPaneHeight, 90, 600);
    }

    private void PersistPaneLayout()
    {
        if (desktopWorkspace is not null && !compact)
            for (var i = 0; i < 3; i++) paneWidths[i] = desktopWorkspace.ColumnDefinitions[i * 2].Width;
        if (rootGrid is not null && !compact) paletteHeight = Math.Max(90, rootGrid.RowDefinitions[4].ActualHeight);
        var p = editor.Preferences;
        p.PaneLayout = paneOrder.Select(x => x.ToString()).ToArray();
        p.PaneWidths = paneWidths.Select(x => x.IsStar ? 0 : x.Value).ToArray();
        p.BottomPaneHeight = paletteHeight; p.Save();
    }

    private Border PaneHeader(Pane pane, string caption)
    {
        var header = new Border
        {
            Name = pane + "Header", Background = Brush(UiTheme.Header), Padding = new Thickness(8, 5),
            Child = compact ? Label(caption, true) : Row(new PackIconMaterial { Kind = PackIconMaterialKind.Drag, Width = 16, Height = 16 }, Label(caption, true)),
            Cursor = new Cursor(compact ? StandardCursorType.Arrow : StandardCursorType.SizeAll), Focusable = !compact
        };
        if (!compact)
        {
            ToolTip.SetTip(header, "Drag to a highlighted dock area; Escape cancels. Right-click to move without dragging.");
            Avalonia.Automation.AutomationProperties.SetName(header, $"Move {caption} pane");
            var menu = new ContextMenu();
            foreach (var destination in Enumerable.Range(0, 4))
            {
                var item = new MenuItem { Header = "Move to " + new[] { "left", "center", "right", "bottom" }[destination] };
                item.Click += (_, _) => MovePane(pane, destination); menu.Items.Add(item);
            }
            header.ContextMenu = menu;
        }
        header.PointerPressed += (_, e) =>
        {
            if (compact || shell is null || !e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
            draggingPane = pane; paneDragStart = e.GetPosition(shell); dockingPointer = e.Pointer; e.Pointer.Capture(header);
        };
        header.PointerMoved += (_, e) =>
        {
            if (draggingPane != pane || shell is null || e.Pointer != dockingPointer) return;
            var point = e.GetPosition(shell);
            if (!paneDragActive && ((Vector)(point - paneDragStart)).Length < 8) return;
            paneDragActive = true; ShowDockingTargets(point); e.Handled = true;
        };
        header.PointerReleased += (_, e) =>
        {
            if (e.Pointer != dockingPointer) return;
            var source = draggingPane; var target = dockingTarget; var active = paneDragActive;
            CancelPaneDrag(); e.Handled = active;
            if (active && source is Pane from && target >= 0) MovePane(from, target);
        };
        header.PointerCaptureLost += (_, _) => CancelPaneDrag();
        header.KeyDown += (_, e) => { if (e.Key == Key.Escape) { CancelPaneDrag(); e.Handled = true; } };
        return header;
    }

    private void ShowDockingTargets(Point point)
    {
        if (shell is null) return;
        if (dockingOverlay is null)
        {
            dockingOverlay = new Canvas { Name = "DockingTargets", IsHitTestVisible = false };
            shell.Children.Add(dockingOverlay);
        }
        dockingOverlay.Children.Clear(); dockingTarget = -1;
        for (var i = 0; i < paneHosts.Count; i++)
        {
            var host = paneHosts[i]; var origin = host.TranslatePoint(default, shell);
            if (origin is not Point start) continue;
            var rect = new Rect(start, host.Bounds.Size).Deflate(5);
            var selected = rect.Contains(point);
            if (selected) dockingTarget = i;
            var target = new Border
            {
                Name = $"DockTarget{i}", Width = rect.Width, Height = rect.Height,
                Background = Brush(selected ? UiTheme.Selection : UiTheme.Surface), Opacity = selected ? .85 : .65,
                BorderBrush = Brush(UiTheme.Accent), BorderThickness = new Thickness(selected ? 3 : 1),
                Child = new TextBlock { Text = (selected ? "Release to swap · " : "Dock · ") + new[] { "Left", "Center", "Right", "Bottom" }[i],
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Canvas.SetLeft(target, rect.X); Canvas.SetTop(target, rect.Y); dockingOverlay.Children.Add(target);
        }
    }

    private void CancelPaneDrag()
    {
        draggingPane = null; dockingTarget = -1; paneDragActive = false;
        if (dockingOverlay is not null) shell?.Children.Remove(dockingOverlay);
        dockingOverlay = null;
        var pointer = dockingPointer; dockingPointer = null; pointer?.Capture(null);
    }

    private void MovePane(Pane pane, int destination)
    {
        var source = Array.IndexOf(paneOrder, pane);
        if (source == destination || destination is < 0 or > 3) return;
        (paneOrder[source], paneOrder[destination]) = (paneOrder[destination], paneOrder[source]);
        PersistPaneLayout();
        // A bottom storyboard is a horizontal filmstrip, not a clipped vertical list.
        if (paneOrder[3] is Pane.Storyboard or Pane.Canvas) paletteHeight = Math.Max(240, paletteHeight);
        editor.Preferences.BottomPaneHeight = paletteHeight; editor.Preferences.Save();
        rootGrid = null; Build(false);
    }
}
