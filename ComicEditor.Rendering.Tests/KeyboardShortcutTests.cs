using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    private static KeyEventArgs Shortcut(MainView view, Key key, KeyModifiers modifiers = KeyModifiers.None, object? source = null)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = source ?? view };
        typeof(MainView).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(view, [view, args]);
        return args;
    }

    [Fact]
    public async Task ToolKeysRecallAndCycleEveryBrushVariantWithoutMenu()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show();
            Assert.True(Shortcut(view, Key.B).Handled); Assert.Equal(Tool.Pixel, State(view).Tool);
            Shortcut(view, Key.B, KeyModifiers.Shift); Assert.Equal(Tool.Smooth, State(view).Tool);
            Shortcut(view, Key.E); Assert.Equal(Tool.Eraser, State(view).Tool);
            Shortcut(view, Key.B); Assert.Equal(Tool.Smooth, State(view).Tool);
            Shortcut(view, Key.B, KeyModifiers.Shift); Assert.Equal(Tool.Pressure, State(view).Tool);
            Shortcut(view, Key.U); Assert.Equal(Tool.Rectangle, State(view).Tool);
            Shortcut(view, Key.U, KeyModifiers.Shift); Assert.Equal(Tool.RoundedRectangle, State(view).Tool);
            Shortcut(view, Key.M, KeyModifiers.Shift); Assert.Equal(Tool.Lasso, State(view).Tool);
            Shortcut(view, Key.M, KeyModifiers.Shift); Assert.Equal(Tool.EllipseSelect, State(view).Tool);
            Shortcut(view, Key.R, KeyModifiers.Shift); Assert.Equal(Tool.Blur, State(view).Tool);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FrameShortcutsAreUndoableAndTypingOrAltGrNeverCreatesFrames()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show();
            var editor = State(view); var count = editor.Scene.Frames.Count;
            Shortcut(view, Key.N, KeyModifiers.Alt); Assert.Equal(count + 1, editor.Scene.Frames.Count); Assert.Equal(4000, editor.Frame.DurationMs);
            Shortcut(view, Key.D, KeyModifiers.Alt); Assert.Equal(count + 2, editor.Scene.Frames.Count);
            Assert.True(editor.Undo()); Assert.Equal(count + 1, editor.Scene.Frames.Count);
            var text = new TextBox();
            Assert.False(Shortcut(view, Key.N, KeyModifiers.Alt, text).Handled);
            Assert.False(Shortcut(view, Key.B, source: text).Handled);
            Assert.False(Shortcut(view, Key.N, KeyModifiers.Control | KeyModifiers.Alt).Handled);
            Assert.Equal(count + 1, editor.Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShortcutReferenceAndBrushSizeAreDiscoverableAndBounded()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show();
            Assert.Equal("KeyboardShortcutsMenu", ((MenuItem)Invoke(view, "KeyboardShortcutsMenu")!).Name);
            Shortcut(view, Key.B); State(view).BrushSize = 1;
            Shortcut(view, Key.OemOpenBrackets); Assert.Equal(1, State(view).BrushSize);
            Shortcut(view, Key.OemCloseBrackets, KeyModifiers.Shift); Assert.Equal(6, State(view).BrushSize);
            State(view).BrushSize = 64; Shortcut(view, Key.OemCloseBrackets); Assert.Equal(64, State(view).BrushSize);
            Shortcut(view, Key.F1); _ = Capture(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), label => label.Text?.Contains("Shift+B") == true);
            Assert.False(Shortcut(view, Key.N, KeyModifiers.Alt).Handled);
            Shortcut(view, Key.Escape); window.Close();
        }, CancellationToken.None);
    }
    [Fact]
    public async Task EditingExistingTextStartsAtClickedLineRatherThanEnd()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var editor = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show();
            var obj = editor.CreateText(); obj.X = 20; obj.Y = 20; obj.Width = 200; obj.Height = 80;
            editor.Frame.TextObjects.Add(obj); editor.SelectedTextId = obj.Id; editor.Tool = Tool.Text;
            editor.Scene.Translations[editor.Language][obj.Key] = "hello\nworld";
            Invoke(view, "RefreshAll"); _ = Capture(window);
            Invoke(view, "BeginInlineTextEditAt", new Point(21, 23)); _ = Capture(window);
            var box = Named<TextBox>(window, "InlineTranslationText");
            Assert.True(box.IsFocused); Assert.InRange(box.CaretIndex, 0, 2);
            Assert.Equal(box.SelectionStart, box.SelectionEnd);
            var caret = box.CaretIndex; box.SelectedText = "X"; _ = Capture(window);
            Assert.Equal("hello\nworld".Insert(caret, "X"), editor.Scene.Text(editor.Language, obj.Key));
            Invoke(view, "EndInlineTextEdit");
            Assert.True(editor.Undo()); Assert.Equal("hello\nworld", editor.Scene.Text(editor.Language, obj.Key));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HostShortcutsWorkWithoutChildFocusAfterRebuildAndDetachCleanly()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var editor = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);
            void HostKey(Key key, KeyModifiers modifiers) => window.RaiseEvent(new KeyEventArgs
            { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window });
            HostKey(Key.N, KeyModifiers.Alt); Assert.Equal(2, editor.Scene.Frames.Count);
            var card = Named<Border>(window, "FrameRow1"); card.Focus();
            // A descendant event passes both host and view: it must execute once.
            card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Alt, Source = card });
            Assert.Equal(3, editor.Scene.Frames.Count);
            Invoke(view, "DuplicateSelectedFrames"); _ = Capture(window);
            var afterDuplicate = editor.Scene.Frames.Count;
            HostKey(Key.N, KeyModifiers.Alt); Assert.Equal(afterDuplicate + 1, editor.Scene.Frames.Count);
            // A text field still prevents frame/tool bindings at the host.
            var duration = Named<Button>(window, "FrameTimingDisplay0");
            Click(window, duration); _ = Capture(window);
            var timing = Named<TextBox>(window, "FrameTiming0");
            timing.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Alt, Source = timing });
            Assert.Equal(afterDuplicate + 1, editor.Scene.Frames.Count);
            window.Content = null;
            HostKey(Key.N, KeyModifiers.Alt); Assert.Equal(afterDuplicate + 1, editor.Scene.Frames.Count);
            window.Content = view; _ = Capture(window);
            HostKey(Key.N, KeyModifiers.Alt); Assert.Equal(afterDuplicate + 2, editor.Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawKeyboardShortcutsWorkAtStartupAndAfterFocusedRowsAreRebuilt(bool compact)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(compact);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); _ = Capture(window);
            // Use the headless platform's raw input path, not a direct handler
            // invocation or RaiseEvent: focus determines the actual event source.
            void Press(Key key, RawInputModifiers modifiers, PhysicalKey physical)
            {
                window.KeyPress(key, modifiers, physical, null);
                window.KeyRelease(key, modifiers, physical, null);
            }
            var editor = State(view);
            window.FocusManager!.Focus(null);
            Assert.Null(window.FocusManager.GetFocusedElement());
            Press(Key.N, RawInputModifiers.Alt, PhysicalKey.N);
            Assert.Equal(2, editor.Scene.Frames.Count);
            if (compact) Click(window, Named<Button>(window, "CompactFrames"));
            _ = Capture(window);
            Assert.True(Named<Border>(window, "FrameRow1").Focus());
            Press(Key.N, RawInputModifiers.Alt, PhysicalKey.N);
            Assert.Equal(3, editor.Scene.Frames.Count);
            Press(Key.N, RawInputModifiers.Alt, PhysicalKey.N);
            Assert.Equal(4, editor.Scene.Frames.Count);
            Press(Key.N, RawInputModifiers.Control, PhysicalKey.N);
            Assert.NotSame(editor, State(view));
            Assert.Single(State(view).Scene.Frames);
            Press(Key.N, RawInputModifiers.Alt, PhysicalKey.N);
            Assert.Equal(2, State(view).Scene.Frames.Count);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawCtrlSSavesOutsideTextFields(bool focusFrame)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "comic-shortcut-" + Guid.NewGuid().ToString("N") + ".ctsc");
            var view = new MainView(); var editor = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            try
            {
                var original = CutsceneFile.Write(editor.Scene);
                await File.WriteAllBytesAsync(path, original);
                editor.MarkSaved(original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                Assert.NotNull(file);
                await (Task)Invoke(view, "BindFile", file, original)!;
                editor.AddFrame(false);
                Assert.True(editor.FastDirty);
                Invoke(view, "RefreshAll"); _ = Capture(window);
                if (focusFrame) Assert.True(Named<Border>(window, "FrameRow1").Focus());
                else
                {
                    window.FocusManager!.Focus(null);
                    Assert.Null(window.FocusManager.GetFocusedElement());
                }
                window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, null);
                window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, null);
                // Wait for the real asynchronous file write, not just Handled=true.
                for (var i = 0; i < 100 && editor.FastDirty; i++) await Task.Delay(20);
                Assert.False(editor.FastDirty);
                Assert.Equal(2, CutsceneFile.Parse(await File.ReadAllBytesAsync(path)).Frames.Count);
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AltFrameChordDoesNotActivateMenuButBareAltStillDoes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); _ = Capture(window);
            void AltDown() => window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            void AltUp() => window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            for (var count = 2; count <= 3; count++)
            {
                AltDown();
                window.KeyPress(Key.N, RawInputModifiers.Alt, PhysicalKey.N, null);
                window.KeyRelease(Key.N, RawInputModifiers.Alt, PhysicalKey.N, null);
                AltUp();
                Assert.Equal(count, State(view).Scene.Frames.Count);
                Assert.IsNotType<MenuItem>(window.FocusManager!.GetFocusedElement());
                _ = Capture(window);
                Assert.True(Named<Border>(window, $"FrameRow{count - 1}").Focus());
            }
            AltDown(); AltUp();
            Assert.IsType<MenuItem>(window.FocusManager!.GetFocusedElement());
            window.Close();
        }, CancellationToken.None);
    }

}
