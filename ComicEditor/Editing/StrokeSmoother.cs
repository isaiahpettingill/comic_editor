using Avalonia;

namespace ComicEditor.Editing;

// Distance-adaptive low-pass filter: steady slow strokes, less lag on fast movement.
public sealed class StrokeSmoother(Point start)
{
    private Point filtered = start;
    public Point Add(Point point)
    {
        var distance = Math.Sqrt(Math.Pow(point.X - filtered.X, 2) + Math.Pow(point.Y - filtered.Y, 2));
        var alpha = Math.Clamp(distance / 12, .25, .8);
        filtered += (point - filtered) * alpha;
        return filtered;
    }
}
