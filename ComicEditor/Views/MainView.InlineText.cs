using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ComicEditor.Rendering;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private Grid? inlineTextLayer;
    private TextBox? inlineTextBox;
    private string? inlineTextObjectId;
    private string? inlineLanguage;
    private bool inlineTextCaptured;
    private Action? inlineCommit;
    private int inlineSelectionStart, inlineSelectionEnd;

    private void BeginInlineTextEdit()
    {
        var obj = editor.SelectedText;
        if (obj is null || inlineTextLayer is null) return;
        EndInlineTextEdit();
        inlineTextObjectId = obj.Id; inlineLanguage = editor.Language; inlineTextCaptured = false;
        inlineSelectionStart = inlineSelectionEnd = 0;
        var language = editor.Language; var frame = editor.Frame;
        var placement = obj.Placement(language, editor.Scene.FallbackLanguage);
        var color = ComicEditor.Format.RgbaColor.Parse(editor.Scene.Palette[obj.Color]);
        var box = new TextBox
        {
            Name = "InlineTranslationText",
            Text = editor.Scene.Text(language, obj.Key),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Width = placement.Width,
            Height = placement.Height,
            FontFamily = CutsceneFonts.Resolve(obj.FontId, language),
            FontSize = obj.FontSize,
            FontWeight = obj.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = obj.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8))),
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(placement.X, placement.Y, 0, 0)
        };
        inlineTextBox = box;
        void Commit()
        {
            if (inlineTextBox != box || !ReferenceEquals(frame, editor.Frame) || inlineTextObjectId != obj.Id) return;
            var next = box.Text ?? "";
            if (editor.Scene.Text(language, obj.Key) == next) return;
            if (!inlineTextCaptured) { editor.BeforeChange(); inlineTextCaptured = true; }
            editor.SetTranslation(language, obj.Key, next);
            canvas?.InvalidateVisual(); RefreshTitle();
        }
        inlineCommit = Commit;
        box.TextChanged += (_, _) => Commit();
        box.PropertyChanged += (_, e) =>
        {
            if (!box.IsFocused || e.Property != TextBox.SelectionStartProperty && e.Property != TextBox.SelectionEndProperty) return;
            inlineSelectionStart = box.SelectionStart; inlineSelectionEnd = box.SelectionEnd;
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { EndInlineTextEdit(); e.Handled = true; }
            else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { EndInlineTextEdit(); e.Handled = true; }
            else if (FontStep(e, out var delta)) { ChangeInlineFontSize(delta); e.Handled = true; }
        };
        inlineTextLayer.Children.Add(box);
        if (compact) ShowCompactPage(CompactPage.Draw);
        RefreshInlineTextToolbar();
        box.Focus(); box.CaretIndex = box.Text?.Length ?? 0;
    }

    private void EndInlineTextEdit()
    {
        var box = inlineTextBox;
        if (box is null) return;
        inlineCommit?.Invoke();
        inlineCommit = null;
        inlineTextBox = null; inlineTextObjectId = null; inlineLanguage = null; inlineTextCaptured = false;
        inlineTextLayer?.Children.Remove(box);
        RefreshInspector(); RefreshCanvas(); RefreshTools();
    }

    private static bool FontStep(KeyEventArgs e, out int delta)
    {
        delta = 0;
        if (!(e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) || !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return false;
        if (e.Key == Key.OemPeriod) delta = 1;
        else if (e.Key == Key.OemComma) delta = -1;
        return delta != 0;
    }

    private void BeginInlineChange()
    {
        if (inlineTextCaptured) return;
        editor.BeforeChange(); inlineTextCaptured = true;
    }

    private void ChangeInlineFontSize(int delta)
    {
        var box = inlineTextBox; var obj = editor.SelectedText;
        if (box is null || obj is null) return;
        inlineCommit?.Invoke();
        var start = Math.Min(inlineSelectionStart, inlineSelectionEnd);
        var length = Math.Abs(inlineSelectionEnd - inlineSelectionStart);
        BeginInlineChange();
        if (length > 0)
        {
            obj.ChangeStyle(editor.Language, box.Text ?? "", start, length, sizeDelta: delta);
            editor.Preferences.FontSize = obj.StyleAt(editor.Language, box.Text ?? "", start).Size;
        }
        else
        {
            obj.FontSize = Math.Clamp(obj.FontSize + delta, 1, 2048);
            box.FontSize = obj.FontSize; editor.Preferences.FontSize = obj.FontSize;
        }
        editor.Preferences.Save(); canvas?.InvalidateVisual(); RefreshInlineTextToolbar(); RefreshInspector(); RefreshTitle();
    }

    private void ChangeInlineFont(string fontId)
    {
        var box = inlineTextBox; var obj = editor.SelectedText;
        if (box is null || obj is null) return;
        inlineCommit?.Invoke();
        var start = Math.Min(inlineSelectionStart, inlineSelectionEnd);
        var length = Math.Abs(inlineSelectionEnd - inlineSelectionStart);
        BeginInlineChange();
        if (length > 0) obj.ChangeStyle(editor.Language, box.Text ?? "", start, length, fontId: fontId);
        else { obj.FontId = fontId; box.FontFamily = CutsceneFonts.Resolve(fontId, editor.Language); }
        editor.Preferences.FontId = fontId; editor.Preferences.Save();
        canvas?.InvalidateVisual(); RefreshInspector(); RefreshTitle();
    }

    private void RefreshInlineTextToolbar()
    {
        if (toolOptions is null || inlineTextBox is null || editor.SelectedText is not { } obj) return;
        toolOptions.Children.Clear(); toolOptions.Spacing = 4;
        var box = inlineTextBox;
        var start = Math.Min(inlineSelectionStart, inlineSelectionEnd);
        var style = obj.StyleAt(editor.Language, box.Text ?? "", start);
        var shrink = Button("A−", () => ChangeInlineFontSize(-1)); shrink.Name = "InlineFontSmaller"; shrink.Width = 38;
        var grow = Button("A+", () => ChangeInlineFontSize(1)); grow.Name = "InlineFontLarger"; grow.Width = 38;
        toolOptions.Children.Add(shrink);
        toolOptions.Children.Add(Label($"{style.Size:0}px"));
        toolOptions.Children.Add(grow);
        var ids = CutsceneFonts.Ids.Concat(CutsceneFonts.ImportedIds).Append(obj.FontId).Concat(obj.Styles.Select(s => s.FontId)).Distinct().ToArray();
        var fonts = new ComboBox
        {
            Name = "InlineFontFamily",
            ItemsSource = ids.Select(CutsceneFonts.NameFor).ToArray(),
            SelectedIndex = Array.IndexOf(ids, style.FontId),
            Width = compact ? 130 : 170,
            Height = compact ? 44 : 32
        };
        fonts.SelectionChanged += (_, _) => { if (fonts.SelectedIndex >= 0 && ids[fonts.SelectedIndex] != style.FontId) ChangeInlineFont(ids[fonts.SelectedIndex]); };
        toolOptions.Children.Add(fonts);
        var more = Button("Style…", () => { if (editor.SelectedText is { } text) EditTextProperties(text); });
        more.Name = "InlineTextStyle"; toolOptions.Children.Add(more);
        toolOptions.Children.Add(Button("Done", EndInlineTextEdit));
    }
}
