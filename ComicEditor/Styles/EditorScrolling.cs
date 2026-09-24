using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace ComicEditor.Styles;

public sealed class EditorScrolling : Avalonia.Styling.Styles
{
    public EditorScrolling(bool touchLayout)
    {
        // Fluent's auto-hide mode spans the content underneath both scrollbars.
        // Turning it off lets the template reserve a separate row and column.
        // Set the attached property on template owners too (TextBox, ComboBox,
        // etc.), since their internal viewers bind to the owner's value.
        Add(new Style(selector => selector.Is<Control>())
        {
            Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) }
        });
        if (touchLayout)
        {
            // Hide the chrome, not the scrollable axes: touch gestures, inertia,
            // mouse wheels, and bringing focused fields into view still work.
            Add(new Style(selector => selector.OfType<ScrollViewer>().Template().OfType<ScrollBar>())
            {
                Setters = { new Setter(ScrollBar.VisibilityProperty, ScrollBarVisibility.Hidden) }
            });
        }
    }
}
