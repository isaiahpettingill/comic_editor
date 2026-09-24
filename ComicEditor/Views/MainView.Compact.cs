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
        var navigation = new Grid { Name = "CompactNavigation", ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Background = Brush(UiTheme.Surface) };
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
        compactColor = new Border { Width = 16, Height = 16, BorderBrush = Brush(UiTheme.Border), BorderThickness = new Thickness(1), Background = Brush(editor.Scene.Palette[editor.Color]) };
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
            compactTabs[i].Background = i == (int)page ? Brush(UiTheme.Selection) : Brushes.Transparent;
        }
        if (toolOptions is not null) toolOptions.IsVisible = page == CompactPage.Draw;
    }

    private void RefreshCompactTools()
    {
        if (toolOptions is null) return;
        toolOptions.Margin = new Thickness(4, 2);
        toolOptions.Spacing = 4;
        var choices = new Grid { Name = "CompactToolChoices", ColumnDefinitions = new ColumnDefinitions("*,*") };
        for (var i = 0; i < (Enum.GetValues<Tool>().Length + 1) / 2; i++) choices.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var toolScroll = new ScrollViewer { Content = choices, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = toolScroll };
        flyout.Opening += (_, _) => toolScroll.MaxHeight = Math.Max(88, Bounds.Height - 100);
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = Button("", () => { flyout.Hide(); ChooseTool(tool); });
            button.Name = "CompactTool" + tool;
            var caption = Label(ToolName(tool)); caption.FontSize = 12;
            button.Content = Row(new PackIconMaterial { Kind = ToolIcon(tool), Width = 22, Height = 22 }, caption);
            button.Width = 140; button.Margin = new Thickness(2);
            if (tool == editor.Tool) button.Background = Brush(UiTheme.Selection);
            AddAt(choices, button, (int)tool % 2, (int)tool / 2);
        }
        var chooseTool = Icon(ToolIcon(editor.Tool), "Choose drawing tool: " + ToolName(editor.Tool), () => { });
        chooseTool.Name = "CompactToolPicker"; chooseTool.Flyout = flyout;
        toolOptions.Children.Add(chooseTool);
        var options = Button(pathBase is not null ? "Finish" : UsesSize(editor.Tool) ? $"{editor.BrushSize}px · Options" : "Options", () => { if (pathBase is not null) FinishPath(); else EditToolOptions(); });
        options.Width = 112; options.Padding = new Thickness(4); options.FontSize = 12; options.Name = "ToolOptions";
        toolOptions.Children.Add(options);
        var scales = new[] { "Fit", "100%", "200%", "400%", $"{zoom:P0}" }.Distinct().ToArray();
        var scale = new ComboBox { Name = "CompactZoom", ItemsSource = scales, SelectedItem = zoom == 0 ? "Fit" : $"{zoom:P0}", Width = 84, Height = 44, MinHeight = 44, Padding = new Thickness(6, 3) };
        Avalonia.Automation.AutomationProperties.SetName(scale, "Canvas zoom");
        scale.SelectionChanged += (_, _) => { if (scale.SelectedIndex <= 3) zoom = scale.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 }; RefreshCanvas(); };
        toolOptions.Children.Add(scale);
        if (compactOnionButton is not null) toolOptions.Children.Add(compactOnionButton);
    }
}
