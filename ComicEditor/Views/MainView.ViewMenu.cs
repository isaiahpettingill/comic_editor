using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ComicEditor.Views;

public partial class MainView
{
    private MenuItem ViewMenu()
    {
        var view = new MenuItem { Header = "_View", Name = "ViewMenu" };
        var onion = new MenuItem
        {
            Header = "Onion skin", Name = "OnionSkinMenu",
            ToggleType = MenuItemToggleType.CheckBox, IsChecked = editor.OnionSkin
        };
        var opacity = new MenuItem { Header = "Onion skin opacity…", Name = "OnionOpacityMenu", IsEnabled = editor.OnionSkin };
        onion.PropertyChanged += (_, e) =>
        {
            if (e.Property != MenuItem.IsCheckedProperty) return;
            editor.OnionSkin = onion.IsChecked;
            opacity.IsEnabled = editor.OnionSkin;
            RefreshCanvas();
        };
        opacity.Click += (_, _) => EditOnionOpacity();
        var compare = new MenuItem
        {
            Header = "Compare", Name = "CompareMenu",
            ToggleType = MenuItemToggleType.CheckBox, IsChecked = editor.Compare
        };
        ToolTip.SetTip(compare, "Show the previous frame beside the current frame in desktop layout");
        compare.PropertyChanged += (_, e) =>
        {
            if (e.Property != MenuItem.IsCheckedProperty) return;
            editor.Compare = compare.IsChecked; RefreshCanvas();
        };
        var reset = new MenuItem { Header = "Reset pane layout" };
        reset.Click += (_, _) =>
        {
            desktopWorkspace = null; paneOrder[0] = Pane.Storyboard; paneOrder[1] = Pane.Canvas; paneOrder[2] = Pane.Inspector;
            paneWidths[0] = new(190); paneWidths[1] = new(1, GridUnitType.Star); paneWidths[2] = new(300); Build(compact);
        };
        foreach (var item in new Control[] { onion, opacity, compare, new Separator(), reset, ThemeMenu() }) view.Items.Add(item);
        return view;
    }

    private void EditOnionOpacity()
    {
        var opacity = new Slider
        {
            Name = "OnionOpacity", Minimum = 0, Maximum = 1, Value = editor.OnionOpacity,
            SmallChange = 0.05, LargeChange = 0.1, Height = compact ? 44 : 32,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 12, 0)
        };
        Avalonia.Automation.AutomationProperties.SetName(opacity, "Onion skin opacity");
        var percent = Label($"{editor.OnionOpacity:P0}"); percent.Width = 44;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(opacity); AddAt(row, percent, 1);
        opacity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            editor.OnionOpacity = opacity.Value; percent.Text = $"{opacity.Value:P0}"; RefreshCanvas();
        };
        ShowModal("Onion skin opacity", new StackPanel
        {
            Spacing = 8,
            Children = { Label("How strongly the previous frame shows through the current frame."), row }
        });
    }
}
