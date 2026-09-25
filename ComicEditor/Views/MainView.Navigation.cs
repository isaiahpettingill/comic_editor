using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ComicEditor.Views;

public partial class MainView
{
    private readonly Dictionary<IPointer, Point> canvasTouches = new();
    private bool touchNavigation, touchDrawing, dispatchingTouch;
    private Action? cancelTouchEdit;
    private double touchInitialZoom, pinchDistance, pinchZoom;
    private Point pinchAnchor, panPoint, touchPanPoint;
    private IPointer? panPointer, drawingPointer;

    private void AttachCanvasNavigation()
    {
        canvasScroll!.AddHandler(PointerPressedEvent, NavigationPressed, RoutingStrategies.Tunnel);
        canvasScroll.AddHandler(PointerMovedEvent, NavigationMoved, RoutingStrategies.Tunnel);
        canvasScroll.AddHandler(PointerReleasedEvent, NavigationReleased, RoutingStrategies.Tunnel);
        canvasScroll.AddHandler(PointerCaptureLostEvent, NavigationCaptureLost, RoutingStrategies.Bubble);
    }

    private double CanvasScale => canvasPair?.TranslatePoint(new Point(1, 0), canvasScroll!)?.X
        - canvasPair?.TranslatePoint(default, canvasScroll!)?.X ?? 1;

    private void ZoomAt(double value, Point anchor)
    {
        if (canvasScroll is null || canvasPair is null) return;
        var local = canvasScroll.TranslatePoint(anchor, canvasPair);
        zoom = Math.Clamp(value, .1, 32);
        RefreshCanvas(); canvasScroll.UpdateLayout();
        if (local is Point p && canvasPair.TranslatePoint(p, canvasScroll) is Point moved)
            canvasScroll.Offset += moved - anchor;
    }

    private void NavigationPressed(object? sender, PointerPressedEventArgs e)
    {
        if (canvasScroll is null || canvas is null) return;
        // Keep scrollbar controls usable on desktop.
        if (e.Source is Visual source && (source is ScrollBar or TextBox || source.GetVisualAncestors().Any(v => v is ScrollBar or TextBox))) return;
        var buttons = e.GetCurrentPoint(canvasScroll).Properties;
        if (e.Pointer.Type == PointerType.Mouse && buttons.IsMiddleButtonPressed && !buttons.IsLeftButtonPressed)
        {
            if (dragging || canvasTouches.Count != 0) return;
            panPointer = e.Pointer; panPoint = e.GetPosition(canvasScroll);
            e.Pointer.Capture(canvasScroll); canvasScroll.Cursor = new Cursor(StandardCursorType.Hand); e.Handled = true;
            return;
        }
        if (e.Pointer.Type != PointerType.Touch) return;
        e.Handled = true;
        canvasTouches[e.Pointer] = e.GetPosition(canvasScroll);
        if (canvasTouches.Count == 1)
        {
            touchNavigation = dragging || panPointer is not null; // Ignore palm contacts during pen/mouse drawing.
            touchInitialZoom = zoom;
            var p = e.GetPosition(canvas);
            touchDrawing = !touchNavigation && new Rect(canvas.Bounds.Size).Contains(p);
            if (touchDrawing)
            {
                cancelTouchEdit = CaptureTouchEdit();
                dispatchingTouch = true;
                try { CanvasPressed(canvas, e); }
                finally { dispatchingTouch = false; }
                // Fill/eyedropper don't retain capture, but still need their release.
                e.Pointer.Capture(canvas);
            }
            else e.Pointer.Capture(canvasScroll);
        }
        else
        {
            if (!touchNavigation)
            {
                touchNavigation = true; touchDrawing = false;
                CancelTouchDrawing();
            }
            e.Pointer.Capture(canvasScroll);
        }
        RebaseTouchNavigation();
    }

