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
        if (canvas is null || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
        canvas.Focus(); var point = canvas.CanvasPoint(e);
        startX = lastX = (int)point.X; startY = lastY = (int)point.Y;
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
        if (editor.Tool is Tool.Line or Tool.Rectangle or Tool.Ellipse) shapeStart = editor.Layer.Rows.ToList();
        else Raster.Dot(editor.Layer, startX, startY, color, Radius(e));
        RefreshCanvas();
    }

    private void CanvasMoved(object? sender, PointerEventArgs e)
    {
        if (!dragging || canvas is null) return;
        var point = canvas.CanvasPoint(e); var x = (int)point.X; var y = (int)point.Y;
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
            if (editor.Tool == Tool.Line) Raster.Line(editor.Layer, startX, startY, x, y, editor.Color);
            else if (editor.Tool == Tool.Rectangle) Raster.Rectangle(editor.Layer, startX, startY, x, y, editor.Color);
            else if (editor.Tool == Tool.Ellipse) Raster.Ellipse(editor.Layer, startX, startY, x, y, editor.Color);
        }
        else
        {
            var color = editor.Tool == Tool.Eraser ? -1 : editor.Color;
            if (editor.Tool == Tool.Smooth) { x = (x + lastX) / 2; y = (y + lastY) / 2; }
            Raster.Line(editor.Layer, lastX, lastY, x, y, color, Radius(e));
        }
        lastX = x; lastY = y; RefreshCanvas();
    }

    private void CanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        CanvasMoved(sender, e); dragging = false; shapeStart = null;
        if (creatingText && canvas?.DraftTextBounds is Avalonia.Rect bounds && bounds.Width >= 4 && bounds.Height >= 4)
        {
            editor.BeforeChange();
            var obj = new TextObject { Key = editor.NewTextKey(), X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };
            editor.Frame.TextObjects.Add(obj); editor.SelectedTextId = obj.Id;
        }
        creatingText = false; if (canvas is not null) canvas.DraftTextBounds = null;
        e.Pointer.Capture(null); RefreshAll();
    }

    private int Radius(PointerEventArgs e)
    {
        if (editor.BrushSize == 1) return 0;
        var pressure = editor.Tool == Tool.Pressure ? e.GetCurrentPoint(canvas).Properties.Pressure : 1;
        if (pressure <= 0) pressure = 1; // mouse or a pen without pressure data
        return Math.Max(0, (int)Math.Round((editor.BrushSize - 1) * pressure / 2));
    }
}
