using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ComicEditor.Editing;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private enum CompactPage { Draw, Frames, Layers, Colors }
    private CompactPage compactPage;
    private Control[] compactPages = [];
    private Button[] compactTabs = [];
    private Button? compactOnionButton;
    private Border? compactColor;

    private void AddCompactWorkspace(Grid root, Grid workspace, Control draw, Control frames, Control layers, Control colors)
    {
        compactPages = [draw, frames, layers, colors];
        foreach (var page in compactPages) workspace.Children.Add(page);
        var navigation = new Grid { Name = "CompactNavigation", ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Background = Brush("#E2E3E3") };
        compactTabs = new Button[4];
        var names = new[] { "Draw", "Frames", "Layers/text", "Colors" };
        for (var i = 0; i < names.Length; i++)
        {
            var page = (CompactPage)i;
            var button = Button(names[i], () => ShowCompactPage(page));
            button.Name = "Compact" + page;
            button.Height = 48; button.Padding = new Thickness(2); button.FontSize = 13;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            compactTabs[i] = button; AddAt(navigation, button, i);
        }
        compactColor = new Border { Width = 16, Height = 16, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brush(editor.Scene.Palette[editor.Color]) };
        compactTabs[3].Content = Row(compactColor, Label("Colors"));
        AddAt(root, navigation, row: 4);
        ShowCompactPage(compactPage);
    }

    private void ShowCompactPage(CompactPage page)
    {
        if (!compact || compactPages.Length == 0) return;
        compactPage = page;
        for (var i = 0; i < compactPages.Length; i++)
        {
            compactPages[i].IsVisible = i == (int)page;
            compactTabs[i].Background = i == (int)page ? Brush("#BBDDF5") : Brushes.Transparent;
        }
        if (toolOptions is not null) toolOptions.IsVisible = page == CompactPage.Draw;
    }

    private void RefreshCompactTools()
    {
        if (toolOptions is null) return;
        toolOptions.Margin = new Thickness(4, 2);
        toolOptions.Spacing = 4;
        var kinds = new[] { PackIconMaterialKind.Pencil, PackIconMaterialKind.Brush, PackIconMaterialKind.Draw, PackIconMaterialKind.Eraser,
            PackIconMaterialKind.FormatColorFill, PackIconMaterialKind.VectorLine, PackIconMaterialKind.RectangleOutline,
            PackIconMaterialKind.EllipseOutline, PackIconMaterialKind.Eyedropper, PackIconMaterialKind.FormatText };
        var choices = new Grid { Name = "CompactToolChoices", ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto") };
        var toolScroll = new ScrollViewer { Content = choices, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = toolScroll };
        flyout.Opening += (_, _) => toolScroll.MaxHeight = Math.Max(88, Bounds.Height - 100);
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = Button("", () => { flyout.Hide(); editor.Tool = tool; RefreshTools(); RefreshInspector(); RefreshCanvas(); });
            button.Name = "CompactTool" + tool;
            button.Content = Row(new PackIconMaterial { Kind = kinds[(int)tool], Width = 22, Height = 22 }, Label(tool.ToString()));
            button.Width = 140; button.Margin = new Thickness(2);
            if (tool == editor.Tool) button.Background = Brush("#BBDDF5");
            AddAt(choices, button, (int)tool % 2, (int)tool / 2);
        }
        var chooseTool = Icon(kinds[(int)editor.Tool], "Choose drawing tool: " + editor.Tool, () => { });
        chooseTool.Name = "CompactToolPicker"; chooseTool.Flyout = flyout;
        toolOptions.Children.Add(chooseTool);
        if (editor.Tool is Tool.Pixel or Tool.Smooth or Tool.Pressure or Tool.Eraser)
        {
            var size = new NumericUpDown { Name = "CompactBrushSize", Value = editor.BrushSize, Minimum = 1, Maximum = 16, Width = 112, Height = 44, MinHeight = 44 };
            size.ValueChanged += (_, _) => editor.BrushSize = (int)(size.Value ?? 1);
            Avalonia.Automation.AutomationProperties.SetName(size, "Brush size in pixels");
            toolOptions.Children.Add(size);
        }
        var scales = new[] { "Fit", "100%", "200%", "400%", $"{zoom:P0}" }.Distinct().ToArray();
        var scale = new ComboBox { Name = "CompactZoom", ItemsSource = scales, SelectedItem = zoom == 0 ? "Fit" : $"{zoom:P0}", Width = 84, Height = 44, MinHeight = 44, Padding = new Thickness(6, 3) };
        Avalonia.Automation.AutomationProperties.SetName(scale, "Canvas zoom");
        scale.SelectionChanged += (_, _) => { if (scale.SelectedIndex <= 3) zoom = scale.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 }; RefreshCanvas(); };
        toolOptions.Children.Add(scale);
        if (compactOnionButton is not null) toolOptions.Children.Add(compactOnionButton);
    }
}
