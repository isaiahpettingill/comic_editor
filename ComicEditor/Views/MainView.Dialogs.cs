using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Views;

public partial class MainView
{
    private void EditDrawingInput()
    {
        var smooth = new CheckBox { Name = "SmoothMouse", Content = "Smooth mouse / touchpad strokes", IsChecked = editor.Preferences.SmoothMouse };
        ShowModal("Drawing input", new StackPanel
        {
            Spacing = 12,
            Children =
        {
            smooth, Label("Reduces small wobbles in freehand strokes. Pen input keeps its normal pressure response. Pixel edges stay crisp.")
        }
        }, () => { editor.Preferences.SmoothMouse = smooth.IsChecked == true; editor.Preferences.Save(); CloseModal(); });
    }

    private void ResizeCanvas()
    {
        var width = new NumericUpDown { Name = "CanvasWidth", Value = editor.Scene.Width, Minimum = 1, Maximum = 2048, Height = 34 };
        var height = new NumericUpDown { Name = "CanvasHeight", Value = editor.Scene.Height, Minimum = 1, Maximum = 2048, Height = 34 };
        var sizes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        sizes.Children.Add(new StackPanel { Spacing = 4, Children = { Label("Width (px)"), width } });
        AddAt(sizes, new StackPanel { Spacing = 4, Children = { Label("Height (px)"), height } }, 2);
        var anchor = new ComboBox { ItemsSource = new[] { "Center", "Top left" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var warning = Label("");
        void UpdateWarning() => warning.Text = width.Value < editor.Scene.Width || height.Value < editor.Scene.Height
            ? "This will crop artwork outside the new canvas. You can undo the resize."
            : "New space will be transparent. Existing artwork keeps its pixel size.";
        width.ValueChanged += (_, _) => UpdateWarning(); height.ValueChanged += (_, _) => UpdateWarning(); UpdateWarning();
        ShowModal("Resize canvas", new StackPanel
        {
            Spacing = 10,
            Children =
        { Label($"Applies to all {editor.Scene.Frames.Count} frames."), sizes, Label("Anchor"), anchor, warning }
        }, () =>
        {
            if (width.Value is not decimal w || height.Value is not decimal h) return;
            if ((int)w != editor.Scene.Width || (int)h != editor.Scene.Height)
            { editor.BeforeChange(); editor.Scene.ResizeCanvas((int)w, (int)h, anchor.SelectedIndex == 0); }
            selection = null; editor.RememberCanvas(); CloseModal(); RefreshAll(); RefreshTools();
        }, "Resize");
    }

    private void ShowModal(string title, Control body, Action? apply = null, string applyText = "Apply")
    {
        if (shell is null || rootGrid is null) return;
        returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        rootGrid.IsEnabled = false;
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        var heading = Label(title, true); heading.FontSize = 18; heading.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(heading);
        AddAt(panel, new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, row: 1);
        var footer = Row(Button(apply is null ? "Close" : "Cancel", CloseModal)); footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(0, 14, 0, 0);
        if (apply is not null) { var confirm = Button(applyText, apply); confirm.Name = "ModalApply"; footer.Children.Add(confirm); }
        AddAt(panel, footer, row: 2);
        var card = new Border
        {
            Name = "ModalCard",
            Child = panel,
            Background = Brush("#F5F4F0"),
            BorderBrush = Brush("#989DA3"),
            BorderThickness = new Thickness(1),
            MaxWidth = 540,
            MaxHeight = Math.Max(120, Bounds.Height - 24),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        modal = new Border { Background = Brush("#66000000"), Child = card, Name = "ModalOverlay" };
        shell.Children.Add(modal);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => footer.Children.OfType<Button>().First().Focus());
    }

    private void CloseModal()
    {
        if (updateInstalling) return;
        if (modal is not null) shell?.Children.Remove(modal);
        modal = null; if (rootGrid is not null) rootGrid.IsEnabled = true;
        returnFocus?.Focus(); returnFocus = null;
        if (UseCompactLayout != compact || compact && compactSingleRow != CompactLandscape) Build(UseCompactLayout);
    }

    private void RenameLayer(ArtworkLayer layer)
    {
        var name = new TextBox { Text = layer.Name, MaxLength = 80 };
        ShowModal("Rename layer", name, () =>
        { if (string.IsNullOrWhiteSpace(name.Text)) return; editor.BeforeChange(); layer.Name = name.Text.Trim(); CloseModal(); RefreshAll(); });
    }

    private void EditTextProperties(TextObject obj)
    {
        var body = new StackPanel { Spacing = 10 };
        var geometry = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        NumericUpDown Number(string title, double value, double min, int col, int row)
        {
            var input = new NumericUpDown { Name = "Text" + title.Replace(" ", ""), Value = (decimal)value, Minimum = (decimal)min, Maximum = 2048, Increment = 1, Height = 34 };
            var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8), Children = { Label(title), input } };
            AddAt(geometry, field, col, row); return input;
        }
        var x = Number("X", obj.X, -2048, 0, 0); var y = Number("Y", obj.Y, -2048, 2, 0);
        var width = Number("Width", obj.Width, 1, 0, 1); var height = Number("Height", obj.Height, 1, 2, 1);
        body.Children.Add(geometry);
        var fontIds = CutsceneFonts.Ids.Concat(editor.Scene.Frames.SelectMany(f => f.TextObjects).Select(t => CutsceneFonts.Normalize(t.FontId))
            .Where(id => id.StartsWith("google:"))).Concat(CutsceneFonts.ImportedIds).Distinct().ToList();
        var bundled = fontIds.IndexOf(CutsceneFonts.Normalize(obj.FontId));
        var font = new ComboBox
        {
            Name = "TextFont",
            ItemsSource = fontIds.Select(CutsceneFonts.NameFor).Append("Custom system font…").ToArray(),
            SelectedIndex = bundled >= 0 ? bundled : fontIds.Count,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var custom = new TextBox
        {
            Name = "CustomFont",
            Text = obj.FontId.StartsWith("system:") ? obj.FontId[7..] : bundled < 0 ? obj.FontId : "",
            PlaceholderText = "Installed font family name"
        };
        var requirement = Label("");
        body.Children.Add(Label("Font")); body.Children.Add(font); body.Children.Add(custom); body.Children.Add(requirement);
        var googleLink = new TextBox { Name = "GoogleFontLink", PlaceholderText = "https://fonts.google.com/specimen/…" };
        var importStatus = Label(""); importStatus.IsVisible = false;
        var import = Button("Load font", () => { }); import.Name = "LoadGoogleFont";
        var importRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        importRow.Children.Add(googleLink); AddAt(importRow, import, 1);
        body.Children.Add(Label("Google Fonts link")); body.Children.Add(importRow); body.Children.Add(importStatus);
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false; importStatus.IsVisible = true; importStatus.Text = "Downloading font…";
            try
            {
                var id = await CutsceneFonts.LoadGoogleAsync(googleLink.Text ?? "");
                if (!fontIds.Contains(id)) fontIds.Add(id);
                font.ItemsSource = fontIds.Select(CutsceneFonts.NameFor).Append("Custom system font…").ToArray();
                font.SelectedIndex = fontIds.IndexOf(id); importStatus.Text = "Ready. The project stores the family reference.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
            finally { import.IsEnabled = true; }
        };
        var size = new NumericUpDown { Name = "TextFontSize", Value = (decimal)obj.FontSize, Minimum = 1, Maximum = 2048, Width = 120, Height = 34 };
        var bold = new CheckBox { Content = "Bold", IsChecked = obj.Bold }; var italic = new CheckBox { Content = "Italic", IsChecked = obj.Italic };
        body.Children.Add(Row(Label("Size (px)"), size, bold, italic));
        var sample = new TextBlock { Text = editor.Scene.RenderText(editor.Language, obj.Key), TextWrapping = TextWrapping.Wrap, MaxHeight = 75 };
        if (string.IsNullOrWhiteSpace(sample.Text)) sample.Text = "Hades is drinking the ocean!";
        body.Children.Add(new Border { Child = sample, Padding = new Thickness(8), Background = Brushes.White, ClipToBounds = true });
        string FontId() => font.SelectedIndex >= 0 && font.SelectedIndex < fontIds.Count ? fontIds[font.SelectedIndex] : "system:" + custom.Text?.Trim();
        void UpdatePreview()
        {
            var isCustom = font.SelectedIndex == fontIds.Count; custom.IsVisible = isCustom;
            requirement.Text = isCustom ? "Install this font on machines that edit or compile the project. The game uses rasterized text." : "Font family reference. Noto supplies missing glyphs.";
            if (isCustom && !CutsceneFonts.IsInstalled(FontId())) requirement.Text += " Unavailable here: preview uses Noto.";
            sample.FontFamily = CutsceneFonts.Resolve(isCustom && string.IsNullOrWhiteSpace(custom.Text) ? "noto-sans" : FontId());
            sample.FontSize = (double)(size.Value ?? 16); sample.FontWeight = bold.IsChecked == true ? FontWeight.Bold : FontWeight.Normal;
            sample.FontStyle = italic.IsChecked == true ? FontStyle.Italic : FontStyle.Normal;
        }
        font.SelectionChanged += (_, _) => UpdatePreview(); custom.TextChanged += (_, _) => UpdatePreview();
        size.ValueChanged += (_, _) => UpdatePreview(); bold.Click += (_, _) => UpdatePreview(); italic.Click += (_, _) => UpdatePreview(); UpdatePreview();
        var color = new ComboBox { ItemsSource = editor.Scene.Palette.Select((hex, i) => $"{i:D3}  {hex}").ToArray(), SelectedIndex = obj.Color, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(Label("Palette color")); body.Children.Add(color);
        ShowModal("Text layout & style", body, () =>
        {
            if (new[] { x, y, width, height, size }.Any(n => n.Value is null) || color.SelectedIndex < 0) return;
            if (font.SelectedIndex == fontIds.Count && string.IsNullOrWhiteSpace(custom.Text)) { requirement.Text = "Enter the installed font family name."; return; }
            editor.BeforeChange(); obj.X = (double)x.Value!.Value; obj.Y = (double)y.Value!.Value;
            obj.Width = (double)width.Value!.Value; obj.Height = (double)height.Value!.Value; obj.FontSize = (double)size.Value!.Value;
            obj.FontId = FontId(); obj.Color = color.SelectedIndex;
            editor.Color = obj.Color;
            obj.Bold = bold.IsChecked == true; obj.Italic = italic.IsChecked == true;
            editor.Preferences.Remember(obj);
            CloseModal(); RefreshAll();
        });
    }

    private void ManageLanguages()
    {
        var draft = editor.Scene.Translations.ToDictionary(k => k.Key, k => new Dictionary<string, string>(k.Value), StringComparer.OrdinalIgnoreCase);
        var fallback = editor.Scene.FallbackLanguage;
        var body = new StackPanel { Spacing = 10 };
        var list = new ListBox { Height = 150, SelectionMode = SelectionMode.Single };
        var defaultLanguage = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Refresh()
        {
            var keys = draft.Keys.Order().ToArray(); list.ItemsSource = keys;
            if (!draft.ContainsKey(fallback)) fallback = keys.FirstOrDefault() ?? "";
            defaultLanguage.ItemsSource = keys; defaultLanguage.SelectedItem = fallback;
        }
        defaultLanguage.SelectionChanged += (_, _) => { if (defaultLanguage.SelectedItem is string value) fallback = value; };
        Refresh(); body.Children.Add(Label("Project languages", true)); body.Children.Add(list);
        var code = new TextBox { Name = "LanguageCode", PlaceholderText = "Language code, e.g. fr or pt-BR", MinWidth = 220 };
        var message = Label(""); message.Foreground = Brush("#AB3B13");
        var add = Button("Add", () =>
        {
            var value = code.Text?.Trim() ?? "";
            if (value.Length is < 2 or > 35 || !value.Split('-').All(part => part.Length > 0 && part.All(char.IsAsciiLetterOrDigit)))
            { message.Text = "Use a language tag such as fr, ja, or zh-Hant."; return; }
            if (!draft.TryAdd(value, new Dictionary<string, string>())) { message.Text = "That language already exists."; return; }
            Refresh(); list.SelectedItem = value; code.Text = ""; message.Text = "";
        });
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; addRow.Children.Add(code); AddAt(addRow, add, 1); body.Children.Add(addRow);
        var confirmRemoval = new CheckBox { Content = "Remove its translations too" };
        var removeLanguage = Button("Remove selected", () =>
        {
            if (list.SelectedItem is not string value) return;
            if (draft.Count == 1) { message.Text = "Keep at least one project language."; return; }
            if (draft[value].Count > 0 && confirmRemoval.IsChecked != true) { message.Text = "Confirm removal of this language’s translations."; return; }
            draft.Remove(value); confirmRemoval.IsChecked = false; Refresh(); message.Text = "";
        });
        body.Children.Add(new WrapPanel { Children = { removeLanguage, confirmRemoval } });
        body.Children.Add(Label("Fallback language")); body.Children.Add(defaultLanguage);
        body.Children.Add(Label("Missing translations use the fallback text. If both are empty, the rendered dialogue is blank."));
        body.Children.Add(message);
        ShowModal("Manage languages", body, () =>
        {
            editor.BeforeChange(); editor.Scene.Translations = draft; editor.Scene.FallbackLanguage = fallback; editor.EnsureLanguage();
            CloseModal(); RefreshAll();
        });
    }

    private void EditPaletteColor()
    {
        var index = editor.Color; var initial = Color.Parse(editor.Scene.Palette[index]);
        var body = new StackPanel { Spacing = 10 };
        var preview = new Border { Height = 54, Background = new SolidColorBrush(initial) };
        var hex = new TextBox { Text = editor.Scene.Palette[index], MaxLength = 7, Name = "PaletteHex" };
        body.Children.Add(preview); body.Children.Add(Row(Label("Hex RGB"), hex));
        var sliders = new List<Slider>(); var syncing = false;
        foreach (var (label, value) in new[] { ("Red", initial.R), ("Green", initial.G), ("Blue", initial.B) })
        {
            var slider = new Slider { Minimum = 0, Maximum = 255, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*") }; row.Children.Add(Label(label)); AddAt(row, slider, 1); body.Children.Add(row); sliders.Add(slider);
        }
        foreach (var slider in sliders) slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || syncing) return;
            hex.Text = $"#{(int)sliders[0].Value:X2}{(int)sliders[1].Value:X2}{(int)sliders[2].Value:X2}";
        };
        var error = Label(""); error.Foreground = Brush("#AB3B13"); body.Children.Add(error);
        hex.TextChanged += (_, _) =>
        {
            var text = hex.Text ?? "";
            if (text.Length != 7 || text[0] != '#' || !text[1..].All(Uri.IsHexDigit)) { error.Text = "Enter #RRGGBB."; return; }
            error.Text = ""; var c = Color.Parse(text); preview.Background = new SolidColorBrush(c);
            syncing = true; sliders[0].Value = c.R; sliders[1].Value = c.G; sliders[2].Value = c.B; syncing = false;
        };
        ShowModal($"Palette color {index:D3}", body, () =>
        { if (error.Text?.Length > 0) return; SetPaletteColor(index, hex.Text ?? ""); CloseModal(); });
    }
}
