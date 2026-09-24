using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private void RefreshStoryboard()
    {
        if (storyboard is null) return;
        storyboard.Children.Clear();
        for (var i = 0; i < editor.Scene.Frames.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44"), Height = 64 };
            row.Children.Add(new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = new CutsceneCanvas
                {
                    Scene = editor.Scene,
                    FrameIndex = i,
                    Language = editor.Language,
                    Width = editor.Scene.Width,
                    Height = editor.Scene.Height,
                    IsHitTestVisible = false
                }
            });
            var number = Label((i + 1).ToString("D3")); number.TextAlignment = TextAlignment.Center; AddAt(row, number, 1);
            var button = Button("", () => { SelectFrame(index); if (compact) ShowCompactPage(CompactPage.Draw); }); button.Content = row; button.Height = 74;
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(4);
            if (i == editor.FrameIndex) { button.BorderBrush = Brush("#147BC1"); button.Background = Brush("#D7EAF8"); }
            storyboard.Children.Add(button);
        }
    }

    private void RefreshPalette()
    {
        if (palette is null) return;
        if (compactColor is not null) compactColor.Background = Brush(editor.Scene.Palette[editor.Color]);
        palette.Children.Clear();
        var heading = Row(Label("Palette", true), Label($"{editor.Color:D3}  {editor.Scene.Palette[editor.Color]}"), Button("Edit color…", EditPaletteColor));
        palette.Children.Add(heading);
        var colors = new WrapPanel { Name = "PaletteSwatches", Orientation = Orientation.Horizontal };
        for (var i = 0; i < editor.Scene.Palette.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Name = "Swatch" + i,
                Width = compact ? 40 : 26,
                Height = compact ? 40 : 26,
                Background = Brush(editor.Scene.Palette[i]),
                BorderBrush = i == editor.Color ? Brushes.DodgerBlue : Brush("#777B80"),
                BorderThickness = new Thickness(i == editor.Color ? 3 : 1),
                Margin = new Thickness(1),
                Padding = default
            };
            ToolTip.SetTip(button, $"{i:D3}  {editor.Scene.Palette[i]} — right click to edit");
            button.Click += (_, _) => { editor.Color = index; RefreshPalette(); };
            button.DoubleTapped += (_, _) => { editor.Color = index; EditPaletteColor(); };
            var edit = new MenuItem { Header = "Edit color…" }; edit.Click += (_, _) => { editor.Color = index; EditPaletteColor(); };
            button.ContextMenu = new ContextMenu { Items = { edit } }; colors.Children.Add(button);
        }
        palette.Children.Add(colors);
    }

    private void RefreshInspector()
    {
        if (inspector is null) return;
        inspector.Children.Clear();
        inspector.Children.Add(Label("Layers", true));
        var rows = new StackPanel { Spacing = 1 };
        void LayerRow(string name, bool visible, bool selected, Action select, Action toggle, Action? rename)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "44,*" : "32,*"), Background = selected ? Brush("#D7EAF8") : Brush("#E2E3E3") };
            var eye = Icon(visible ? PackIconMaterialKind.EyeOutline : PackIconMaterialKind.EyeOffOutline, visible ? "Hide " + name : "Show " + name, toggle);
            eye.Background = Brushes.Transparent; eye.BorderThickness = default; row.Children.Add(eye);
            var item = Button(name, select); item.HorizontalAlignment = HorizontalAlignment.Stretch;
            item.HorizontalContentAlignment = HorizontalAlignment.Left; item.Background = Brushes.Transparent; item.BorderThickness = default;
            if (rename is not null)
            {
                item.DoubleTapped += (_, _) => rename();
                var renameItem = new MenuItem { Header = "Rename…" }; renameItem.Click += (_, _) => rename();
                item.ContextMenu = new ContextMenu { Items = { renameItem } };
            }
            AddAt(row, item, 1); rows.Children.Add(row);
        }
        LayerRow($"Text ({editor.Frame.TextObjects.Count})", editor.Frame.TextVisible, editor.Tool == Tool.Text,
            () => { editor.Tool = Tool.Text; RefreshTools(); RefreshInspector(); },
            () => { editor.BeforeChange(); editor.Frame.TextVisible = !editor.Frame.TextVisible; RefreshAll(); }, null);
        for (var i = editor.Frame.Layers.Count - 1; i >= 0; i--)
        {
            var index = i; var layer = editor.Frame.Layers[i];
            LayerRow(layer.Name, layer.Visible, editor.Tool != Tool.Text && editor.LayerIndex == i,
                () => { editor.LayerIndex = index; if (editor.Tool == Tool.Text) editor.Tool = Tool.Pixel; RefreshTools(); RefreshInspector(); },
                () => { editor.BeforeChange(); layer.Visible = !layer.Visible; RefreshAll(); }, () => RenameLayer(layer));
        }
        inspector.Children.Add(new ScrollViewer { Content = rows, MaxHeight = compact ? 180 : 140 });
        var layerActions = Row(Icon(PackIconMaterialKind.Plus, "Add artwork layer", () =>
            {
                editor.BeforeChange(); editor.Frame.Layers.Add(ArtworkLayer.Create($"Layer {editor.Frame.Layers.Count + 1}", editor.Scene.Width, editor.Scene.Height));
                editor.LayerIndex = editor.Frame.Layers.Count - 1; editor.Tool = Tool.Pixel; RefreshTools(); RefreshAll();
            }),
            Icon(PackIconMaterialKind.DeleteOutline, "Delete selected artwork layer", () =>
            {
                if (editor.Frame.Layers.Count <= 1 || editor.Tool == Tool.Text) return; editor.BeforeChange(); editor.Frame.Layers.RemoveAt(editor.LayerIndex);
                editor.LayerIndex = Math.Max(0, editor.LayerIndex - 1); RefreshAll();
            }),
            Icon(PackIconMaterialKind.ArrowUp, "Raise artwork layer", () => MoveLayer(1)),
            Icon(PackIconMaterialKind.ArrowDown, "Lower artwork layer", () => MoveLayer(-1)),
            Icon(PackIconMaterialKind.Pencil, "Rename artwork layer", () => RenameLayer(editor.Layer)));
        layerActions.Children[1].IsEnabled = editor.Tool != Tool.Text && editor.Frame.Layers.Count > 1;
        layerActions.Children[2].IsEnabled = editor.Tool != Tool.Text && editor.LayerIndex < editor.Frame.Layers.Count - 1;
        layerActions.Children[3].IsEnabled = editor.Tool != Tool.Text && editor.LayerIndex > 0;
        layerActions.Children[4].IsEnabled = editor.Tool != Tool.Text;
        inspector.Children.Add(layerActions); inspector.Children.Add(new Separator());

        var textHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        textHeader.Children.Add(Label("Text & localization", true));
        AddAt(textHeader, Icon(PackIconMaterialKind.Plus, "Add text object", AddText), 1);
        var obj = editor.SelectedText;
        var remove = Icon(PackIconMaterialKind.DeleteOutline, "Delete selected text object", () =>
        { if (obj is null) return; editor.BeforeChange(); editor.Frame.TextObjects.Remove(obj); editor.SelectedTextId = null; RefreshAll(); });
        remove.IsEnabled = obj is not null; AddAt(textHeader, remove, 2); inspector.Children.Add(textHeader);
        if (editor.Frame.TextObjects.Count > 0)
        {
            var objects = editor.Frame.TextObjects.ToArray();
            var selector = new ComboBox
            {
                Name = "TextObjectSelector",
                ItemsSource = objects.Select(t => t.Key).ToArray(),
                SelectedIndex = Array.FindIndex(objects, t => t.Id == editor.SelectedTextId),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "Select text object…",
                Height = compact ? 44 : 32,
                MinHeight = compact ? 44 : 32
            };
            selector.SelectionChanged += (_, _) =>
            {
                if (selector.SelectedIndex < 0) return; editor.SelectedTextId = objects[selector.SelectedIndex].Id;
                editor.Tool = Tool.Text; RefreshTools(); RefreshInspector(); RefreshCanvas();
            };
            inspector.Children.Add(selector);
        }
        if (obj is null)
        {
            inspector.Children.Add(Label("Choose Text and drag a text area on the canvas. Click an existing object to select it."));
            inspector.Children.Add(Button("Manage languages…", ManageLanguages)); return;
        }
        var keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var keyLabel = Label("Key"); keyLabel.Margin = new Thickness(0, 0, 8, 0); keyRow.Children.Add(keyLabel);
        var key = new TextBox { Name = "LocalizationKey", Text = obj.Key, Height = compact ? 44 : 32, MinHeight = compact ? 44 : 32 };
        ToolTip.SetTip(key, "Stable localization key. Use an existing key to share translations.");
        key.LostFocus += (_, _) =>
        {
            var next = key.Text?.Trim() ?? "";
            if (next.Length == 0) { key.Text = obj.Key; return; }
            if (next == obj.Key) return;
            editor.BeforeChange();
            // Renaming this object preserves other objects sharing its old key.
            foreach (var entries in editor.Scene.Translations.Values)
                if (!entries.ContainsKey(next) && entries.TryGetValue(obj.Key, out var value)) entries[next] = value;
            obj.Key = next; RefreshCanvas(); RefreshTitle();
        };
        AddAt(keyRow, key, 1); inspector.Children.Add(keyRow);
        var language = new ComboBox
        {
            Name = "TranslationLanguage",
            ItemsSource = editor.Scene.Translations.Keys.Order().ToArray(),
            SelectedItem = editor.Language,
            Height = compact ? 44 : 32,
            MinHeight = compact ? 44 : 32,
            MinWidth = 90
        };
        language.SelectionChanged += (_, _) => { if (language.SelectedItem is string code && code != editor.Language) { editor.Language = code; RefreshAll(); } };
        inspector.Children.Add(Row(Label("Language"), language));
        var warning = Label(""); warning.Name = "TranslationStatus";
        var translation = new TextBox
        {
            Name = "TranslationText",
            Text = editor.Scene.Text(editor.Language, obj.Key),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            PlaceholderText = "Enter this language’s dialogue…"
        };
        var lang = editor.Language; var captured = false;
        translation.GotFocus += (_, _) => captured = false;
        void UpdateWarning()
        {
            var text = translation.Text ?? "";
            var missing = string.IsNullOrWhiteSpace(text);
            var font = CutsceneFonts.Resolve(obj.FontId);
            var measure = new FormattedText(text, CutsceneCanvas.Culture(lang), CutsceneCanvas.Culture(lang).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(font, obj.Italic ? FontStyle.Italic : FontStyle.Normal, obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, Brushes.Black)
            { MaxTextWidth = obj.Width };
            var overflow = measure.Height > obj.Height + .5 || measure.Width > obj.Width + .5;
            warning.Text = missing ? $"Missing — fallback: {editor.Scene.FallbackLanguage}" : overflow ? "Text overflows its area" : "Translation fits";
            if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId)) warning.Text += " · Custom font unavailable; using Noto";
            warning.Foreground = missing || overflow ? Brush("#AB3B13") : Brush("#406543");
        }
        translation.TextChanged += (_, _) =>
        {
            if (!editor.Scene.Translations.TryGetValue(lang, out var entries)) return;
            var next = translation.Text ?? "";
            if (editor.Scene.Text(lang, obj.Key) == next) return;
            if (!captured) { editor.BeforeChange(); captured = true; }
            entries[obj.Key] = next; UpdateWarning(); RefreshCanvas(); RefreshTitle();
        };
        UpdateWarning(); inspector.Children.Add(warning); inspector.Children.Add(translation);
        var edit = Button("Layout & style…", () => EditTextProperties(obj)); edit.Name = "EditTextProperties";
        var textActions = new WrapPanel { Orientation = Orientation.Horizontal };
        edit.Margin = new Thickness(0, 0, 4, 4); textActions.Children.Add(edit);
        textActions.Children.Add(Button("Languages…", ManageLanguages)); inspector.Children.Add(textActions);
    }

    private void MoveLayer(int delta)
    {
        if (editor.Tool == Tool.Text) return;
        var target = editor.LayerIndex + delta; if (target < 0 || target >= editor.Frame.Layers.Count) return;
        editor.BeforeChange(); var layer = editor.Layer; editor.Frame.Layers.RemoveAt(editor.LayerIndex);
        editor.Frame.Layers.Insert(target, layer); editor.LayerIndex = target; RefreshAll();
    }

    private void AddText()
    {
        editor.BeforeChange(); var obj = editor.CreateText();
        editor.Frame.TextObjects.Add(obj); editor.SelectedTextId = obj.Id; editor.Tool = Tool.Text; RefreshAll(); RefreshTools();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var field = inspector?.Children.OfType<Grid>().SelectMany(g => g.Children).OfType<TextBox>().FirstOrDefault(t => t.Name == "LocalizationKey");
            field?.Focus(); field?.SelectAll();
        });
    }
}
