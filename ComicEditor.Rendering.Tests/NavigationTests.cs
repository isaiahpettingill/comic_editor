using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinchZoomAndThreeFingerPanNeverEditArtwork(bool mobile)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(mobile); var window = new Window { Content = view, Width = mobile ? 400 : 1100, Height = 800 };
            window.Show(); _ = Capture(window);
            var editor = State(view); editor.Preferences.Zoom = 4; Invoke(view, "RefreshCanvas"); _ = Capture(window);
            // Preserve a redo entry through the cancelled first-finger edit.
            editor.BeforeChange(); editor.Layer.SetPixel(0, 0, 1); editor.Undo(); Invoke(view, "RefreshCanvas");
            var before = CutsceneFile.Write(editor.Scene);
            var viewport = Named<ScrollViewer>(window, "CanvasViewport"); viewport.Offset = new Vector(200, 100); _ = Capture(window);
            var p = viewport.TranslatePoint(new Point(100, 120), window)!.Value;
            using var one = window.TouchBegin(p); window.TouchMove(one, p + new Vector(5, 0));
            using var two = window.TouchBegin(p + new Vector(80, 0));
            Assert.Equal(before, CutsceneFile.Write(editor.Scene)); Assert.True(editor.CanRedo); Assert.False(editor.CanUndo);
            window.TouchMove(two, p + new Vector(140, 0)); _ = Capture(window);
            Assert.True(editor.Preferences.Zoom > 4);
            using var three = window.TouchBegin(p + new Vector(60, 70));
            var scale = editor.Preferences.Zoom; var offset = viewport.Offset;
            window.TouchMove(one, p + new Vector(-25, -30));
            window.TouchMove(two, p + new Vector(110, -30));
            window.TouchMove(three, p + new Vector(30, 40)); _ = Capture(window);
            Assert.Equal(scale, editor.Preferences.Zoom);
            Assert.InRange(viewport.Offset.X - offset.X, 29, 31); Assert.InRange(viewport.Offset.Y - offset.Y, 29, 31);
            window.TouchEnd(three, p + new Vector(30, 40)); window.TouchEnd(two, p + new Vector(110, -30));
            window.TouchMove(one, p + new Vector(20, 20)); window.TouchEnd(one, p + new Vector(20, 20));
            Assert.Equal(before, CutsceneFile.Write(editor.Scene)); Assert.True(editor.CanRedo); Assert.False(editor.CanUndo);
            // A fresh single-finger stroke still draws and remains undoable.
            using var draw = window.TouchBegin(p); window.TouchMove(draw, p + new Vector(30, 20)); window.TouchEnd(draw, p + new Vector(30, 20));
            Assert.NotEqual(before, CutsceneFile.Write(editor.Scene)); Assert.True(editor.Undo()); Assert.Equal(before, CutsceneFile.Write(editor.Scene));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WheelZoomAnchorsPointerAndMiddleButtonPansWithoutDrawing()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1100, Height = 800 };
            window.Show(); _ = Capture(window); var editor = State(view);
            editor.Preferences.Zoom = 4; Invoke(view, "RefreshCanvas"); _ = Capture(window);
            var viewport = Named<ScrollViewer>(window, "CanvasViewport"); viewport.Offset = new Vector(200, 100); _ = Capture(window);
            var canvas = window.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var p = viewport.TranslatePoint(new Point(200, 150), window)!.Value;
            var pixel = window.TranslatePoint(p, canvas)!.Value;
            window.MouseWheel(p, new Vector(0, 1)); _ = Capture(window);
            Assert.InRange(editor.Preferences.Zoom, 4.99, 5.01);
            Assert.True(((Vector)(canvas.TranslatePoint(pixel, window)!.Value - p)).Length < 2, "Zoom should preserve the pixel beneath the pointer.");
            var before = CutsceneFile.Write(editor.Scene); var offset = viewport.Offset;
            window.MouseDown(p, MouseButton.Middle); window.MouseMove(p + new Vector(-60, -40), RawInputModifiers.MiddleMouseButton); window.MouseUp(p + new Vector(-60, -40), MouseButton.Middle);
            Assert.InRange(viewport.Offset.X - offset.X, 59, 61); Assert.InRange(viewport.Offset.Y - offset.Y, 39, 41);
            Assert.Equal(before, CutsceneFile.Write(editor.Scene)); Assert.False(editor.CanUndo);
            window.MouseWheel(p, new Vector(0, -1)); Assert.InRange(editor.Preferences.Zoom, 3.99, 4.01);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinchStartsFromFitAndCaptureLossDoesNotLeaveDrawingActive(bool rebuild)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(true); var window = new Window { Content = view, Width = 400, Height = 800 };
            window.Show(); _ = Capture(window); var editor = State(view);
            var canvas = window.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var p = canvas.TranslatePoint(new Point(80, 90), window)!.Value;
            var scale = canvas.TranslatePoint(new Point(81, 90), window)!.Value.X - p.X;
            var before = CutsceneFile.Write(editor.Scene);
            using var one = window.TouchBegin(p);
            if (rebuild)
            {
                Invoke(view, "Build", true); _ = Capture(window);
                window.TouchEnd(one, p);
            }
            else
            {
                using var two = window.TouchBegin(p + new Vector(70, 0));
                window.TouchMove(two, p + new Vector(140, 0)); _ = Capture(window);
                Assert.InRange(editor.Preferences.Zoom, scale * 1.95, scale * 2.05);
                window.TouchEnd(two, p + new Vector(140, 0)); window.TouchEnd(one, p);
            }
            Assert.Equal(before, CutsceneFile.Write(editor.Scene)); Assert.False(editor.CanUndo);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(Tool.Fill)]
    [InlineData(Tool.Text)]
    [InlineData(Tool.Spray)]
    public async Task TouchNavigationCancelsProvisionalToolChanges(Tool tool)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(true); var window = new Window { Content = view, Width = 400, Height = 800 };
            window.Show(); _ = Capture(window); var editor = State(view); editor.Tool = tool;
            var canvas = window.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var p = canvas.TranslatePoint(new Point(60, 70), window)!.Value;
            var before = CutsceneFile.Write(editor.Scene);
            using var one = window.TouchBegin(p); window.TouchMove(one, p + new Vector(40, 30));
            using var two = window.TouchBegin(p + new Vector(80, 0));
            window.TouchEnd(two, p + new Vector(80, 0)); window.TouchEnd(one, p + new Vector(40, 30));
            Assert.Equal(before, CutsceneFile.Write(editor.Scene)); Assert.False(editor.CanUndo);
            Assert.Null(canvas.DraftTextBounds); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TouchPolygonContinuesAcrossTapsAndSurvivesNavigation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(true); var window = new Window { Content = view, Width = 400, Height = 800 };
            window.Show(); _ = Capture(window); var editor = State(view); editor.Tool = Tool.Polygon;
            var canvas = window.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var p = canvas.TranslatePoint(new Point(60, 70), window)!.Value;
            var before = CutsceneFile.Write(editor.Scene);
            using (var first = window.TouchBegin(p)) window.TouchEnd(first, p);
            using (var next = window.TouchBegin(p + new Vector(60, 0))) window.TouchEnd(next, p + new Vector(60, 0));
            var preview = CutsceneFile.Write(editor.Scene); Assert.NotEqual(before, preview); Assert.False(editor.CanUndo);
            using (var one = window.TouchBegin(p + new Vector(60, 60)))
            using (var two = window.TouchBegin(p + new Vector(120, 60)))
            { window.TouchEnd(two, p + new Vector(120, 60)); window.TouchEnd(one, p + new Vector(60, 60)); }
            Assert.Equal(preview, CutsceneFile.Write(editor.Scene)); Assert.False(editor.CanUndo);
            using (var last = window.TouchBegin(p + new Vector(60, 60))) window.TouchEnd(last, p + new Vector(60, 60));
            Invoke(view, "FinishPath", false); Assert.True(editor.CanUndo);
            Assert.True(editor.Undo()); Assert.Equal(before, CutsceneFile.Write(editor.Scene)); window.Close();
        }, CancellationToken.None);
    }
}
