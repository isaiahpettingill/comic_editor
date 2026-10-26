using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task PaletteCanSwapWithStoryboardUsingVisibleDropTargetsAndPersist()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 900 };
            window.Show(); _ = Capture(window);
            var paletteHeader = Named<Border>(window, "PaletteHeader");
            var story = Named<Border>(window, "PaneSlot0");
            var start = paletteHeader.TranslatePoint(new Point(50, 10), window)!.Value;
            var end = story.TranslatePoint(new Point(80, 90), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); _ = Capture(window);
            Assert.Equal(4, Named<Canvas>(window, "DockingTargets").Children.Count);
            Assert.Equal(3, Named<Border>(window, "DockTarget0").BorderThickness.Left);
            window.MouseUp(end, MouseButton.Left); _ = Capture(window);
            SaveCapture(window, "COMIC_DOCKING_SCREENSHOT");
            Assert.Contains(Named<Border>(window, "PaneSlot0").GetVisualDescendants(), x => x is Border { Name: "PaletteHeader" });
            Assert.Contains(Named<Border>(window, "PaneSlot3").GetVisualDescendants(), x => x is Border { Name: "StoryboardHeader" });
            Assert.Equal(new[] { "Palette", "Canvas", "Inspector", "Storyboard" }, State(view).Preferences.PaneLayout);
            Assert.DoesNotContain(view.GetVisualDescendants(), x => x is Canvas { Name: "DockingTargets" });
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DockingEscapeOrOutsideReleaseDoesNotMovePane()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 900 };
            window.Show(); _ = Capture(window);
            var header = Named<Border>(window, "PaletteHeader");
            var start = header.TranslatePoint(new Point(60, 10), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(new Point(100, 220), RawInputModifiers.LeftMouseButton);
            header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            window.MouseUp(new Point(100, 220), MouseButton.Left); _ = Capture(window);
            Assert.Contains(Named<Border>(window, "PaneSlot3").GetVisualDescendants(), x => x is Border { Name: "PaletteHeader" });
            window.MouseDown(start, MouseButton.Left); window.MouseMove(new Point(-30, -30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(-30, -30), MouseButton.Left); _ = Capture(window);
            Assert.Contains(Named<Border>(window, "PaneSlot3").GetVisualDescendants(), x => x is Border { Name: "PaletteHeader" });
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void InvalidDockPreferencesDoNotBreakStartup()
    {
        var preferences = EditorPreferences.Parse("{\"PaneLayout\":[\"garbage\",\"Canvas\",\"Palette\",\"Inspector\"],\"PaneWidths\":[-1,0,300]}");
        Assert.NotNull(preferences);
    }
}
