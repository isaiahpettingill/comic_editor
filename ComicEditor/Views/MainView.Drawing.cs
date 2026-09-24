using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Views;

public partial class MainView
{
    private bool dragging;
    private bool textMoveUndo;
    private bool creatingText;
    private int textHandle = -1;
    private Avalonia.Rect originalTextBounds;
    private int startX, startY, lastX, lastY;
    private List<string>? shapeStart;

    private void CanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (canvas is null) return;
        var properties = e.GetCurrentPoint(canvas).Properties;
        if (editor.Tool == Tool.Zoom && (properties.IsLeftButtonPressed || properties.IsRightButtonPressed))
        {
            var current = zoom > 0 ? zoom : canvas.TranslatePoint(new Point(1, 0), this)!.Value.X - canvas.TranslatePoint(default, this)!.Value.X;
            zoom = Math.Clamp(current * (properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .5 : 2), .1, 32);
            RefreshCanvas(); RefreshTools(); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed) return;
        canvas.Focus(); var point = canvas.CanvasPoint(e);
        startX = lastX = (int)point.X; startY = lastY = (int)point.Y;
        if (editor.Tool is Tool.Select or Tool.Lasso)
        {
            StartSelection(startX, startY); dragging = true; e.Pointer.Capture(canvas); RefreshCanvas(); return;
        }
        if (editor.Tool is Tool.Curve or Tool.Polygon)
        {
            StartPath(startX, startY, e.ClickCount); dragging = pathBase is not null;
            if (dragging) e.Pointer.Capture(canvas); RefreshCanvas(); return;
        }
        if (editor.Tool == Tool.Eyedropper)
        {
            var sampled = editor.Frame.Layers.Where(l => l.Visible).Reverse().Select(l => l.Pixel(startX, startY)).FirstOrDefault(v => v >= 0, -1);
            if (sampled >= 0) { editor.Color = sampled; RefreshPalette(); }
            return;
        }
        if (editor.Tool == Tool.Text)
        {
            textHandle = -1;
            var selected = editor.SelectedText;
            if (selected is not null)
            {
                var scale = Math.Abs((canvas.TranslatePoint(new Avalonia.Point(1, 0), this)?.X ?? 1) - (canvas.TranslatePoint(default, this)?.X ?? 0));
                var tolerance = 7 / Math.Max(.1, scale);
                textHandle = Array.FindIndex(CutsceneCanvas.TextHandles(selected), p => Math.Abs(p.X - point.X) <= tolerance && Math.Abs(p.Y - point.Y) <= tolerance);
                originalTextBounds = new Avalonia.Rect(selected.X, selected.Y, selected.Width, selected.Height);
            }
            var hit = textHandle >= 0 ? selected : editor.Frame.TextObjects.LastOrDefault(t => startX >= t.X && startY >= t.Y && startX < t.X + t.Width && startY < t.Y + t.Height);
            textMoveUndo = false;
            creatingText = hit is null;
            editor.SelectedTextId = hit?.Id;
            dragging = true; e.Pointer.Capture(canvas); RefreshInspector(); RefreshCanvas(); return;
        }
        editor.BeforeChange(); dragging = true; e.Pointer.Capture(canvas);
        var color = editor.Tool == Tool.Eraser ? -1 : editor.Color;
        if (editor.Tool == Tool.Fill) { Raster.Fill(editor.Layer, startX, startY, color); dragging = false; e.Pointer.Capture(null); RefreshAll(); return; }
        if (editor.Tool is Tool.Line or Tool.Rectangle or Tool.Ellipse or Tool.RoundedRectangle) shapeStart = editor.Layer.Rows.ToList();
        else if (editor.Tool == Tool.Spray) StartSpray();
        else
        {
            smoother = editor.Tool == Tool.Smooth || e.Pointer.Type == PointerType.Mouse && editor.Preferences.SmoothMouse ? new StrokeSmoother(point) : null;
            PaintRaster.Stamp(editor.Layer, startX, startY, color, Diameter(e), editor.Paint.Tip);
        }
        RefreshCanvas();
    }

    private void CanvasMoved(object? sender, PointerEventArgs e)
    {
        if (canvas is null) return;
        if (!dragging)
        {
            if (pathBase is not null && pathTool == Tool.Polygon) { var hover = canvas.CanvasPoint(e); DrawPath((int)hover.X, (int)hover.Y); RefreshCanvas(); }
            return;
        }
        var point = canvas.CanvasPoint(e); var x = (int)point.X; var y = (int)point.Y;
        if (editor.Tool is Tool.Select or Tool.Lasso) { MoveSelection(x, y); RefreshCanvas(); return; }
        if (editor.Tool is Tool.Curve or Tool.Polygon) { DrawPath(x, y); RefreshCanvas(); return; }
        if (editor.Tool == Tool.Text)
        {
            if (creatingText)
            {
                canvas.DraftTextBounds = new Avalonia.Rect(Math.Min(startX, x), Math.Min(startY, y), Math.Abs(x - startX), Math.Abs(y - startY));
                canvas.InvalidateVisual(); return;
            }
            var obj = editor.SelectedText;
            if (obj is not null && (x != lastX || y != lastY))
            {
                if (!textMoveUndo) { editor.BeforeChange(); textMoveUndo = true; }
                if (textHandle >= 0)
                {
                    var left = originalTextBounds.Left; var right = originalTextBounds.Right;
                    var top = originalTextBounds.Top; var bottom = originalTextBounds.Bottom;
                    if (textHandle is 0 or 6 or 7) left = Math.Min(right - 4, left + x - startX);
                    if (textHandle is 2 or 3 or 4) right = Math.Max(left + 4, right + x - startX);
                    if (textHandle is 0 or 1 or 2) top = Math.Min(bottom - 4, top + y - startY);
                    if (textHandle is 4 or 5 or 6) bottom = Math.Max(top + 4, bottom + y - startY);
                    obj.X = left; obj.Y = top; obj.Width = right - left; obj.Height = bottom - top;
                }
                else { obj.X += x - lastX; obj.Y += y - lastY; }
            }
        }
        else if (shapeStart is not null)
        {
            editor.Layer.Rows = shapeStart.ToList();
            if (editor.Tool == Tool.Line) PaintRaster.Stroke(editor.Layer, startX, startY, x, y, editor.Color, editor.BrushSize, BrushTip.Round);
            else PaintRaster.Polygon(editor.Layer, PaintRaster.Shape(editor.Tool.ToString(), startX, startY, x, y), editor.Color, editor.BrushSize, editor.Paint.Fill);
        }
        else if (editor.Tool == Tool.Spray)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Pow(x - lastX, 2) + Math.Pow(y - lastY, 2)) / Math.Max(1, editor.BrushSize / 3.0)));
            for (var i = 1; i <= steps; i++) SprayAt(lastX + (x - lastX) * i / steps, lastY + (y - lastY) * i / steps);
        }
        else
        {
            var color = editor.Tool == Tool.Eraser ? -1 : editor.Color;
            if (smoother is not null) { var filtered = smoother.Add(point); x = (int)Math.Round(filtered.X); y = (int)Math.Round(filtered.Y); }
            PaintRaster.Stroke(editor.Layer, lastX, lastY, x, y, color, Diameter(e), editor.Paint.Tip);
        }
        lastX = x; lastY = y; RefreshCanvas();
    }

    private void CanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        var endpoint = canvas!.CanvasPoint(e);
        if (editor.Tool is Tool.Select or Tool.Lasso)
        {
            MoveSelection((int)endpoint.X, (int)endpoint.Y); EndSelection((int)endpoint.X, (int)endpoint.Y);
            dragging = false; e.Pointer.Capture(null); RefreshAll(); return;
        }
        if (editor.Tool is Tool.Curve or Tool.Polygon)
        {
            DrawPath((int)endpoint.X, (int)endpoint.Y);
            if (editor.Tool == Tool.Curve && ++curveStage >= 3) FinishPath();
            else if (editor.Tool == Tool.Polygon && path.Count == 1 && path[0] != (endpoint.X, endpoint.Y)) path.Add((endpoint.X, endpoint.Y));
            dragging = false; e.Pointer.Capture(null); RefreshCanvas(); RefreshTools(); return;
        }
        CanvasMoved(sender, e); dragging = false; shapeStart = null;
        StopSpray();
        if (smoother is not null)
        {
            PaintRaster.Stroke(editor.Layer, lastX, lastY, (int)endpoint.X, (int)endpoint.Y, editor.Tool == Tool.Eraser ? -1 : editor.Color, Diameter(e), editor.Paint.Tip);
            smoother = null;
        }
        if (creatingText && canvas?.DraftTextBounds is Avalonia.Rect bounds && bounds.Width >= 4 && bounds.Height >= 4)
        {
            editor.BeforeChange();
            var obj = editor.CreateText(); obj.X = bounds.X; obj.Y = bounds.Y; obj.Width = bounds.Width; obj.Height = bounds.Height;
            editor.Frame.TextObjects.Add(obj); editor.SelectedTextId = obj.Id;
        }
        creatingText = false; if (canvas is not null) canvas.DraftTextBounds = null;
        e.Pointer.Capture(null); RefreshAll();
    }

    private int Diameter(PointerEventArgs e)
    {
        var pressure = editor.Tool == Tool.Pressure && e.Pointer.Type == PointerType.Pen ? e.GetCurrentPoint(canvas).Properties.Pressure : 1;
        if (pressure <= 0) pressure = 1; // mouse or a pen without pressure data
        return Math.Max(1, (int)Math.Round(editor.BrushSize * pressure));
    }
}
