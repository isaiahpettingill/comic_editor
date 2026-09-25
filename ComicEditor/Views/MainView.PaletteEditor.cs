using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ComicEditor.Editing;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private void EditPalette()
    {
        FinishPath();
        var draft = editor.Scene.Palette.Select((hex, i) => new GplColor(hex, $"Color {i:D3}")).ToList();
        var selected = editor.Color; var syncing = false; var busy = false;
        var body = new StackPanel { Spacing = 8 };
        var presets = new ComboBox { Name = "PalettePresets", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Saved palettes" };
        var name = new TextBox { Name = "PaletteName", Text = "Cutscene palette", PlaceholderText = "Palette name", MaxLength = 80 };
        var count = new NumericUpDown { Name = "PaletteSize", Minimum = 2, Maximum = 255, Value = draft.Count, Width = 110, Increment = 1 };
        var swatches = new WrapPanel { Name = "PaletteEditorSwatches", Orientation = Orientation.Horizontal };
        var caption = Label("");
        var hex = new TextBox { Name = "PaletteEditorHex", MaxLength = 7, Width = 105 };
        var picker = PaletteColorPicker.Create(Color.Parse(draft[selected].Hex));
        var sample = new Border { Width = 38, Height = 32, BorderBrush = Brush(UiTheme.Border), BorderThickness = new Thickness(1) };
        var channels = Enumerable.Range(0, 3).Select(i => new NumericUpDown { Name = "Palette" + new[] { "Red", "Green", "Blue" }[i], Minimum = 0, Maximum = 255, Increment = 1, ShowButtonSpinner = !touchLayout }).ToArray();
        var message = Label("Changes stay in this cutscene. Save preset writes a shared .gpl file."); message.Name = "PaletteEditorStatus"; message.FontSize = 12;
        void Report(string text, bool error = false) { message.Text = text; message.Foreground = error ? Brush(UiTheme.Error) : Brush(UiTheme.Text); }
        void RefreshSelection()
        {
            syncing = true; caption.Text = $"Color {selected:D3}"; hex.Text = draft[selected].Hex;
            sample.Background = Brush(draft[selected].Hex);
            picker.Color = Color.Parse(draft[selected].Hex);
            var rgb = Convert.FromHexString(draft[selected].Hex[1..]);
            for (var i = 0; i < 3; i++) channels[i].Value = rgb[i];
            for (var i = 0; i < swatches.Children.Count; i++)
            {
                var button = (Button)swatches.Children[i]; button.Background = Brush(draft[i].Hex);
                button.BorderBrush = i == selected ? Brush(UiTheme.Accent) : Brush(UiTheme.Border);
                button.BorderThickness = new Thickness(i == selected ? 3 : 1);
                ToolTip.SetTip(button, $"{i:D3}  {draft[i].Hex}  {draft[i].Name}");
            }
            syncing = false;
        }
        void Rebuild()
        {
            syncing = true; count.Value = draft.Count; selected = Math.Min(selected, draft.Count - 1); swatches.Children.Clear();
            for (var i = 0; i < draft.Count; i++)
            {
                var index = i; var button = new Button { Name = "PaletteEditSwatch" + i, Width = touchLayout ? 40 : 27, Height = touchLayout ? 40 : 27, Margin = new Thickness(1), Padding = default };
                Avalonia.Automation.AutomationProperties.SetName(button, $"Palette color {i:D3}");
                button.Click += (_, _) => { selected = index; RefreshSelection(); };
                swatches.Children.Add(button);
            }
            RefreshSelection();
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; body.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { Report(ex.Message, true); }
            finally { busy = false; body.IsEnabled = true; }
        }
        async Task RefreshPresets(string? select = null)
        {
            var files = await PaletteLibrary.List(); presets.ItemsSource = files;
            presets.SelectedItem = select ?? files.FirstOrDefault();
        }
        GplPalette Snapshot()
        {
            if (!GplPalette.IsHex(hex.Text)) throw new InvalidDataException("Enter a color as #RRGGBB.");
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException("Enter a palette name.");
            return new(name.Text.Trim(), draft.ToArray());
        }
        void Load(string text, string fallback)
        {
            var palette = GplPalette.Parse(text, fallback); draft = palette.Colors.ToList(); name.Text = palette.Name; Rebuild();
            Report(draft.Count < editor.Scene.Palette.Count ? $"Loaded {draft.Count} colors. Apply maps removed slots to their nearest remaining color. Saved presets stay unchanged." : $"Loaded {draft.Count} colors. Apply changes this cutscene; saved presets remain unchanged.");
        }
        var load = Button("Load", () => _ = Run(async () =>
        {
            if (presets.SelectedItem is string file) Load(await PaletteLibrary.Read(file), Path.GetFileNameWithoutExtension(file));
        })); load.Name = "LoadPalettePreset";
        var presetRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; presetRow.Children.Add(presets); AddAt(presetRow, load, 1);
        body.Children.Add(presetRow);
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; nameRow.Children.Add(name); body.Children.Add(nameRow);
        var files = new[] { new FilePickerFileType("GIMP palette") { Patterns = ["*.gpl"], MimeTypes = ["application/x-gimp-palette", "text/plain"] } };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var import = Button("Import .gpl…", () => _ = Run(async () =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var chosen = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import palette", AllowMultiple = false, FileTypeFilter = files });
            if (chosen.Count == 0) return;
            var info = await chosen[0].GetBasicPropertiesAsync();
            if (info.Size > 1_048_576) throw new InvalidDataException("The palette file is too large.");
            await using var stream = await chosen[0].OpenReadAsync(); using var reader = new StreamReader(stream);
            Load(await reader.ReadToEndAsync(), Path.GetFileNameWithoutExtension(chosen[0].Name));
        })); import.Name = "ImportPalette"; actions.Children.Add(import);
        var export = Button("Export .gpl…", () => _ = Run(async () =>
        {
            var palette = Snapshot(); var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var chosen = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export palette", SuggestedFileName = PaletteLibrary.FileName(palette.Name), DefaultExtension = "gpl", FileTypeChoices = files });
            if (chosen is null) return;
            await using var stream = await chosen.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(palette.Write()); await writer.FlushAsync();
            Report($"Exported {chosen.Name}.");
        })); export.Name = "ExportPalette"; actions.Children.Add(export);
        var save = Button("Save preset", () => _ = Run(async () =>
        {
            var palette = Snapshot(); var file = PaletteLibrary.FileName(palette.Name);
            await PaletteLibrary.Write(file, palette.Write()); await RefreshPresets(file); Report($"Saved {file} to your palette library.");
        })); save.Name = "SavePalettePreset"; save.Margin = new Thickness(4, 0, 0, 0); AddAt(nameRow, save, 1);
        if (!touchLayout && !OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            actions.Children.Add(Button("Open folder", () => _ = Run(async () =>
            {
                Directory.CreateDirectory(PaletteLibrary.DefaultDirectory);
                if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher && !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(PaletteLibrary.DefaultDirectory)))
                    Report("Palette folder: " + PaletteLibrary.DefaultDirectory);
            })));
        foreach (var action in actions.Children) action.Margin = new Thickness(0, 0, 4, 4);
        body.Children.Add(actions); body.Children.Add(Row(Label("Colors"), count));
        body.Children.Add(new ScrollViewer { Name = "PaletteEditorViewport", Content = swatches, MaxHeight = touchLayout ? 84 : 285, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(Row(caption, hex, sample));
        body.Children.Add(picker);
        var rgbGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*,6,*") };
        for (var i = 0; i < 3; i++) AddAt(rgbGrid, new StackPanel { Children = { Label(new[] { "Red", "Green", "Blue" }[i]), channels[i] } }, i * 2);
        body.Children.Add(rgbGrid); body.Children.Add(message);
        count.ValueChanged += (_, _) =>
        {
            if (syncing || count.Value is not decimal size) return;
            var next = Math.Clamp((int)size, 2, 255);
            while (draft.Count < next) draft.Add(new("#FFFFFF", $"Color {draft.Count:D3}"));
            if (draft.Count > next) draft.RemoveRange(next, draft.Count - next);
            Rebuild(); Report(next < editor.Scene.Palette.Count ? "Removed color slots will map to their nearest remaining color in artwork and text. You can undo Apply." : "New slots are white. Changes apply to this cutscene only.");
        };
        hex.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (!GplPalette.IsHex(hex.Text)) { Report("Enter a color as #RRGGBB.", true); return; }
            draft[selected] = draft[selected] with { Hex = hex.Text!.ToUpperInvariant() }; RefreshSelection();
        };
        picker.PropertyChanged += (_, e) =>
        {
            if (syncing || e.Property != AvaloniaColorPicker.CustomColorPicker.ColorProperty) return;
            var color = picker.Color;
            hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        };
        foreach (var channel in channels) channel.ValueChanged += (_, _) =>
        {
            if (syncing || channels.Any(c => c.Value is null)) return;
            hex.Text = $"#{(int)channels[0].Value!:X2}{(int)channels[1].Value!:X2}{(int)channels[2].Value!:X2}";
        };
        Rebuild();
        ShowModal("Palette editor", body, () =>
        {
            if (busy) return;
            try
            {
                var palette = Snapshot(); editor.ApplyPalette(palette.Colors.Select(c => c.Hex).ToArray());
                selection = clipboardSelection = null; CloseModal(); RefreshAll();
            }
            catch (Exception ex) { Report(ex.Message, true); }
        });
        if (!touchLayout && modal?.Child is Border card) card.MaxWidth = 920;
        _ = Run(() => RefreshPresets());
    }
}
