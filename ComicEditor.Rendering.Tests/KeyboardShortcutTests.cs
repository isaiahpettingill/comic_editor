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

}
