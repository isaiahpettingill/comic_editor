using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task DesktopScrollbarsReserveSpaceBesideToolButtonsAndCanvas()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1000, Height = 600 }; window.Show(); _ = Capture(window);
            var rail = Named<StackPanel>(window, "ToolRail");
            var scroll = rail.GetVisualAncestors().OfType<ScrollViewer>().First();
            var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
            Assert.True(bar.IsVisible);
            AssertOutsideContent(bar);
            var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            foreach (var button in rail.Children.OfType<Button>())
                Assert.True(button.TranslatePoint(new Point(button.Bounds.Width, 0), presenter)!.Value.X <= presenter.Bounds.Width);
            // Hovering used to expand the overlay across the icons.
            window.MouseMove(bar.TranslatePoint(new Point(4, 20), window)!.Value); _ = Capture(window);
            AssertOutsideContent(bar);
            window.MouseWheel(rail.TranslatePoint(new Point(15, 100), window)!.Value, new Vector(0, -3)); _ = Capture(window);
            Assert.True(scroll.Offset.Y > 0);
            State(view).Preferences.Zoom = 8; Invoke(view, "RefreshCanvas"); _ = Capture(window);
            var canvas = Named<ScrollViewer>(window, "CanvasViewport");
            var bars = canvas.GetVisualDescendants().OfType<ScrollBar>().Where(b => b.IsVisible).ToArray();
            Assert.Equal(2, bars.Length); foreach (var item in bars) AssertOutsideContent(item);
            SaveCapture(window, "COMIC_SCROLL_DESKTOP"); window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DialogScrollingPreservesContentAndTouchGestures(bool touch)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touch);
            var window = new Window { Content = view, Width = touch ? 320 : 1000, Height = touch ? 568 : 600 }; window.Show();
            var body = new StackPanel { Margin = new Thickness(12) };
            var multiline = new TextBox { Text = string.Join('\n', Enumerable.Repeat(new string('W', 80), 12)), Height = 90, AcceptsReturn = true };
            ScrollViewer.SetHorizontalScrollBarVisibility(multiline, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(multiline, ScrollBarVisibility.Auto);
            body.Children.Add(multiline);
            for (var i = 0; i < 30; i++) body.Children.Add(new TextBox { Text = "Editable row " + i, Height = 44 });
            Invoke(view, "ShowModal", "Scrolling", body, null!, "Apply"); _ = Capture(window);
            var scroller = body.GetVisualAncestors().OfType<ScrollViewer>().First();
            var bars = scroller.GetVisualDescendants().OfType<ScrollBar>().ToArray();
            Assert.True(multiline.GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Height > 90);
            if (touch) Assert.DoesNotContain(bars, b => b.IsVisible);
            else foreach (var bar in bars.Where(b => b.IsVisible)) AssertOutsideContent(bar);
            // Test drag scrolling independently of wall-clock inertia timing.
            scroller.IsScrollInertiaEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var from = scroller.TranslatePoint(new Point(5, 150), window)!.Value;
            using var contact = window.TouchBegin(from);
            window.TouchMove(contact, from - new Vector(0, 30));
            window.TouchMove(contact, from - new Vector(0, 90));
            Assert.True(scroller.Offset.Y > 0, "Hidden scrollbars must still allow touch scrolling.");
            window.TouchEnd(contact, from - new Vector(0, 100)); _ = Capture(window);
            if (touch) Assert.DoesNotContain(bars, b => b.IsVisible);
            SaveCapture(window, touch ? "COMIC_SCROLL_MOBILE" : "COMIC_SCROLL_DIALOG");
            Invoke(view, "CloseModal"); window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolPopupUsesTheSameScrollbarPolicy(bool touch)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touch); var window = new Window { Content = view, Width = 720, Height = 320 };
            window.Show(); _ = Capture(window);
            var picker = Named<Button>(window, "CompactToolPicker"); Click(window, picker);
            var scroll = Assert.IsType<ScrollViewer>(Assert.IsType<Flyout>(picker.Flyout).Content); _ = Capture(window);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
            if (touch) Assert.False(bar.IsVisible);
            else { Assert.True(bar.IsVisible); AssertOutsideContent(bar); }
            picker.Flyout!.Hide(); window.Close();
        }, CancellationToken.None);
    }

    private static void AssertOutsideContent(ScrollBar bar)
    {
        var owner = Assert.IsAssignableFrom<ScrollViewer>(bar.TemplatedParent);
        var presenter = owner.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        var contentEnd = presenter.TranslatePoint(new Point(presenter.Bounds.Width, presenter.Bounds.Height), owner)!.Value;
        var barStart = bar.TranslatePoint(default, owner)!.Value;
        Assert.True(bar.Orientation == Orientation.Vertical ? contentEnd.X <= barStart.X + .1 : contentEnd.Y <= barStart.Y + .1,
            $"{bar.Orientation} scrollbar overlaps the content viewport.");
    }
}
