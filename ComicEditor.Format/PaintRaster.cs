namespace ComicEditor.Format;

public enum BrushTip { Round, Square, Slash, Backslash, Horizontal, Vertical }
public enum ShapeFill { Outline, Solid }

public static class PaintRaster
{
    // Batch edits by row: storing hex rows should not allocate a whole row for every pixel.
    private sealed class Surface(ArtworkLayer layer, IReadOnlyList<string>? palette = null, int opacity = 255) : IDisposable
    {
        private readonly Dictionary<int, char[]> rows = new();
        public void Pixel(int x, int y, int color)
        {
            if (y < 0 || y >= layer.Rows.Count || x < 0 || x >= layer.Width) return;
            if (!rows.TryGetValue(y, out var row)) rows[y] = row = layer.Rows[y].ToCharArray();
            if (layer.IsRgba)
            {
                var source = color < 0 ? 0u : RgbaColor.Parse(palette?[color] ?? throw new ArgumentException("RGBA painting requires the palette."));
                var offset = x * 8;
                var destination = Convert.ToUInt32(new string(row, offset, 8), 16);
                var result = color < 0 ? 0u : RgbaColor.Blend(source, destination, opacity);
                RgbaColor.Hex(result).AsSpan(1).CopyTo(row.AsSpan(offset, 8));
                return;
            }
            const string digits = "0123456789ABCDEF"; var index = color < 0 ? 255 : color;
            row[x * 2] = digits[index >> 4]; row[x * 2 + 1] = digits[index & 15];
        }
        public void Dispose() { foreach (var (y, row) in rows) layer.Rows[y] = new string(row); }
    }
    public static void Stamp(ArtworkLayer layer, int x, int y, int color, int size, BrushTip tip, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        using var surface = new Surface(layer, palette, opacity); Stamp(surface, x, y, color, size, tip);
    }
    private static void Stamp(Surface surface, int x, int y, int color, int size, BrushTip tip)
    {
        size = Math.Clamp(size, 1, 64);
        var lo = -size / 2; var hi = lo + size - 1; var center = (lo + hi) / 2.0;
        for (var dy = lo; dy <= hi; dy++)
            for (var dx = lo; dx <= hi; dx++)
            {
                var inside = tip switch
                {
                    BrushTip.Round => Math.Pow(dx - center, 2) + Math.Pow(dy - center, 2) <= size * size / 4.0,
                    BrushTip.Slash => dx + dy == lo + hi,
                    BrushTip.Backslash => dx == dy,
                    BrushTip.Horizontal => dy == 0,
                    BrushTip.Vertical => dx == 0,
                    _ => true
                };
                if (inside) surface.Pixel(x + dx, y + dy, color);
            }
    }

