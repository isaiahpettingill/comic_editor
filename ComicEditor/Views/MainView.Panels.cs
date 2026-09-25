using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private StackPanel? renderedStoryboard;
    private Cutscene? thumbnailScene;
    private string[] thumbnailFrameIds = [];
    private readonly List<CutsceneCanvas> thumbnailCanvases = [];
    private readonly List<Border> thumbnailCards = [];
    private readonly HashSet<int> thumbnailPendingFrames = [];
    private DispatcherTimer? thumbnailTimer;
    private bool thumbnailRefreshAll;

    private void QueueThumbnailRefresh(bool all = false)
    {
        if (thumbnailCanvases.Count == 0) return;
        thumbnailRefreshAll |= all;
        if (!all) thumbnailPendingFrames.Add(editor.FrameIndex);
        thumbnailTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        thumbnailTimer.Tick -= RefreshQueuedThumbnails;
        thumbnailTimer.Tick += RefreshQueuedThumbnails;
        thumbnailTimer.Stop(); thumbnailTimer.Start();
    }

    private void RefreshQueuedThumbnails(object? sender, EventArgs args)
    {
        thumbnailTimer?.Stop();
        if (thumbnailRefreshAll)
            foreach (var thumb in thumbnailCanvases) thumb.InvalidateVisual();
        else
            foreach (var index in thumbnailPendingFrames)
                if (index >= 0 && index < thumbnailCanvases.Count) thumbnailCanvases[index].InvalidateVisual();
        thumbnailRefreshAll = false; thumbnailPendingFrames.Clear();
    }

    private void RefreshStoryboard()
    {
        if (storyboard is null) return;
        var ids = editor.Scene.Frames.Select(frame => frame.Id).ToArray();
        if (renderedStoryboard == storyboard && ids.SequenceEqual(thumbnailFrameIds))
        {
            var changedScene = !ReferenceEquals(thumbnailScene, editor.Scene);
            var changedLanguage = thumbnailCanvases.Count > 0 && thumbnailCanvases[0].Language != editor.Language;
            var changedDimensions = thumbnailCanvases.Count > 0 &&
                (thumbnailCanvases[0].Width != editor.Scene.Width || thumbnailCanvases[0].Height != editor.Scene.Height);
            thumbnailScene = editor.Scene;
            for (var i = 0; i < thumbnailCanvases.Count; i++)
            {
                thumbnailCanvases[i].Scene = editor.Scene;
                thumbnailCanvases[i].Language = editor.Language;
                thumbnailCanvases[i].Width = editor.Scene.Width;
                thumbnailCanvases[i].Height = editor.Scene.Height;
                thumbnailCards[i].Background = Brush(i == editor.FrameIndex ? UiTheme.Selection : UiTheme.Surface);
                thumbnailCards[i].BorderBrush = Brush(i == editor.FrameIndex ? UiTheme.Accent : UiTheme.Border);
                thumbnailCards[i].BorderThickness = new Thickness(i == editor.FrameIndex ? 2 : 1);
            }
            QueueThumbnailRefresh(changedScene || changedLanguage || changedDimensions);
            return;
        }
        renderedStoryboard = storyboard; thumbnailScene = editor.Scene; thumbnailFrameIds = ids;
        thumbnailPendingFrames.Clear(); thumbnailRefreshAll = false;
        thumbnailCanvases.Clear(); thumbnailCards.Clear();
        storyboard.Children.Clear();
        for (var i = 0; i < editor.Scene.Frames.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44"), Height = 64 };
            var thumbnail = new CutsceneCanvas
            {
                Scene = editor.Scene,
                FrameIndex = i,
                Language = editor.Language,
                Width = editor.Scene.Width,
                Height = editor.Scene.Height,
                IsHitTestVisible = false
            };
            thumbnailCanvases.Add(thumbnail);
            row.Children.Add(new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = thumbnail
            });
            var number = Label((i + 1).ToString("D3")); number.TextAlignment = TextAlignment.Center; AddAt(row, number, 1);
            var card = new Border
            {
                Name = $"FrameRow{i}",
                Child = row,
                Height = 74,
                Padding = new Thickness(4),
                Focusable = true,
                Background = Brush(i == editor.FrameIndex ? UiTheme.Selection : UiTheme.Surface),
                BorderBrush = Brush(i == editor.FrameIndex ? UiTheme.Accent : UiTheme.Border),
                BorderThickness = new Thickness(i == editor.FrameIndex ? 2 : 1)
            };
            ToolTip.SetTip(card, "Click to open; drag to reorder");
            thumbnailCards.Add(card);
            AttachReorder(card, storyboard, index, true, () => { SelectFrame(index); if (compact) ShowCompactPage(CompactPage.Draw); });
            storyboard.Children.Add(card);
        }
    }

    private void RefreshAfterArtworkEdit()
    {
        RefreshCanvas(); RefreshTitle(); QueueThumbnailRefresh();
    }

    private void RefreshPalette()
    {
        if (palette is null) return;
        if (compactColor is not null) compactColor.Background = Brush(editor.Scene.Palette[editor.Color]);
        palette.Children.Clear();
        var slot = new NumericUpDown { Name = "CurrentPaletteSlot", Minimum = 0, Maximum = editor.Scene.Palette.Count - 1, Value = editor.Color, Width = 100 };
        slot.ValueChanged += (_, _) => { if (slot.Value is null || (int)slot.Value == editor.Color) return; editor.Color = (int)slot.Value; RefreshPalette(); };
        var heading = Row(Label("Palette", true), slot, Label(editor.Scene.Palette[editor.Color]), Button("Edit palette…", EditPalette));
        palette.Children.Add(heading);
        var colors = new WrapPanel { Name = "PaletteSwatches", Orientation = Orientation.Horizontal };
        var first = editor.Color / 256 * 256;
        for (var i = first; i < Math.Min(editor.Scene.Palette.Count, first + 256); i++)
        {
            var index = i;
            var button = new Button
            {
                Name = "Swatch" + i,
                Width = compact ? 40 : 26,
                Height = compact ? 40 : 26,
                Background = Brush(editor.Scene.Palette[i]),
                BorderBrush = i == editor.Color ? Brush(UiTheme.Accent) : Brush(UiTheme.Border),
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
        refreshFontWarning = null;
        if (inspector is null) return;
        inspector.Children.Clear();
        inspector.Children.Add(Label("Layers", true));
        var rows = new StackPanel { Spacing = 1 };
        void LayerRow(string name, bool visible, bool selected, Action select, Action toggle, Action? rename, int? layerIndex = null)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "44,*" : "32,*") };
            var eye = Icon(visible ? PackIconMaterialKind.EyeOutline : PackIconMaterialKind.EyeOffOutline, visible ? "Hide " + name : "Show " + name, toggle);
            eye.Background = Brushes.Transparent; eye.BorderThickness = default; row.Children.Add(eye);
            if (layerIndex is int index)
            {
                var item = new Border { Child = Label(name), Height = compact ? 44 : 32, Padding = new Thickness(7, 4), Focusable = true, Background = Brushes.Transparent };
                ToolTip.SetTip(item, "Click to select; drag to reorder; double-click to rename");
                item.DoubleTapped += (_, _) => rename?.Invoke();
                var renameItem = new MenuItem { Header = "Rename…" }; renameItem.Click += (_, _) => rename?.Invoke();
                item.ContextMenu = new ContextMenu { Items = { renameItem } };
                AttachReorder(item, rows, index, false, select);
                AddAt(row, item, 1);
                rows.Children.Add(new Border { Name = $"LayerRow{index}", Child = row, Background = selected ? Brush(UiTheme.Selection) : Brush(UiTheme.Surface) });
            }
            else
            {
                var item = Button(name, select); item.HorizontalAlignment = HorizontalAlignment.Stretch;
                item.HorizontalContentAlignment = HorizontalAlignment.Left; item.Background = Brushes.Transparent; item.BorderThickness = default;
                AddAt(row, item, 1); row.Background = selected ? Brush(UiTheme.Selection) : Brush(UiTheme.Surface);
                rows.Children.Add(row);
            }
        }
        LayerRow($"Text ({editor.Frame.TextObjects.Count})", editor.Frame.TextVisible, editor.Tool == Tool.Text,
            () => { editor.Tool = Tool.Text; RefreshTools(); RefreshInspector(); },
            () => { editor.BeforeChange(); editor.Frame.TextVisible = !editor.Frame.TextVisible; RefreshAll(); }, null);
        for (var i = editor.Frame.Layers.Count - 1; i >= 0; i--)
        {
            var index = i; var layer = editor.Frame.Layers[i];
            LayerRow(layer.Name, layer.Visible, editor.Tool != Tool.Text && editor.LayerIndex == i,
                () =>
                {
                    if (editor.LayerIndex == index && editor.Tool != Tool.Text) return;
                    editor.LayerIndex = index; if (editor.Tool == Tool.Text) editor.Tool = Tool.Pixel;
                    RefreshTools(); RefreshInspector();
                },
                () => { editor.BeforeChange(); layer.Visible = !layer.Visible; RefreshAll(); }, () => RenameLayer(layer), index);
        }
        inspector.Children.Add(new ScrollViewer { Content = rows, MaxHeight = compact ? 180 : 140 });
        var layerActions = Row(Icon(PackIconMaterialKind.Plus, "Add artwork layer", () =>
            {
                editor.BeforeChange(); editor.Frame.Layers.Add(ArtworkLayer.Create($"Layer {editor.Frame.Layers.Count + 1}", editor.Scene.Width, editor.Scene.Height, editor.Scene.IsRgba));
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
            obj.Key = next; RefreshCanvas(); RefreshTitle(); QueueThumbnailRefresh();
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
            var placement = obj.Placement(lang, editor.Scene.FallbackLanguage);
            var text = translation.Text ?? "";
            var missing = string.IsNullOrWhiteSpace(text);
            var font = CutsceneFonts.Resolve(obj.FontId, lang);
            var measure = new FormattedText(text, CutsceneCanvas.Culture(lang), CutsceneCanvas.Culture(lang).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(font, obj.Italic ? FontStyle.Italic : FontStyle.Normal, obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, Brushes.Black)
            { MaxTextWidth = placement.Width };
            TextStyleFormatter.Apply(measure, obj, lang, text.Length);
            var overflow = measure.Height > placement.Height + .5 || measure.Width > placement.Width + .5;
            warning.Text = missing ? $"Missing — fallback: {editor.Scene.FallbackLanguage}" : overflow ? "Text overflows its area" : "Translation fits";
            if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId)) warning.Text += " · Custom font unavailable; using Noto";
            var missingFonts = CutsceneFonts.Missing(text, obj.FontId, lang);
            if (missingFonts.Count > 0) warning.Text += " · Missing font: " + string.Join(", ", missingFonts.Select(f => f.Family.Length > 0 ? f.Family : f.Sample));
            warning.Foreground = missing || overflow || missingFonts.Count > 0 ? Brush(UiTheme.Error) : Brush(UiTheme.Success);
        }
        translation.TextChanged += (_, _) =>
        {
            if (!editor.Scene.Translations.TryGetValue(lang, out var entries)) return;
            var next = translation.Text ?? "";
            if (editor.Scene.Text(lang, obj.Key) == next) return;
            if (!captured) { editor.BeforeChange(); captured = true; }
            editor.SetTranslation(lang, obj.Key, next); UpdateWarning(); RefreshCanvas(); RefreshTitle(); QueueThumbnailRefresh(); QueueFontCheck();
        };
        refreshFontWarning = UpdateWarning;
        UpdateWarning(); inspector.Children.Add(warning); inspector.Children.Add(translation);
        var edit = Button("Layout & style…", () => EditTextProperties(obj)); edit.Name = "EditTextProperties";
        var textActions = new WrapPanel { Orientation = Orientation.Horizontal };
        edit.Margin = new Thickness(0, 0, 4, 4); textActions.Children.Add(edit);
        textActions.Children.Add(Button("Edit on canvas", BeginInlineTextEdit));
        textActions.Children.Add(Button("Languages…", ManageLanguages)); inspector.Children.Add(textActions);
        var installFonts = Button("Install missing fonts…", () => _ = CheckFontsAsync(CancellationToken.None, explicitlyRequested: true));
        installFonts.Name = "InstallMissingFonts";
        inspector.Children.Add(installFonts);
    }

    private void MoveLayer(int delta)
    {
        if (editor.Tool == Tool.Text) return;
        var target = editor.LayerIndex + delta; if (target < 0 || target >= editor.Frame.Layers.Count) return;
        editor.ReorderLayer(editor.LayerIndex, target); RefreshAll();
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
