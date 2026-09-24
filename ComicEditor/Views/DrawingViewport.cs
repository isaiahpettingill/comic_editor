using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input.GestureRecognizers;

namespace ComicEditor.Views;

/// <summary>
/// The standard ScrollViewer treats pen drags as scroll gestures and captures the
/// pointer once the canvas overflows. Navigation on this canvas is handled by
/// MainView instead, so keep the normal ScrollViewer layout without that gesture.
/// </summary>
internal sealed class DrawingViewport : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find("PART_ContentPresenter") is not ScrollContentPresenter presenter) return;
        foreach (var recognizer in presenter.GestureRecognizers.OfType<ScrollGestureRecognizer>().ToArray())
            presenter.GestureRecognizers.Remove(recognizer);
    }
}
