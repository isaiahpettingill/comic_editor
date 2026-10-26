using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task TimingStartsAsReadoutAndClickEnterEscapeAndBlurHaveExplicitLifetimes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            var display = Named<Button>(window, "FrameTimingDisplay0");
            var input = Named<TextBox>(window, "FrameTiming0");
            Assert.True(display.IsVisible); Assert.False(input.IsVisible);
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(input.IsVisible); Assert.False(display.IsVisible);
            input.Text = "5.25";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(5250, state.Frame.DurationMs); Assert.False(input.IsVisible); Assert.Equal("5.25 s", display.Content);
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); input.Text = "10";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(5250, state.Frame.DurationMs); Assert.False(input.IsVisible);
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); input.Text = "6";
            Named<Button>(window, "FrameOptions0").Focus();
            Assert.Equal(6000, state.Frame.DurationMs); Assert.False(input.IsVisible);
            display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); input.Text = "invalid";
            Named<Button>(window, "FrameOptions0").Focus();
            Assert.Equal(6000, state.Frame.DurationMs); Assert.False(input.IsVisible); Assert.Equal("6 s", display.Content);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ItemMenusActOnTheirOwnFrameLayerAndTextNotTheSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.AddFrame(false); state.AddFrame(false); state.SelectFrame(2);
            var first = state.Scene.Frames[0]; var selected = state.Frame;
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var frameMenu = Named<Button>(window, "FrameOptions0").ContextMenu!;
            MenuAction(frameMenu, "FrameDelete0");
            Assert.DoesNotContain(first, state.Scene.Frames); Assert.Contains(selected, state.Scene.Frames);
            state.SelectFrame(0);
            state.Frame.Layers.Add(ArtworkLayer.Create("Other", state.Scene.Width, state.Scene.Height));
            state.LayerIndex = 1; var kept = state.Layer; var removed = state.Frame.Layers[0];
            var text = state.CreateText(); state.Frame.TextObjects.Add(text);
            var otherText = state.CreateText(); state.Frame.TextObjects.Add(otherText); state.SelectedTextId = otherText.Id;
            Invoke(view, "RefreshAll"); _ = Capture(window);
            MenuAction(Named<Button>(window, "LayerOptions0").ContextMenu!, "LayerDelete0");
            Assert.DoesNotContain(removed, state.Frame.Layers); Assert.Same(kept, state.Layer);
            _ = Capture(window);
            MenuAction(Named<Button>(window, "TextOptions_" + text.Id).ContextMenu!, "TextDelete_" + text.Id);
            Assert.DoesNotContain(text, state.Frame.TextObjects); Assert.Contains(otherText, state.Frame.TextObjects);
            Assert.Equal(otherText.Id, state.SelectedTextId);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RetainedFrameMenuCannotMutateAnotherDocument()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            var menu = Named<Button>(window, "FrameOptions0").ContextMenu!;
            state.Load(CutsceneFile.Write(Cutscene.Create(2, 2)), "different.ctsc");
            MenuAction(menu, "FrameDuplicate0"); Assert.Single(state.Scene.Frames);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FrameModifierSelectionRangesBulkDuplicateAndDeleteAreUndoable()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            for (var i = 0; i < 4; i++) state.AddFrame(false);
            state.SelectFrame(0);
            var ids = state.Scene.Frames.Select(f => f.Id).ToArray();
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll");
            void Select(int index, KeyModifiers modifiers) => Invoke(view, "SelectListItem", index, true, modifiers,
                (Action)(() => state.SelectFrame(index)));
            string[] Selected() => ((HashSet<string>)typeof(MainView).GetField("selectedFrameIds", PrivateInstance)!.GetValue(view)!).Order().ToArray();
            Select(0, KeyModifiers.None); Select(2, KeyModifiers.Shift);
            Assert.Equal(ids.Take(3).Order(), Selected());
            Select(4, KeyModifiers.Control); Assert.Equal(new[] { ids[0], ids[1], ids[2], ids[4] }.Order(), Selected());
            Select(3, KeyModifiers.Control | KeyModifiers.Shift); Assert.Equal(ids.Order(), Selected());
            // Ctrl toggles a member without destroying the remaining selected set.
            Select(1, KeyModifiers.Control); Assert.DoesNotContain(ids[1], Selected()); Assert.Equal(4, Selected().Length);
            Select(1, KeyModifiers.None); Select(2, KeyModifiers.Shift);
            Invoke(view, "DuplicateSelectedFrames"); Assert.Equal(7, state.Scene.Frames.Count); Assert.Equal(2, Selected().Length);
            Assert.True(state.Undo()); Assert.Equal(ids, state.Scene.Frames.Select(f => f.Id));
            Invoke(view, "RefreshAll"); Select(1, KeyModifiers.None); Select(3, KeyModifiers.Shift);
            Invoke(view, "DeleteFrames", state.Scene.Frames[2]); Assert.Equal(new[] { ids[0], ids[4] }, state.Scene.Frames.Select(f => f.Id));
            Assert.True(state.Undo()); Assert.Equal(ids, state.Scene.Frames.Select(f => f.Id));
            Invoke(view, "RefreshAll"); Select(0, KeyModifiers.None); Select(4, KeyModifiers.Shift);
            Invoke(view, "DeleteFrames", state.Scene.Frames[0]); Assert.Equal(5, state.Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MultiFrameDragAndClipboardPreserveOrderAndUseOneUndo()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            for (var i = 0; i < 4; i++) state.AddFrame(false);
            state.SelectFrame(0); var ids = state.Scene.Frames.Select(f => f.Id).ToArray();
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll");
            void Select(int index, KeyModifiers modifiers) => Invoke(view, "SelectListItem", index, true, modifiers,
                (Action)(() => state.SelectFrame(index)));
            Select(1, KeyModifiers.None); Select(2, KeyModifiers.Shift);
            Invoke(view, "ReorderSelectedItems", 1, 5, true);
            Assert.Equal(new[] { ids[0], ids[3], ids[4], ids[1], ids[2] }, state.Scene.Frames.Select(f => f.Id));
            Assert.True(state.Undo()); Assert.Equal(ids, state.Scene.Frames.Select(f => f.Id));
            Invoke(view, "RefreshAll"); Select(1, KeyModifiers.None); Select(2, KeyModifiers.Shift);
            state.Scene.Frames[1].DurationMs = 5000; state.Scene.Frames[2].DurationMs = 6000;
            Invoke(view, "CopyFrame", 1); Invoke(view, "PasteFrame", 4);
            Assert.Equal(7, state.Scene.Frames.Count); Assert.Equal(5000, state.Scene.Frames[5].DurationMs); Assert.Equal(6000, state.Scene.Frames[6].DurationMs);
            Assert.True(state.Undo()); Assert.Equal(5, state.Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealModifierClicksSelectFrameRangesWithoutStartingDrag()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.AddFrame(false); state.AddFrame(false); state.SelectFrame(0);
            var window = new Window { Content = view, Width = 1200, Height = 1100 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var ids = state.Scene.Frames.Select(f => f.Id).ToArray();
            void ClickFrame(int index, RawInputModifiers modifiers)
            {
                var row = Named<Border>(window, "FrameRow" + index);
                var point = row.TranslatePoint(new Point(10, 10), window)!.Value;
                window.MouseDown(point, MouseButton.Left, modifiers); window.MouseUp(point, MouseButton.Left, modifiers); _ = Capture(window);
            }
            ClickFrame(0, RawInputModifiers.None); ClickFrame(2, RawInputModifiers.Shift);
            var selected = (HashSet<string>)typeof(MainView).GetField("selectedFrameIds", PrivateInstance)!.GetValue(view)!;
            Assert.Equal(ids.Order(), selected.Order());
            ClickFrame(1, RawInputModifiers.Control); Assert.Equal(new[] { ids[0], ids[2] }.Order(), selected.Order());
            Assert.Equal(ids, state.Scene.Frames.Select(f => f.Id));
            SaveCapture(window, "COMIC_ITEM_SELECTION_SCREENSHOT"); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LayerAndTextRangesDeleteSelectedSetAndPreserveLastLayer()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.Frame.Layers.Add(ArtworkLayer.Create("Middle", state.Scene.Width, state.Scene.Height));
            state.Frame.Layers.Add(ArtworkLayer.Create("Top", state.Scene.Width, state.Scene.Height));
            var texts = Enumerable.Range(0, 3).Select(_ => state.CreateText()).ToArray(); state.Frame.TextObjects.AddRange(texts);
            var window = new Window { Content = view, Width = 1200, Height = 900 }; window.Show(); Invoke(view, "RefreshAll");
            void Layer(int index, KeyModifiers modifiers) => Invoke(view, "SelectListItem", index, false, modifiers,
                (Action)(() => state.LayerIndex = index));
            Layer(2, KeyModifiers.None); Layer(1, KeyModifiers.Shift);
            Invoke(view, "DeleteLayers", state.Frame.Layers[2]); Assert.Single(state.Frame.Layers);
            Assert.True(state.Undo()); Assert.Equal(3, state.Frame.Layers.Count); Invoke(view, "RefreshAll");
            Layer(2, KeyModifiers.None); Layer(0, KeyModifiers.Shift);
            Invoke(view, "DeleteLayers", state.Frame.Layers[0]); Assert.Equal(3, state.Frame.Layers.Count);
            texts = state.Frame.TextObjects.ToArray();
            Invoke(view, "SelectTextItem", texts[0], KeyModifiers.None); Invoke(view, "SelectTextItem", texts[1], KeyModifiers.Shift);
            Invoke(view, "DeleteTextObjects", texts[1]); Assert.Single(state.Frame.TextObjects); Assert.Equal(texts[2].Id, state.Frame.TextObjects[0].Id);
            Assert.True(state.Undo()); Assert.Equal(3, state.Frame.TextObjects.Count); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task KeyboardShiftRangesAndControlFocusNavigationUseTheFocusedList()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.AddFrame(false); state.AddFrame(false); state.SelectFrame(0);
            var ids = state.Scene.Frames.Select(f => f.Id).ToArray();
            var window = new Window { Content = view, Width = 1200, Height = 1000 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            void Key(int index, Key key, KeyModifiers modifiers)
            {
                var row = Named<Border>(window, "FrameRow" + index); row.Focus();
                row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            }
            Key(0, Avalonia.Input.Key.Down, KeyModifiers.Shift);
            var selected = (HashSet<string>)typeof(MainView).GetField("selectedFrameIds", PrivateInstance)!.GetValue(view)!;
            Assert.Equal(ids.Take(2).Order(), selected.Order());
            Key(1, Avalonia.Input.Key.Down, KeyModifiers.Control | KeyModifiers.Shift); Assert.Equal(ids.Order(), selected.Order());
            Key(2, Avalonia.Input.Key.Up, KeyModifiers.Control); Assert.Equal(ids.Order(), selected.Order()); Assert.Equal(2, state.FrameIndex);
            // Cutting an all-selected storyboard is a no-op: an editable document keeps one frame.
            Key(1, Avalonia.Input.Key.X, KeyModifiers.Control); Assert.Equal(3, state.Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BottomStoryboardReordersHorizontallyAndSupportsShiftRight()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view); state.AddFrame(false); state.AddFrame(false); state.SelectFrame(0);
            var window = new Window { Content = view, Width = 1200, Height = 900 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var palette = Named<Border>(window, "PaletteHeader"); var slot = Named<Border>(window, "PaneSlot0");
            var start = palette.TranslatePoint(new Point(40, 10), window)!.Value;
            var end = slot.TranslatePoint(new Point(80, 90), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); window.MouseUp(end, MouseButton.Left); _ = Capture(window);
            var ids = state.Scene.Frames.Select(f => f.Id).ToArray();
            var first = Named<Border>(window, "FrameRow0"); var last = Named<Border>(window, "FrameRow2");
            start = first.TranslatePoint(new Point(10, 20), window)!.Value;
            end = last.TranslatePoint(new Point(last.Bounds.Width * .8, 20), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); window.MouseUp(end, MouseButton.Left); _ = Capture(window);
            Assert.Equal(new[] { ids[1], ids[2], ids[0] }, state.Scene.Frames.Select(f => f.Id));
            Assert.True(state.Undo()); Invoke(view, "RefreshAll"); _ = Capture(window);
            Invoke(view, "SelectListItem", 0, true, KeyModifiers.None, (Action)(() => state.SelectFrame(0)));
            first = Named<Border>(window, "FrameRow0"); first.Focus();
            first.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = KeyModifiers.Shift });
            var selected = (HashSet<string>)typeof(MainView).GetField("selectedFrameIds", PrivateInstance)!.GetValue(view)!;
            Assert.Equal(ids.Take(2).Order(), selected.Order());
            SaveCapture(window, "COMIC_BOTTOM_STORYBOARD_SCREENSHOT"); window.Close();
        }, CancellationToken.None);
    }

    private static void MenuAction(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name)
        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
}
