using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
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
    private readonly List<(TextBox Timing, Button Readout, TextBlock Condition)> frameBadges = [];

    private void RefreshFrameBadge(int index)
    {
        var frame = editor.Scene.Frames[index]; var badge = frameBadges[index];
        if (!badge.Timing.IsKeyboardFocusWithin) badge.Timing.Text = (frame.DurationMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        badge.Readout.Content = (frame.DurationMs / 1000m).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s";
        badge.Condition.Text = (frame.AudioQoa.Length > 0 ? "♫ " : "") + (frame.Requirement switch { "always" => "✓", "never" => "⊘", _ when frame.Requirement.StartsWith("not ", StringComparison.Ordinal) => "!" + frame.Requirement[4..], _ => frame.Requirement });
        ToolTip.SetTip(badge.Timing, $"Duration in seconds (minimum {frame.MinimumDurationMs / 1000.0:0.###}s). Enter to save; Escape to cancel.");
        ToolTip.SetTip(badge.Condition, "Show: " + frame.Requirement + (frame.AudioQoa.Length > 0 ? $" · QOA audio: {frame.MinimumDurationMs} ms" : ""));
    }

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
        SyncItemSelection();
        var ids = editor.Scene.Frames.Select(frame => frame.Id).ToArray();
        if (renderedStoryboard == storyboard && ReferenceEquals(thumbnailScene, editor.Scene) && ids.SequenceEqual(thumbnailFrameIds))
        {
            var changedScene = !ReferenceEquals(thumbnailScene, editor.Scene);
            var changedLanguage = thumbnailCanvases.Count > 0 && thumbnailCanvases[0].Language != editor.Language;
            var changedDimensions = thumbnailCanvases.Count > 0 &&
                (thumbnailCanvases[0].Width != editor.Scene.Width || thumbnailCanvases[0].Height != editor.Scene.Height);
            thumbnailScene = editor.Scene;
            for (var i = 0; i < thumbnailCanvases.Count; i++)
            {
                RefreshFrameBadge(i);
                thumbnailCanvases[i].Scene = editor.Scene;
                thumbnailCanvases[i].Language = editor.Language;
                thumbnailCanvases[i].Width = editor.Scene.Width;
                thumbnailCanvases[i].Height = editor.Scene.Height;
                thumbnailCards[i].Background = Brush(selectedFrameIds.Contains(editor.Scene.Frames[i].Id) ? UiTheme.Selection : UiTheme.Surface);
                thumbnailCards[i].BorderBrush = Brush(i == editor.FrameIndex ? UiTheme.Accent : UiTheme.Border);
            }
            QueueThumbnailRefresh(changedScene || changedLanguage || changedDimensions);
            return;
        }
        renderedStoryboard = storyboard; thumbnailScene = editor.Scene; thumbnailFrameIds = ids;
        thumbnailPendingFrames.Clear(); thumbnailRefreshAll = false;
        thumbnailCanvases.Clear(); thumbnailCards.Clear(); frameBadges.Clear();
        storyboard.Children.Clear();
        for (var i = 0; i < editor.Scene.Frames.Count; i++)
        {
            var index = i;
            // Let the artwork use the pane width. A fixed metadata column made
            // previews only a few dozen pixels wide in the default storyboard.
            var row = new StackPanel { Spacing = 4 };
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
                Name = $"FramePreview{i}",
                Stretch = Stretch.Uniform,
                MaxHeight = compact ? 260 : storyboard.Orientation == Orientation.Horizontal ? 120 : 220,
                Child = thumbnail
            });
            var number = Label((i + 1).ToString("D3"), true); number.FontSize = 12;
            var timing = InlineFrameDuration(i);
            var condition = Label(""); condition.Name = $"FrameRequirement{i}"; condition.FontSize = 11; condition.TextWrapping = TextWrapping.NoWrap; condition.TextTrimming = TextTrimming.CharacterEllipsis;
            frameBadges.Add((timing.Children.OfType<TextBox>().Single(), timing.Children.OfType<Button>().Single(), condition)); RefreshFrameBadge(i);
            var details = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            timing.Margin = new Thickness(3, 0, 0, 0);
            condition.Margin = new Thickness(3, 0);
            AddAt(details, number); AddAt(details, timing, 1); AddAt(details, condition, 2); AddAt(details, FrameOptions(index), 3);
            row.Children.Add(details);
            var card = new Border
            {
                Name = $"FrameRow{i}",
                Width = storyboard.Orientation == Orientation.Horizontal ? 200 : double.NaN,
                Child = row,
                Padding = new Thickness(4),
                Focusable = true,
                Background = Brush(selectedFrameIds.Contains(editor.Scene.Frames[i].Id) ? UiTheme.Selection : UiTheme.Surface),
                BorderBrush = Brush(i == editor.FrameIndex ? UiTheme.Accent : UiTheme.Border),
                // Reserve the selection border so selecting a frame never
                // resizes its preview or shifts the rows below it.
                BorderThickness = new Thickness(2)
            };
            ToolTip.SetTip(card, "Click to open; Ctrl-click to toggle; Shift-click for a range; drag to reorder");
            thumbnailCards.Add(card);
            AttachFrameClipboardMenu(card, index);
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
        var heading = new WrapPanel { Orientation = Orientation.Horizontal };
        if (compact) heading.Children.Add(Label("Palette", true));
        foreach (var control in new Control[] { slot, Label(editor.Scene.Palette[editor.Color]), Button("Edit palette…", EditPalette) })
        { control.Margin = new Thickness(0, 0, 4, 3); heading.Children.Add(control); }
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
        SyncItemSelection();
        inspector.Children.Clear();
        inspector.Children.Add(Label("Layers", true));
        var rows = new StackPanel { Spacing = 1 };
        void LayerRow(string name, bool visible, bool selected, Action select, Action toggle, Action? rename, int? layerIndex = null)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "44,*,44" : "32,*,32") };
            var eye = Icon(visible ? PackIconMaterialKind.EyeOutline : PackIconMaterialKind.EyeOffOutline, visible ? "Hide " + name : "Show " + name, toggle);
            eye.Background = Brushes.Transparent; eye.BorderThickness = default; row.Children.Add(eye);
            if (layerIndex is int index)
            {
                var item = new Border { Name = $"LayerItem{index}", Child = Label(name), Height = compact ? 44 : 32, Padding = new Thickness(7, 4), Focusable = true, Background = Brushes.Transparent };
                ToolTip.SetTip(item, "Click to select; Ctrl-click to toggle; Shift-click for a range; drag to reorder; double-click to rename");
                item.DoubleTapped += (_, _) => rename?.Invoke();
                var options = LayerOptions(index);
                item.ContextMenu = options.ContextMenu;
                AddAt(row, options, 2);
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
            LayerRow(layer.Name, layer.Visible, selectedLayerIds.Contains(layer.Id),
                () =>
                {
                    if (editor.LayerIndex == index && editor.Tool != Tool.Text) return;
                    editor.LayerIndex = index; if (editor.Tool == Tool.Text) editor.Tool = Tool.Pixel;
                    RefreshTools(); RefreshInspector();
                },
                () => { editor.BeforeChange(); layer.Visible = !layer.Visible; RefreshAll(); }, () => RenameLayer(layer), index);
        }
        inspector.Children.Add(new ScrollViewer { Content = rows, MaxHeight = compact ? 180 : 140 });
        inspector.Children.Add(Icon(PackIconMaterialKind.Plus, "Add artwork layer", () =>
        {
            editor.BeforeChange(); editor.Frame.Layers.Add(ArtworkLayer.Create($"Layer {editor.Frame.Layers.Count + 1}", editor.Scene.Width, editor.Scene.Height, editor.Scene.IsRgba));
            editor.LayerIndex = editor.Frame.Layers.Count - 1; editor.Tool = Tool.Pixel; RefreshTools(); RefreshAll();
        }));
        inspector.Children.Add(new Separator());

        var textHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        textHeader.Children.Add(Label("Text & localization", true));
        AddAt(textHeader, Icon(PackIconMaterialKind.Plus, "Add text object", AddText), 1);
        var obj = editor.SelectedText;
        inspector.Children.Add(textHeader);
        if (editor.Frame.TextObjects.Count > 0)
        {
            var textRows = new StackPanel { Name = "TextObjectList", Spacing = 2 };
            foreach (var textObject in editor.Frame.TextObjects)
            {
                var capturedObject = textObject;
                var row = new Grid { Name = "TextRow_" + textObject.Id, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var modifiers = KeyModifiers.None;
                var select = Button(textObject.Key, () => SelectTextItem(capturedObject, modifiers));
                select.Name = "TextSelect_" + textObject.Id;
                select.PointerPressed += (_, e) => modifiers = e.KeyModifiers;
                select.KeyDown += (_, e) => modifiers = e.KeyModifiers;
                select.HorizontalAlignment = HorizontalAlignment.Stretch;
                select.HorizontalContentAlignment = HorizontalAlignment.Left;
                select.Background = Brush(selectedTextIds.Contains(textObject.Id) ? UiTheme.Selection : UiTheme.Surface);
                row.Children.Add(select); AddAt(row, TextObjectOptions(textObject), 1); textRows.Children.Add(row);
            }
            inspector.Children.Add(new ScrollViewer { Content = textRows, MaxHeight = compact ? 180 : 140 });
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
        textActions.Children.Add(Button("Languages…", ManageLanguages)); inspector.Children.Add(textActions);
        var installFonts = Button("Install missing fonts…", () => _ = CheckFontsAsync(CancellationToken.None, explicitlyRequested: true));
        installFonts.Name = "InstallMissingFonts";
        inspector.Children.Add(installFonts);
    }

    private Button LayerOptions(int index)
    {
        var scene = editor.Scene; var frame = editor.Frame; var layer = frame.Layers[index];
        bool Current() => ReferenceEquals(editor.Scene, scene) && ReferenceEquals(editor.Frame, frame) && frame.Layers.Contains(layer);
        var button = Button("…", () => { }); button.Name = $"LayerOptions{index}";
        Avalonia.Automation.AutomationProperties.SetName(button, $"Options for layer {layer.Name}");
        ToolTip.SetTip(button, "Layer options");
        MenuItem Item(string title, string name, Action action)
        {
            var item = new MenuItem { Header = title, Name = name + index };
            item.Click += (_, _) => { if (!Current()) return; FinishPath(); EndInlineTextEdit(); action(); };
            return item;
        }
        var rename = Item("Rename…", "LayerRename", () => RenameLayer(layer));
        var raise = Item("Raise layer", "LayerRaise", () => MoveLayers(layer, 1));
        var lower = Item("Lower layer", "LayerLower", () => MoveLayers(layer, -1));
        var delete = Item("Delete layer(s)", "LayerDelete", () => DeleteLayers(layer));
        button.ContextMenu = new ContextMenu { Items = { rename, raise, lower, new Separator(), delete } };
        button.ContextMenu.Opened += (_, _) =>
        {
            var i = Current() ? frame.Layers.IndexOf(layer) : -1;
            var targets = Current() ? LayerTargets(layer) : [];
            delete.Header = targets.Count > 1 ? $"Delete {targets.Count} layers" : "Delete layer";
            raise.IsEnabled = i >= 0 && !targets.Contains(frame.Layers[^1]);
            lower.IsEnabled = i >= 0 && !targets.Contains(frame.Layers[0]);
            delete.IsEnabled = i >= 0 && targets.Count < frame.Layers.Count;
        };
        button.Click += (_, _) => button.ContextMenu.Open(button); return button;
    }

    private Button TextObjectOptions(TextObject obj)
    {
        var scene = editor.Scene; var frame = editor.Frame;
        bool Current() => ReferenceEquals(editor.Scene, scene) && ReferenceEquals(editor.Frame, frame) && frame.TextObjects.Contains(obj);
        var button = Button("…", () => { }); button.Name = "TextOptions_" + obj.Id;
        Avalonia.Automation.AutomationProperties.SetName(button, $"Options for text {obj.Key}");
        ToolTip.SetTip(button, "Text options");
        var edit = new MenuItem { Header = "Layout & style…", Name = "TextEdit_" + obj.Id };
        edit.Click += (_, _) => { if (Current()) EditTextProperties(obj); };
        var delete = new MenuItem { Header = "Delete text object", Name = "TextDelete_" + obj.Id };
        delete.Click += (_, _) => { if (Current()) DeleteTextObjects(obj); };
        button.ContextMenu = new ContextMenu { Items = { edit, delete } };
        button.ContextMenu.Opened += (_, _) =>
        {
            SyncItemSelection(); var count = selectedTextIds.Contains(obj.Id) ? selectedTextIds.Count : 1;
            delete.Header = count > 1 ? $"Delete {count} text objects" : "Delete text object";
        };
        button.Click += (_, _) => button.ContextMenu.Open(button); return button;
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
