using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ComicEditor.Rendering;
using ComicEditor.Styles;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    private static MenuItem ViewItem(MainView view, string name)
    {
        var menu = view.GetVisualDescendants().OfType<Menu>().Single();
        IEnumerable<MenuItem> Items(MenuItem item) => new[] { item }.Concat(item.Items.OfType<MenuItem>().SelectMany(Items));
        return menu.Items.OfType<MenuItem>().SelectMany(Items).Single(item => item.Name == name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewMenuOwnsOnionSkinOpacityAndCompare(bool touch)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touch); var state = State(view);
            state.AddFrame(false);
            var window = new Window { Content = view, Width = touch ? 400 : 1280, Height = 800 };
            window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var previous = (CutsceneCanvas)typeof(MainView).GetField("previous", PrivateInstance)!.GetValue(view)!;
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ToggleButton>(), b => b.Content is "Onion skin" or "Compare");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "CompactOnion");
            var onion = ViewItem(view, "OnionSkinMenu");
            var opacity = ViewItem(view, "OnionOpacityMenu");
            var compare = ViewItem(view, "CompareMenu");
            Assert.Equal(MenuItemToggleType.CheckBox, onion.ToggleType);
            Assert.Equal(MenuItemToggleType.CheckBox, compare.ToggleType);
            Assert.Equal(state.OnionSkin, onion.IsChecked);
            Assert.Equal(state.Compare, compare.IsChecked);

            // Accessibility toggle actions change IsChecked without raising Click.
            onion.IsChecked = !state.OnionSkin;
            Assert.Equal(onion.IsChecked, state.OnionSkin);
            Assert.Equal(state.OnionSkin, opacity.IsEnabled);
            compare.IsChecked = !state.Compare;
            Assert.Equal(compare.IsChecked, state.Compare);

            // Exercise actual menu keyboard activation, including toggling twice.
            void Activate(MenuItem item)
            {
                if (touch) ((MenuItem)view.GetVisualDescendants().OfType<Menu>().Single().Items[0]!).IsSubMenuOpen = true;
                ViewItem(view, "ViewMenu").IsSubMenuOpen = true;
                _ = Capture(window);
                item.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                _ = Capture(window);
            }
            var initialOnion = state.OnionSkin;
            Activate(onion);
            Assert.Equal(!initialOnion, state.OnionSkin);
            Assert.Equal(state.OnionSkin, canvas.OnionSkin);
            Assert.Equal(state.OnionSkin, opacity.IsEnabled);
            Activate(onion);
            Assert.Equal(initialOnion, state.OnionSkin);
            if (!state.OnionSkin) Activate(onion);
            Activate(opacity);
            var slider = Named<Slider>(window, "OnionOpacity");
            slider.Value = 0.65; _ = Capture(window);
            Assert.Equal(0.65, state.OnionOpacity, 3);
            Assert.Equal(0.65, canvas.OnionOpacity, 3);
            Click(window, Caption(window, "Close"));
            Assert.DoesNotContain(view.GetVisualDescendants(), c => c is Control { Name: "ModalCard" });

            var initialCompare = state.Compare;
            Activate(compare);
            Assert.Equal(!initialCompare, state.Compare);
            Assert.Equal(state.Compare && !touch, previous.IsVisible);
            Activate(compare);
            Assert.Equal(initialCompare, state.Compare);
            Invoke(view, "Build", touch); _ = Capture(window);
            Assert.True(ViewItem(view, "OnionSkinMenu").IsChecked);
            Assert.Equal(state.Compare, ViewItem(view, "CompareMenu").IsChecked);
            Activate(ViewItem(view, "OnionOpacityMenu"));
            Assert.Equal(0.65, Named<Slider>(window, "OnionOpacity").Value, 3);
            Click(window, Caption(window, "Close"));
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("solarized-light")]
    [InlineData("dark")]
    public async Task SecondaryActionsAreQuietButShowHoverAndKeyboardFocus(string theme)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); Invoke(view, "SetTheme", theme); _ = Capture(window);
            var button = Named<Button>(window, "NewProjectTab");
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Contains("chrome", button.Classes);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color.A);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color.A);
            var before = button.Bounds;
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); _ = Capture(window);
            Assert.Equal(Color.Parse(EditorThemes.Find(theme).Selection), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            window.MouseMove(new Point(1000, 700));
            button.Focus(NavigationMethod.Tab); _ = Capture(window);
            Assert.Equal(Color.Parse(EditorThemes.Find(theme).Accent), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.BorderBrush).Color);
            Assert.Equal(before, button.Bounds);
            var tools = Named<StackPanel>(window, "ToolRail").Children.OfType<Button>().ToArray();
            Assert.Equal(11, tools.Length);
            Assert.All(tools, tool => Assert.DoesNotContain("chrome", tool.Classes));
            Assert.DoesNotContain("chrome", Named<Button>(window, "Swatch0").Classes);
            var undo = Named<Button>(window, "UndoButton");
            Assert.False(undo.IsEnabled);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(undo.GetVisualDescendants().OfType<ContentPresenter>().First().Background).Color.A);
            window.Close();
        }, CancellationToken.None);
    }
}