    private void NavigationMoved(object? sender, PointerEventArgs e)
    {
        if (canvasScroll is null) return;
        if (e.Pointer == panPointer)
        {
            var p = e.GetPosition(canvasScroll); canvasScroll.Offset += panPoint - p; panPoint = p;
            e.Handled = true; return;
        }
        if (!canvasTouches.ContainsKey(e.Pointer)) return;
        canvasTouches[e.Pointer] = e.GetPosition(canvasScroll); e.Handled = true;
        if (canvasTouches.Count == 1 && touchDrawing) { CanvasMoved(canvas, e); return; }
        if (!touchNavigation || dragging && drawingPointer?.Type != PointerType.Touch || panPointer is not null) return;
        if (canvasTouches.Count == 2)
        {
            var points = canvasTouches.Values.ToArray(); var distance = ((Vector)(points[1] - points[0])).Length; var center = TouchCenter();
            if (pinchDistance >= 4) ZoomAt(pinchZoom * distance / pinchDistance, center);
            else RebaseTouchNavigation();
            canvasScroll.Offset += touchPanPoint - center; touchPanPoint = center;
        }
        else if (canvasTouches.Count >= 3)
        {
            var p = TouchCenter(); canvasScroll.Offset += touchPanPoint - p; touchPanPoint = p;
        }
    }

    private Point TouchCenter() => new(canvasTouches.Values.Average(p => p.X), canvasTouches.Values.Average(p => p.Y));

    private void RebaseTouchNavigation()
    {
        if (canvasTouches.Count == 0) return;
        touchPanPoint = pinchAnchor = TouchCenter(); pinchZoom = CanvasScale;
        var points = canvasTouches.Values.ToArray(); pinchDistance = points.Length == 2 ? ((Vector)(points[1] - points[0])).Length : 0;
    }

    private void NavigationReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer == panPointer)
        {
            panPointer = null; if (canvasScroll is not null) canvasScroll.Cursor = null;
            e.Pointer.Capture(null); e.Handled = true; return;
        }
        if (!canvasTouches.ContainsKey(e.Pointer)) return;
        e.Handled = true;
        // Remove before releasing capture: that generates CaptureLost synchronously.
        canvasTouches.Remove(e.Pointer);
        if (touchDrawing) { CanvasReleased(canvas, e); touchDrawing = false; cancelTouchEdit = null; }
        e.Pointer.Capture(null);
        if (canvasTouches.Count == 0) { touchNavigation = false; cancelTouchEdit = null; RefreshTools(); }
        else RebaseTouchNavigation();
    }

    private void NavigationCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (dispatchingTouch) return;
        if (e.Pointer == panPointer) { panPointer = null; if (canvasScroll is not null) canvasScroll.Cursor = null; }
        if (!canvasTouches.Remove(e.Pointer)) return;
        if (touchDrawing) { CancelTouchDrawing(); touchDrawing = false; }
        touchNavigation = canvasTouches.Count > 0; RebaseTouchNavigation();
    }

    private void CancelTouchDrawing()
    {
        StopSpray(); dragging = creatingText = movingSelection = false; drawingPointer = null;
        shapeStart = selectionBase = null; smoother = null; lasso.Clear();
        FinishPath(cancel: true);
        cancelTouchEdit?.Invoke(); cancelTouchEdit = null; zoom = touchInitialZoom;
        selection = null;
        if (canvas is not null) { canvas.DraftTextBounds = null; canvas.SelectionOutline = null; }
        RefreshAll();
    }

    private Action CaptureTouchEdit()
    {
        var owner = editor.Scene; var restore = editor.CaptureProvisionalEdit();
        var priorBase = pathBase?.ToList(); var priorPoints = path.ToArray(); var priorTool = pathTool;
        var stage = curveStage; var end = curveEnd; var control1 = curveControl1; var control2 = curveControl2;
        return () =>
        {
            if (!ReferenceEquals(editor.Scene, owner)) return;
            restore();
            // A polygon or curve may span several taps. Navigation must preserve
            // its earlier points and the original rows used when committing it.
            pathBase = priorBase; pathLayer = priorBase is null ? null : editor.Layer;
            path.Clear(); path.AddRange(priorPoints); pathTool = priorTool;
            curveStage = stage; curveEnd = end; curveControl1 = control1; curveControl2 = control2;
        };
    }

    private void ResetCanvasNavigation()
    {
        if (cancelTouchEdit is not null) CancelTouchDrawing();
        var pointers = canvasTouches.Keys.ToArray(); canvasTouches.Clear();
        foreach (var pointer in pointers) pointer.Capture(null);
        var mouse = panPointer; panPointer = null; mouse?.Capture(null);
        touchDrawing = touchNavigation = false; drawingPointer = null;
    }
}
