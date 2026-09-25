namespace ComicEditor.Format;

public static class Raster
{
    public static void Dot(ArtworkLayer layer, int x, int y, int color, int radius = 0)
    {
        var height = layer.Rows.Count;
        var width = layer.Width;
        for (var py = Math.Max(0, y - radius); py <= Math.Min(height - 1, y + radius); py++)
            for (var px = Math.Max(0, x - radius); px <= Math.Min(width - 1, x + radius); px++)
                if ((px - x) * (px - x) + (py - y) * (py - y) <= radius * radius || radius == 0)
                    layer.SetPixel(px, py, color);
    }

    public static void Line(ArtworkLayer layer, int x0, int y0, int x1, int y1, int color, int radius = 0)
    {
        var dx = Math.Abs(x1 - x0); var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1; var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Dot(layer, x0, y0, color, radius);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }

    public static void Rectangle(ArtworkLayer layer, int x0, int y0, int x1, int y1, int color)
    {
        Line(layer, x0, y0, x1, y0, color);
        Line(layer, x1, y0, x1, y1, color);
        Line(layer, x1, y1, x0, y1, color);
        Line(layer, x0, y1, x0, y0, color);
    }

    public static void Ellipse(ArtworkLayer layer, int x0, int y0, int x1, int y1, int color)
    {
        var cx = (x0 + x1) / 2.0; var cy = (y0 + y1) / 2.0;
        var rx = Math.Abs(x1 - x0) / 2.0; var ry = Math.Abs(y1 - y0) / 2.0;
        var steps = Math.Max(24, (int)(2 * Math.PI * Math.Max(rx, ry) * 2));
        var previousX = (int)Math.Round(cx + rx); var previousY = (int)Math.Round(cy);
        for (var i = 1; i <= steps; i++)
        {
            var angle = 2 * Math.PI * i / steps;
            var x = (int)Math.Round(cx + rx * Math.Cos(angle));
            var y = (int)Math.Round(cy + ry * Math.Sin(angle));
            Line(layer, previousX, previousY, x, y, color);
            previousX = x; previousY = y;
        }
    }

    public static void Fill(ArtworkLayer layer, int x, int y, int color, IReadOnlyList<string>? palette = null, int opacity = 255)
    {
        var width = layer.Width; var height = layer.Rows.Count;
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        if (layer.IsRgba)
        {
            var pixels = new uint[width * height];
            for (var py = 0; py < height; py++)
                for (var px = 0; px < width; px++) pixels[py * width + px] = layer.RgbaPixel(px, py);
            var sourceRgba = pixels[y * width + x];
            var targetRgba = color < 0 ? 0u : RgbaColor.Blend(RgbaColor.Parse(palette?[color] ?? throw new ArgumentException("RGBA fill requires the palette.")), sourceRgba, opacity);
            if (sourceRgba == targetRgba) return;
            var queueRgba = new Queue<(int X, int Y)>(); queueRgba.Enqueue((x, y)); pixels[y * width + x] = targetRgba;
            while (queueRgba.TryDequeue(out var point))
                foreach (var (nx, ny) in new[] { (point.X - 1, point.Y), (point.X + 1, point.Y), (point.X, point.Y - 1), (point.X, point.Y + 1) })
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height && pixels[ny * width + nx] == sourceRgba)
                    { pixels[ny * width + nx] = targetRgba; queueRgba.Enqueue((nx, ny)); }
            for (var py = 0; py < height; py++)
            {
                var row = new char[width * 8];
                for (var px = 0; px < width; px++) RgbaColor.Hex(pixels[py * width + px]).AsSpan(1).CopyTo(row.AsSpan(px * 8, 8));
                layer.Rows[py] = new string(row);
            }
            return;
        }
        var source = layer.Pixel(x, y);
        if (source == color) return;
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue((x, y));
        layer.SetPixel(x, y, color);
        while (pending.TryDequeue(out var point))
        {
            foreach (var (nx, ny) in new[] { (point.X - 1, point.Y), (point.X + 1, point.Y), (point.X, point.Y - 1), (point.X, point.Y + 1) })
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && layer.Pixel(nx, ny) == source)
                {
                    layer.SetPixel(nx, ny, color);
                    pending.Enqueue((nx, ny));
                }
        }
    }
}
