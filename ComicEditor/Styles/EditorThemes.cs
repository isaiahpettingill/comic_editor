using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace ComicEditor.Styles;

public sealed record EditorTheme(string Id, string Name, bool Dark, string Background, string Surface,
    string Header, string Workspace, string Border, string Text, string Muted, string Accent,
    string Selection, string Error, string Success);

public static class EditorThemes
{
    public const string Default = "solarized-light";
    public static IReadOnlyList<EditorTheme> All { get; } = new EditorTheme[]
    {
        // Preserve the editor's original warm light appearance.
        new(Default, "Solarized light", false, "#F0EFEA", "#F5F4F0", "#DBDEE3", "#D9DCDF", "#989DA3", "#000000", "#60656B", "#147BC1", "#D7EAF8", "#AB3B13", "#406543"),
        new("solarized-dark", "Solarized dark", true, "#002B36", "#073642", "#0A424C", "#00212B", "#586E75", "#93A1A1", "#839496", "#2AA198", "#164F59", "#FF8269", "#B5BD68"),
        new("catppuccin-mocha", "Catppuccin Mocha", true, "#1E1E2E", "#313244", "#45475A", "#181825", "#6C7086", "#CDD6F4", "#BAC2DE", "#89B4FA", "#45475A", "#F38BA8", "#A6E3A1"),
        new("catppuccin-latte", "Catppuccin Latte", false, "#EFF1F5", "#E6E9EF", "#CCD0DA", "#DCE0E8", "#8C8FA1", "#4C4F69", "#5C5F77", "#1E66F5", "#BCC0CC", "#D20F39", "#2F7B20"),
        new("dark", "Dark", true, "#202020", "#2D2D2D", "#383838", "#181818", "#737373", "#E8E8E8", "#B8B8B8", "#75BEFF", "#354B60", "#FF9999", "#9FD88B"),
        new("gruvbox", "Gruvbox", true, "#282828", "#32302F", "#3C3836", "#1D2021", "#7C6F64", "#EBDBB2", "#BDAE93", "#FABD2F", "#504945", "#FB4934", "#B8BB26")
    };

    public static EditorTheme Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static void Apply(string? id)
    {
        if (Application.Current is not { } app) return;
        var theme = Find(id);
        var variant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        for (var i = 0; i < app.Styles.Count; i++)
        {
            if (app.Styles[i] is not FluentTheme previous) continue;
            // Fluent caches some brushes when it first loads a variant. Build
            // its resources with the new palette before attaching it to the app.
            var fluent = new FluentTheme { DensityStyle = previous.DensityStyle };
            var palette = new ColorPaletteResources();
            if (theme.Id != Default)
            {
                var text = Color.Parse(theme.Text); var surface = Color.Parse(theme.Surface);
                var background = Color.Parse(theme.Background); var header = Color.Parse(theme.Header);
                var muted = Color.Parse(theme.Muted); var border = Color.Parse(theme.Border);
                palette = new ColorPaletteResources
                {
                    Accent = Color.Parse(theme.Accent),
                    RegionColor = background,
                    BaseHigh = text,
                    BaseMediumHigh = text,
                    BaseMedium = muted,
                    BaseMediumLow = muted,
                    BaseLow = header,
                    AltHigh = surface,
                    AltMediumHigh = surface,
                    AltMedium = surface,
                    AltMediumLow = surface,
                    AltLow = background,
                    ChromeAltLow = header,
                    ChromeHigh = border,
                    ChromeMedium = header,
                    ChromeMediumLow = surface,
                    ChromeLow = background,
                    ChromeBlackHigh = text,
                    ChromeBlackMedium = muted,
                    ChromeBlackMediumLow = muted,
                    ChromeBlackLow = border,
                    ChromeWhite = surface,
                    ChromeGray = muted,
                    ChromeDisabledHigh = header,
                    ChromeDisabledLow = muted,
                    ListLow = header,
                    ListMedium = Color.Parse(theme.Selection),
                    ErrorText = Color.Parse(theme.Error)
                };
            }
            fluent.Palettes[variant] = palette;
            app.Styles[i] = fluent;
        }
        app.RequestedThemeVariant = variant;
    }
}
