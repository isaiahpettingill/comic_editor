using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ComicEditor.Views;

public partial class MainView
{
    // Conventional drawing-tool keys. Shift cycles variants without a menu or long press.
    private static readonly (Key Key, string Group)[] ToolKeys =
    [
        (Key.B, "Brush"), (Key.E, "Eraser"), (Key.G, "Fill"), (Key.L, "Line"),
        (Key.U, "Shapes"), (Key.I, "Eyedropper"), (Key.T, "Text"), (Key.A, "Spray"),
        (Key.M, "Selection"), (Key.R, "Effects"), (Key.Z, "Zoom")
    ];

    private static string ToolShortcut(ToolGroup group)
    {
        var key = ToolKeys.Single(binding => binding.Group == group.Id).Key;
        return group.Tools.Length > 1 ? $"{key}; Shift+{key} next variant" : key.ToString();
    }

    private MenuItem KeyboardShortcutsMenu()
    {
        var item = new MenuItem { Header = "Keyboard shortcuts…", Name = "KeyboardShortcutsMenu", InputGesture = new KeyGesture(Key.F1) };
        item.Click += (_, _) => ShowKeyboardShortcuts();
        return item;
    }

    private void ShowKeyboardShortcuts()
    {
        var body = new StackPanel { Spacing = 8 };
        void Entry(string keys, string action) => body.Children.Add(Label($"{keys}  —  {action}"));
        Entry("Ctrl / Cmd + N, O, S", "New document, open, save (also while typing)");
        Entry("Ctrl / Cmd + Shift + S", "Save as");
        Entry("Ctrl / Cmd + P", "Print current frame");
        Entry("Ctrl / Cmd + Z, Shift + Z / Y", "Undo, redo");
        Entry("Ctrl / Cmd + X, C, V, A", "Cut, copy, paste, select all (text keeps its own shortcuts)");
        Entry("Ctrl / Cmd + Shift + A", "Deselect artwork");
        Entry("Alt + N / D / Delete", "New / duplicate selected frame(s) / delete selected frame(s)");
        Entry("Delete", "Delete focused items or selected artwork (undoable)");
        Entry("Alt + Left / Right", "Previous / next frame");
        Entry("Alt + Up / Down", "Move selected frame(s) earlier / later");
        Entry("Ctrl / Cmd + Shift + C / V", "Copy frame / paste frame");
        Entry("Alt + T", "Frame timing and visibility");
        Entry("F5", "Preview cutscene");
        Entry("F2", "Edit selected text or rename current artwork layer");
        Entry("Ctrl / Cmd + Shift + . / ,", "Larger / smaller text or selected text span");
        Entry("Ctrl + Enter / Escape while editing text", "Finish editing and keep text");
        Entry("F4 / F6 / F7", "Tool options / palette / current color");
        Entry("[ / ]", "Smaller / larger brush (Shift: five pixels)");
        Entry("Ctrl / Cmd + 0 / 1 / − / +", "Fit / actual pixels / zoom out / in");
        Entry("Enter / Escape", "Finish / cancel a path; Escape clears artwork selection");
        foreach (var group in ToolGroups) Entry(ToolShortcut(group), string.Join(" → ", group.Tools.Select(ToolName)));
        Entry("Shift + tool key", "Cycle to the next variant; plain key recalls the last variant");
        ShowModal("Keyboard shortcuts", new ScrollViewer { Content = body, MaxHeight = 560 });
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Escape && draggingPane is not null) { CancelPaneDrag(); e.Handled = true; return; }
        if (modal is not null) { if (e.Key == Key.Escape) { CloseModal(); e.Handled = true; } return; }
        var typing = e.Source is Visual visual && (visual is TextBox or NumericUpDown || visual.GetVisualAncestors().Any(v => v is TextBox or NumericUpDown));
        var control = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Ctrl+Alt is also AltGr on many keyboard layouts: never consume typed characters.
        if (control && alt) return;
        if (!typing && !alt && NavigateFocusedItems(e)) { e.Handled = true; return; }
        if (!typing && editor.SelectedText is not null && FontStep(e, out var fontDelta))
        { BeginInlineTextEdit(); ChangeInlineFontSize(fontDelta); e.Handled = true; return; }
        if (control)
        {
            if (e.Key == Key.S) { _ = shift ? SaveAs() : Save(); e.Handled = true; }
            else if (!shift && e.Key == Key.P) { _ = PrintCurrentFrame(); e.Handled = true; }
            else if (!shift && e.Key == Key.O) { _ = Open(); e.Handled = true; }
            else if (!shift && e.Key == Key.N) { New(); e.Handled = true; }
            else if (!typing)
            {
                if (e.Key == Key.Z) { if (shift) Redo(); else Undo(); }
                else if (e.Key == Key.Y) Redo();
                else if (e.Key == Key.C) { if (FocusedFrameIndex(e.Source) is int index) CopyFrame(index); else if (shift) CopyFrame(); else CopySelection(false); }
                else if (e.Key == Key.X) { if (!CutFocusedFrames(e.Source)) CopySelection(true); }
                else if (e.Key == Key.V) { if (shift) PasteFrame(); else PasteClipboard(); }
                else if (e.Key == Key.A) { if (shift) Deselect(); else if (!SelectAllFocusedItems(e.Source)) SelectAllArtwork(); }
                else if (e.Key == Key.D0) { zoom = 0; RefreshCanvas(); RefreshTools(); }
                else if (e.Key == Key.D1) { zoom = 1; RefreshCanvas(); RefreshTools(); }
                else if (e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
                {
                    if (canvasScroll is not null) ZoomAt(CanvasScale * (e.Key is Key.OemMinus or Key.Subtract ? .8 : 1.25), new Point(canvasScroll.Bounds.Width / 2, canvasScroll.Bounds.Height / 2));
                    RefreshTools();
                }
                else return;
                e.Handled = true;
            }
            return;
        }
        if (typing) return;
        // Do not steal type-to-search or navigation from an open list or menu.
        if (e.Source is Visual controlSource && new[] { controlSource }.Concat(controlSource.GetVisualAncestors()).Any(v => v is ComboBox or MenuItem)) return;
        if (alt)
        {
            if (shift) return;
            switch (e.Key)
            {
                case Key.N: FinishPath(); editor.AddFrame(false); RefreshAll(); break;
                case Key.D: FinishPath(); DuplicateSelectedFrames(); break;
                case Key.Delete: FinishPath(); DeleteFrames(editor.Frame); break;
                case Key.Left: SelectFrame(editor.FrameIndex - 1); break;
                case Key.Right: SelectFrame(editor.FrameIndex + 1); break;
                case Key.Up: FinishPath(); MoveFrames(editor.Frame, -1); break;
                case Key.Down: FinishPath(); MoveFrames(editor.Frame, 1); break;
                case Key.T: FrameSettings(editor.FrameIndex); break;
                default: return;
            }
            e.Handled = true; return;
        }
        if (e.Key == Key.Escape) { FinishPath(cancel: true); selection = null; RefreshCanvas(); e.Handled = true; return; }
        if (e.Key == Key.Enter && pathBase is not null) { FinishPath(); e.Handled = true; return; }
        if (e.Key == Key.Delete && DeleteFocusedItems(e.Source)) { e.Handled = true; return; }
        if (e.Key == Key.Delete && selection is not null) { DeleteSelection(); e.Handled = true; return; }
        if (e.Key == Key.Delete && editor.SelectedText is { } selected)
        {
            editor.BeforeChange(); editor.Frame.TextObjects.Remove(selected); editor.SelectedTextId = null;
            RefreshAll(); e.Handled = true; return;
        }
        var binding = ToolKeys.FirstOrDefault(binding => binding.Key == e.Key);
        if (binding.Group is not null)
        {
            if (dragging) return; // Finish the active pointer stroke before changing tools.
            var group = ToolGroups.Single(group => group.Id == binding.Group);
            var remembered = RememberedTool(group);
            ChooseTool(shift ? group.Tools[(Array.IndexOf(group.Tools, remembered) + 1) % group.Tools.Length] : remembered);
            e.Handled = true; return;
        }
        switch (e.Key)
        {
            case Key.F1: ShowKeyboardShortcuts(); break;
            case Key.F2: if (editor.SelectedText is not null) BeginInlineTextEdit(); else RenameLayer(editor.Layer); break;
            case Key.F4: EditToolOptions(); break;
            case Key.F5: PreviewPlayback(); break;
            case Key.F6: EditPalette(); break;
            case Key.F7: EditPaletteColor(); break;
            case Key.OemOpenBrackets:
            case Key.OemCloseBrackets:
                if (!UsesSize(editor.Tool)) return;
                editor.BrushSize = Math.Clamp(editor.BrushSize + (e.Key == Key.OemOpenBrackets ? -1 : 1) * (shift ? 5 : 1), 1, 64);
                RefreshTools(); break;
            case Key.Left when e.Source == canvas: SelectFrame(editor.FrameIndex - 1); break;
            case Key.Right when e.Source == canvas: SelectFrame(editor.FrameIndex + 1); break;
            default: return;
        }
        e.Handled = true;
    }
}