    public static void Stroke(ArtworkLayer layer, int x0, int y0, int x1, int y1, int color, int size, BrushTip tip, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        using var surface = new Surface(layer, palette, opacity); Stroke(surface, x0, y0, x1, y1, color, size, tip);
    }
    private static void Stroke(Surface surface, int x0, int y0, int x1, int y1, int color, int size, BrushTip tip)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : (double)i / steps;
            Stamp(surface, (int)Math.Round(x0 + (x1 - x0) * t), (int)Math.Round(y0 + (y1 - y0) * t), color, size, tip);
        }
    }

    public static void Spray(ArtworkLayer layer, int x, int y, int color, int size, int density, Random random, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        using var surface = new Surface(layer, palette, opacity);
        var radius = Math.Max(1, size / 2.0);
        for (var i = 0; i < density; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2; var distance = Math.Sqrt(random.NextDouble()) * radius;
            surface.Pixel(x + (int)Math.Round(Math.Cos(angle) * distance), y + (int)Math.Round(Math.Sin(angle) * distance), color);
        }
    }

    public static bool Inside(IReadOnlyList<(double X, double Y)> points, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    public static void Polygon(ArtworkLayer layer, IReadOnlyList<(double X, double Y)> points, int color, int size, ShapeFill fill, bool closed = true, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        if (points.Count < 2) return;
        using var surface = new Surface(layer, palette, opacity);
        if (fill == ShapeFill.Solid && closed && points.Count >= 3)
        {
            var top = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
            var bottom = Math.Min(layer.Rows.Count - 1, (int)Math.Ceiling(points.Max(p => p.Y)));
            var width = layer.Width;
            // Scanline fill: intersections are computed once per row, not per pixel.
            for (var y = top; y <= bottom; y++)
            {
                var intersections = new List<double>();
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i]; var b = points[(i + 1) % points.Count]; var py = y + .5;
                    if ((a.Y > py) != (b.Y > py)) intersections.Add(a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                intersections.Sort();
                for (var i = 0; i + 1 < intersections.Count; i += 2)
                    for (var x = Math.Max(0, (int)Math.Ceiling(intersections[i] - .5)); x <= Math.Min(width - 1, (int)Math.Floor(intersections[i + 1] - .5)); x++) surface.Pixel(x, y, color);
            }
        }
        for (var i = 0; i < points.Count - (closed ? 0 : 1); i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            Stroke(surface, (int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(b.X), (int)Math.Round(b.Y), color, size, BrushTip.Round);
        }
    }

    public static List<(double X, double Y)> Shape(string kind, int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1); var top = Math.Min(y0, y1); var right = Math.Max(x0, x1); var bottom = Math.Max(y0, y1);
        if (kind == "Rectangle") return [(left, top), (right, top), (right, bottom), (left, bottom)];
        var rx = (right - left) / 2.0; var ry = (bottom - top) / 2.0;
        var points = new List<(double X, double Y)>();
        if (kind == "Ellipse")
        {
            var steps = Math.Max(24, (int)(Math.PI * Math.Max(rx, ry) * 2));
            for (var i = 0; i < steps; i++) { var angle = i * Math.PI * 2 / steps; points.Add((left + rx + rx * Math.Cos(angle), top + ry + ry * Math.Sin(angle))); }
        }
        else
        {
            var radius = Math.Min(12, Math.Min(rx, ry));
            var centers = new[] { (right - radius, bottom - radius), (left + radius, bottom - radius), (left + radius, top + radius), (right - radius, top + radius) };
            for (var corner = 0; corner < 4; corner++)
                for (var i = 0; i <= 16; i++) { var angle = (corner + i / 16.0) * Math.PI / 2; points.Add((centers[corner].Item1 + radius * Math.Cos(angle), centers[corner].Item2 + radius * Math.Sin(angle))); }
        }
        return points;
    }

    public static void Curve(ArtworkLayer layer, (double X, double Y) start, (double X, double Y) end,
        (double X, double Y) c1, (double X, double Y) c2, int color, int size, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        using var surface = new Surface(layer, palette, opacity);
        var length = Math.Abs(c1.X - start.X) + Math.Abs(c1.Y - start.Y) + Math.Abs(c2.X - c1.X) + Math.Abs(c2.Y - c1.Y) + Math.Abs(end.X - c2.X) + Math.Abs(end.Y - c2.Y);
        var steps = Math.Max(1, (int)Math.Ceiling(length * 2)); var previous = start;
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps; var u = 1 - t;
            var x = u * u * u * start.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * end.X;
            var y = u * u * u * start.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * end.Y;
            Stroke(surface, (int)Math.Round(previous.X), (int)Math.Round(previous.Y), (int)Math.Round(x), (int)Math.Round(y), color, size, BrushTip.Round); previous = (x, y);
        }
    }

    public static void Dither(ArtworkLayer layer, int x0, int y0, int x1, int y1, int color, int size, int density, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        int[] bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        using var surface = new Surface(layer, palette, opacity);
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        var radius = Math.Clamp(size, 1, 64) / 2;
        for (var i = 0; i <= steps; i++)
        {
            var x = steps == 0 ? x0 : x0 + (x1 - x0) * i / steps;
            var y = steps == 0 ? y0 : y0 + (y1 - y0) * i / steps;
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                    if (dx * dx + dy * dy <= radius * radius && bayer[((y + dy) & 3) * 4 + ((x + dx) & 3)] < density)
                        surface.Pixel(x + dx, y + dy, color);
        }
    }

    public static void Scramble(ArtworkLayer layer, int x0, int y0, int x1, int y1, int size, Random random)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)); var radius = Math.Max(1, size / 2);
        for (var i = 0; i <= steps; i++)
        {
            var x = steps == 0 ? x0 : x0 + (x1 - x0) * i / steps;
            var y = steps == 0 ? y0 : y0 + (y1 - y0) * i / steps;
            for (var n = 0; n < size; n++)
            {
                var ax = x + random.Next(-radius, radius + 1); var ay = y + random.Next(-radius, radius + 1);
                var bx = x + random.Next(-radius, radius + 1); var by = y + random.Next(-radius, radius + 1);
                if (ax < 0 || ay < 0 || bx < 0 || by < 0 || ax >= layer.Width || bx >= layer.Width || ay >= layer.Rows.Count || by >= layer.Rows.Count) continue;
                if (layer.IsRgba)
                {
                    var first = layer.RgbaPixel(ax, ay); var second = layer.RgbaPixel(bx, by);
                    layer.SetRgbaPixel(ax, ay, second); layer.SetRgbaPixel(bx, by, first);
                }
                else
                {
                    var first = layer.Pixel(ax, ay); var second = layer.Pixel(bx, by);
                    layer.SetPixel(ax, ay, second); layer.SetPixel(bx, by, first);
                }
            }
        }
    }
}
