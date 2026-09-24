using Avalonia.Controls;
using Avalonia.Media;
using ComicEditor.Styles;

namespace ComicEditor.Views;

public partial class MainView
{
    private EditorTheme UiTheme => EditorThemes.Find(editor.Preferences.Theme);

    private MenuItem ThemeMenu()
    {
        var menu = new MenuItem { Header = "Theme", Name = "ThemeMenu" };
        foreach (var theme in EditorThemes.All)
        {
            var item = new MenuItem
            {
                Header = theme.Name,
                Name = "Theme_" + theme.Id.Replace('-', '_'),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "EditorTheme",
                IsChecked = theme.Id == UiTheme.Id
            };
            item.Click += (_, _) => SetTheme(theme.Id);
            menu.Items.Add(item);
        }
        return menu;
    }

    private void SetTheme(string id)
    {
        FinishPath();
        editor.Preferences.Theme = EditorThemes.Find(id).Id;
        editor.Preferences.Save();
        EditorThemes.Apply(editor.Preferences.Theme);
        Foreground = Brush(UiTheme.Text);
        Build(compact);
    }
}
