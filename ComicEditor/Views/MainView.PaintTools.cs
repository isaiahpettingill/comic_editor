using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ComicEditor.Editing;
using ComicEditor.Format;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private sealed record ToolGroup(string Id, Tool[] Tools);
    private static readonly ToolGroup[] ToolGroups =
    [
        new("Brush", [Tool.Pixel, Tool.Smooth, Tool.Pressure, Tool.Marker]),
        new("Eraser", [Tool.Eraser]),
        new("Fill", [Tool.Fill]),
        new("Line", [Tool.Line, Tool.Curve]),
        new("Shapes", [Tool.Rectangle, Tool.RoundedRectangle, Tool.Ellipse, Tool.Polygon]),
        new("Eyedropper", [Tool.Eyedropper]),
        new("Text", [Tool.Text]),
        new("Spray", [Tool.Spray, Tool.Dither]),
        new("Selection", [Tool.Select, Tool.Lasso, Tool.EllipseSelect]),
        new("Effects", [Tool.Scramble, Tool.Blur]),
        new("Zoom", [Tool.Zoom])
    ];
    private Tool RememberedTool(ToolGroup group) => group.Tools.Contains(editor.Tool) ? editor.Tool :
        editor.Preferences.LastToolInGroup.TryGetValue(group.Id, out var last) && group.Tools.Contains(last) ? last : group.Tools[0];

    private Button GroupButton(ToolGroup group)
    {
        var chosen = RememberedTool(group);
        var suppressClick = false;
        var button = Icon(ToolIcon(chosen), $"{ToolName(chosen)} — {ToolHelp(chosen)}" + (group.Tools.Length > 1 ? " Hold or right-click to choose another tool." : ""), () =>
        {
            if (suppressClick) { suppressClick = false; return; }
            ChooseTool(RememberedTool(group));
        });
        button.Name = "ToolGroup" + group.Id; button.Width = button.Height = compact ? 40 : 34;
        if (group.Tools.Length > 1)
        {
            button.Content = new Grid
            {
                Width = 26,
                Height = 26,
                Children =
                {
                    new PackIconMaterial { Kind = ToolIcon(chosen), Width = 19, Height = 19, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top },
                    new PackIconMaterial { Kind = PackIconMaterialKind.MenuDown, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }
                }
            };
            var menu = new ContextMenu();
            foreach (var tool in group.Tools)
            {
                var item = new MenuItem { Header = Row(new PackIconMaterial { Kind = ToolIcon(tool), Width = 18, Height = 18 }, Label(ToolName(tool))) };
                item.Click += (_, _) => ChooseTool(tool);
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            var held = false;
            Point pressPoint = default;
            hold.Tick += (_, _) =>
            {
                hold.Stop(); held = true; suppressClick = true;
                menu.Open(button);
            };
            button.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.Pointer.Type is not (PointerType.Pen or PointerType.Touch) || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                e.PreventGestureRecognition();
                held = false; suppressClick = false;
                pressPoint = e.GetPosition(button);
                hold.Start();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(PointerMovedEvent, (_, e) =>
            {
                var point = e.GetPosition(button);
                if (hold.IsEnabled && (Math.Abs(point.X - pressPoint.X) > 10 || Math.Abs(point.Y - pressPoint.Y) > 10)) hold.Stop();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(PointerReleasedEvent, (_, _) =>
            {
                hold.Stop();
                if (!held) return;
                held = false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!menu.IsOpen) menu.Open(button);
                }, DispatcherPriority.Background);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        if (group.Tools.Contains(editor.Tool)) { button.Background = Brush(UiTheme.Selection); button.BorderBrush = Brush(UiTheme.Accent); }
        return button;
    }

    private ArtworkSelection? selection;
    private ArtworkClipboard? clipboardArtwork;
    private FrameClipboard? clipboardFrame;
    private List<string>? selectionBase;
    private bool movingSelection;
    private int selectionX, selectionY;
    private readonly List<(double X, double Y)> lasso = [];
    private List<string>? pathBase;
    private ArtworkLayer? pathLayer;
    private Tool pathTool;
    private readonly List<(double X, double Y)> path = [];
    private int curveStage;
    private (double X, double Y) curveEnd, curveControl1, curveControl2;
    private StrokeSmoother? smoother;
    private Avalonia.Threading.DispatcherTimer? sprayTimer;

    private static PackIconMaterialKind ToolIcon(Tool tool) => tool switch
    {
        Tool.Pixel => PackIconMaterialKind.Pencil,
        Tool.Smooth => PackIconMaterialKind.Brush,
        Tool.Pressure => PackIconMaterialKind.Draw,
        Tool.Eraser => PackIconMaterialKind.Eraser,
        Tool.Fill => PackIconMaterialKind.FormatColorFill,
        Tool.Line => PackIconMaterialKind.VectorLine,
        Tool.Rectangle => PackIconMaterialKind.RectangleOutline,
        Tool.Ellipse => PackIconMaterialKind.EllipseOutline,
        Tool.Eyedropper => PackIconMaterialKind.Eyedropper,
        Tool.Text => PackIconMaterialKind.FormatText,
        Tool.Spray => PackIconMaterialKind.Spray,
        Tool.Marker => PackIconMaterialKind.Marker,
        Tool.Dither => PackIconMaterialKind.Checkerboard,
        Tool.Scramble => PackIconMaterialKind.ShuffleVariant,
        Tool.Blur => PackIconMaterialKind.Blur,
        Tool.Select => PackIconMaterialKind.Selection,
        Tool.Lasso => PackIconMaterialKind.Lasso,
        Tool.EllipseSelect => PackIconMaterialKind.EllipseOutline,
        Tool.Curve => PackIconMaterialKind.VectorCurve,
        Tool.Polygon => PackIconMaterialKind.VectorPolygon,
        Tool.RoundedRectangle => PackIconMaterialKind.SquareRoundedOutline,
        _ => PackIconMaterialKind.Magnify
    };
    private static string ToolName(Tool tool) => tool switch
    { Tool.Select => "Select rectangle", Tool.Lasso => "Freehand select", Tool.EllipseSelect => "Select ellipse", Tool.RoundedRectangle => "Rounded rectangle", Tool.Spray => "Spray can", Tool.Scramble => "Pixel scramble", _ => tool.ToString() };
    private static bool UsesSize(Tool tool) => tool is not (Tool.Select or Tool.Lasso or Tool.EllipseSelect or Tool.Text or Tool.Fill or Tool.Eyedropper or Tool.Zoom);
    private static string ToolHelp(Tool tool) => tool switch
    {
        Tool.Curve => "Drag the end points, then drag two bends. Finish accepts early; Escape cancels.",
        Tool.Polygon => "Click corners. Double-click, Enter, or Finish closes the polygon. Escape cancels.",
        Tool.Select or Tool.Lasso or Tool.EllipseSelect => "Drag to select artwork on the current layer, then drag inside to move it. Use Edit to copy, cut, paste, or delete.",
        Tool.Spray => "Hold to spray; move to cover an area. Diameter and density are adjustable.",
        Tool.Marker => "Draw with translucent color. Size and opacity are adjustable.",
        Tool.Dither => "Paint with a repeating 4×4 Bayer pattern. Adjust density in tool options.",
        Tool.Scramble => "Shuffle nearby pixels while dragging without adding color.",
        Tool.Blur => "Soften nearby pixels while dragging. Works in RGBA and indexed color modes.",
        Tool.Zoom => "Click to zoom in; right-click or Shift-click to zoom out. Scroll or pinch with two fingers to zoom. Middle-drag or drag three fingers to pan.",
        Tool.Text => "Drag to create a text area; click existing text to select it.",
        _ => "Drag to draw on the current artwork layer."
    };
    private void ChooseTool(Tool tool)
    {
        EndInlineTextEdit(); FinishPath(); StopSpray(); selection = null;
        if (tool == Tool.Marker && !editor.Scene.IsRgba) EnableRgbaMode();
        var group = ToolGroups.FirstOrDefault(g => g.Tools.Contains(tool));
        if (group is not null) editor.Preferences.LastToolInGroup[group.Id] = tool;
        var changed = editor.Tool != tool;
        editor.SelectedTextId = null; editor.Tool = tool; RefreshTools(); RefreshInspector(); RefreshCanvas();
        if (!changed) editor.Preferences.Save();
    }

    private void EditToolOptions()
    {
        FinishPath();
        var settings = editor.Paint;
        var body = new StackPanel { Spacing = 10, Children = { Label(ToolHelp(editor.Tool)) } };
        var size = new NumericUpDown { Name = "ToolSize", Value = settings.Size, Minimum = 1, Maximum = 64, Height = 44 };
        if (UsesSize(editor.Tool)) { body.Children.Add(Label(editor.Tool == Tool.Spray ? "Spray diameter (px)" : "Size (px)")); body.Children.Add(size); }
        var tip = new ComboBox { Name = "BrushTip", ItemsSource = Enum.GetValues<BrushTip>(), SelectedItem = settings.Tip, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (editor.Tool is Tool.Pixel or Tool.Smooth or Tool.Pressure or Tool.Eraser or Tool.Marker)
        { body.Children.Add(Label("Brush shape")); body.Children.Add(tip); }
        var fill = new ComboBox { Name = "ShapeFill", ItemsSource = Enum.GetValues<ShapeFill>(), SelectedItem = settings.Fill, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (editor.Tool is Tool.Rectangle or Tool.Ellipse or Tool.RoundedRectangle or Tool.Polygon)
        { body.Children.Add(Label("Shape appearance")); body.Children.Add(fill); }
        var density = new NumericUpDown { Name = "SprayDensity", Value = settings.SprayDensity, Minimum = 1, Maximum = 100, Height = 44 };
        if (editor.Tool == Tool.Spray) { body.Children.Add(Label("Spray density")); body.Children.Add(density); }
        var opacity = new NumericUpDown { Name = "ToolOpacity", Value = settings.Opacity, Minimum = 0, Maximum = 255, Height = 44 };
        if (editor.Scene.IsRgba && editor.Tool is not (Tool.Select or Tool.Lasso or Tool.EllipseSelect or Tool.Text or Tool.Eyedropper or Tool.Zoom or Tool.Scramble or Tool.Blur or Tool.Eraser))
        { body.Children.Add(Label("Opacity (0–255)")); body.Children.Add(opacity); }
        var dither = new NumericUpDown { Name = "DitherDensity", Value = settings.DitherDensity, Minimum = 1, Maximum = 16, Height = 44 };
        if (editor.Tool == Tool.Dither) { body.Children.Add(Label("Pattern density (1–16)")); body.Children.Add(dither); }
        var smooth = new CheckBox { Content = "Smooth mouse / touchpad strokes", IsChecked = editor.Preferences.SmoothMouse };
        body.Children.Add(smooth);
        ShowModal(ToolName(editor.Tool) + " options", body, () =>
        {
            if (size.Value is null || density.Value is null || opacity.Value is null || dither.Value is null) return;
            settings.Size = (int)size.Value; settings.Tip = (BrushTip)tip.SelectedItem!; settings.Fill = (ShapeFill)fill.SelectedItem!;
            settings.SprayDensity = (int)density.Value; settings.Opacity = (int)opacity.Value; settings.DitherDensity = (int)dither.Value; editor.Preferences.SmoothMouse = smooth.IsChecked == true;
            editor.Preferences.Save(); CloseModal(); RefreshTools();
        });
    }

    private void StartSelection(int x, int y)
    {
        movingSelection = selection?.Owner == editor.Layer && selection.Contains(x, y);
        selectionBase = null; lasso.Clear();
        if (movingSelection) { selectionX = selection!.X; selectionY = selection.Y; }
        else { selection = null; lasso.Add((x, y)); }
    }
    private void MoveSelection(int x, int y)
    {
        if (movingSelection && selection is not null)
        {
            if (selectionBase is null)
            {
                if (x == startX && y == startY) return;
                editor.BeforeChange(); selection.Clear(); selectionBase = editor.Layer.Rows.ToList();
            }
            editor.Layer.Rows = selectionBase.ToList();
            selection.X = Math.Clamp(selectionX + x - startX, 0, Math.Max(0, editor.Scene.Width - selection.Width));
            selection.Y = Math.Clamp(selectionY + y - startY, 0, Math.Max(0, editor.Scene.Height - selection.Height));
            selection.Paste();
        }
        else
        {
            if (editor.Tool == Tool.Lasso && (lasso.Count == 0 || lasso[^1] != (x, y))) lasso.Add((x, y));
            if (canvas is not null) canvas.SelectionOutline = editor.Tool == Tool.Lasso ? lasso.Select(p => new Point(p.X, p.Y)).ToArray() :
                editor.Tool == Tool.EllipseSelect ? Enumerable.Range(0, 48).Select(i =>
                {
                    var angle = i * Math.PI * 2 / 48;
                    return new Point((startX + x) / 2.0 + Math.Cos(angle) * Math.Abs(x - startX) / 2,
                        (startY + y) / 2.0 + Math.Sin(angle) * Math.Abs(y - startY) / 2);
                }).ToArray() : [new(startX, startY), new(x, startY), new(x, y), new(startX, y)];
        }
    }
    private void EndSelection(int x, int y)
    {
        if (!movingSelection)
        {
            var left = Math.Min(startX, x); var top = Math.Min(startY, y); var right = Math.Max(startX, x); var bottom = Math.Max(startY, y);
            if (editor.Tool == Tool.Lasso && lasso.Count > 2)
            { left = (int)lasso.Min(p => p.X); right = (int)lasso.Max(p => p.X); top = (int)lasso.Min(p => p.Y); bottom = (int)lasso.Max(p => p.Y); }
            if ((editor.Tool is Tool.Select or Tool.EllipseSelect || lasso.Count > 2) && right > left && bottom > top)
                selection = new ArtworkSelection(editor.Layer, left, top, right - left + 1, bottom - top + 1, editor.Tool == Tool.Lasso ? lasso : null, editor.Tool == Tool.EllipseSelect);
        }
        movingSelection = false; selectionBase = null; lasso.Clear();
        if (canvas is not null) canvas.SelectionOutline = null;
    }
    private void CopySelection(bool cut)
    {
        FinishPath();
        if (selection?.Owner != editor.Layer) return;
        clipboardArtwork = new ArtworkClipboard(selection, editor.Scene); clipboardFrame = null;
        if (cut) DeleteSelection();
    }
    private void DeleteSelection()
    {
        if (selection?.Owner != editor.Layer) return;
        editor.BeforeChange(); selection.Clear(); selection = null; RefreshAll();
    }
    private void PasteSelection()
    {
        if (clipboardArtwork is null) return;
        var x = Math.Clamp(clipboardArtwork.X + 4, 0, Math.Max(0, editor.Scene.Width - clipboardArtwork.Width));
        var y = Math.Clamp(clipboardArtwork.Y + 4, 0, Math.Max(0, editor.Scene.Height - clipboardArtwork.Height));
        PasteSelectionAt(x, y);
    }
    private void PasteSelectionAt(int x, int y)
    {
        if (clipboardArtwork is null) return;
        FinishPath(); editor.BeforeChange(); editor.Tool = Tool.Select;
        selection = clipboardArtwork.Paste(editor.Layer, editor.Scene, x, y, out var paletteChanged);
        if (paletteChanged) editor.RememberPalette();
        RefreshAll(); RefreshTools();
    }
    private void SelectAllArtwork()
    {
        FinishPath(); editor.Tool = Tool.Select;
        selection = new ArtworkSelection(editor.Layer, 0, 0, editor.Scene.Width, editor.Scene.Height);
        RefreshCanvas(); RefreshTools();
    }
    private void Deselect() { FinishPath(); selection = null; RefreshCanvas(); RefreshTools(); }

    private void StartPath(int x, int y, int clicks)
    {
        if (pathBase is null)
        {
            pathBase = editor.Layer.Rows.ToList(); pathLayer = editor.Layer; pathTool = editor.Tool;
            path.Clear(); path.Add((x, y)); curveStage = 0; curveEnd = curveControl1 = curveControl2 = (x, y);
        }
        else if (editor.Tool == Tool.Polygon)
        {
            if (path[^1] != (x, y)) path.Add((x, y));
            DrawPath(x, y);
            if (clicks > 1) FinishPath();
        }
        RefreshTools();
    }
    private void DrawPath(int x, int y)
    {
        if (pathBase is null || pathLayer != editor.Layer) return;
        editor.Layer.Rows = pathBase.ToList();
        if (pathTool == Tool.Polygon)
        {
            var preview = path.ToList(); if (preview[^1] != (x, y)) preview.Add((x, y));
            PaintRaster.Polygon(editor.Layer, preview, editor.Color, editor.BrushSize, editor.Paint.Fill, palette: editor.Scene.Palette, opacity: editor.Paint.Opacity);
        }
        else
        {
            if (curveStage == 0) { curveEnd = (x, y); curveControl1 = path[0]; curveControl2 = curveEnd; }
            else if (curveStage == 1) curveControl1 = curveControl2 = (x, y);
            else curveControl2 = (x, y);
            PaintRaster.Curve(editor.Layer, path[0], curveEnd, curveControl1, curveControl2, editor.Color, editor.BrushSize, editor.Scene.Palette, editor.Paint.Opacity);
        }
    }
    private void FinishPath(bool cancel = false)
    {
        if (pathBase is null || pathLayer is null) return;
        if (!cancel && pathTool == Tool.Polygon)
        { pathLayer.Rows = pathBase.ToList(); PaintRaster.Polygon(pathLayer, path, editor.Color, editor.BrushSize, editor.Paint.Fill, palette: editor.Scene.Palette, opacity: editor.Paint.Opacity); }
        var owner = pathLayer; var original = pathBase;
        var result = owner.Rows.ToList(); owner.Rows = original;
        pathBase = null; pathLayer = null; path.Clear();
        if (!cancel && !result.SequenceEqual(original)) { editor.BeforeChange(); owner.Rows = result; }
        RefreshAll(); RefreshTools();
    }
    private void StopSpray() { sprayTimer?.Stop(); sprayTimer = null; }
    private void SprayAt(int x, int y) => PaintRaster.Spray(editor.Layer, x, y, editor.Color, editor.BrushSize, editor.Paint.SprayDensity, Random.Shared, editor.Scene.Palette, editor.Paint.Opacity);
    private void StartSpray()
    {
        StopSpray(); SprayAt(startX, startY);
        sprayTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        sprayTimer.Tick += (_, _) => { if (!dragging || editor.Tool != Tool.Spray) { StopSpray(); return; } SprayAt(lastX, lastY); RefreshCanvas(); };
        sprayTimer.Start();
    }
}
