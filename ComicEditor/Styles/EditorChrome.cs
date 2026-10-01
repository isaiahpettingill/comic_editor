using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ComicEditor.Styles;

// Quiet secondary actions, without changing drawing tools, swatches or inputs.
public sealed class EditorChrome : Avalonia.Styling.Styles
{
    public EditorChrome(EditorTheme theme)
    {
        Add(new Style(s => s.OfType<Button>().Class("chrome"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, Brushes.Transparent),
                new Setter(Button.BorderBrushProperty, Brushes.Transparent),
                // Reserve the focus outline so keyboard navigation never shifts content.
                new Setter(Button.BorderThicknessProperty, new Thickness(1))
            }
        });
        Add(new Style(s => s.OfType<Button>().Class("chrome").Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(theme.Selection))),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)
            }
        });
        Add(new Style(s => s.OfType<Button>().Class("chrome").Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(theme.Header)))
            }
        });
        Add(new Style(s => s.OfType<Button>().Class("chrome").Class(":focus-visible").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BorderBrushProperty, new SolidColorBrush(Color.Parse(theme.Accent))),
                new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(1))
            }
        });
        Add(new Style(s => s.OfType<Button>().Class("chrome").Class(":disabled").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)
            }
        });
    }
}
