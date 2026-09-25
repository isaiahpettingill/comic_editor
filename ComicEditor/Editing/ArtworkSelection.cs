using ComicEditor.Format;

namespace ComicEditor.Editing;

/// <summary>Selection of pixels on one artwork layer; transparent pixels paste transparently.</summary>
public sealed class ArtworkSelection
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    public bool[] Mask { get; }
    public ArtworkLayer Owner { get; set; }
    public ArtworkSelection(ArtworkLayer layer, int x, int y, int width, int height, IReadOnlyList<(double X, double Y)>? polygon = null, bool ellipse = false)
    {
        Owner = layer; X = x; Y = y; Width = width; Height = height;
        Pixels = new uint[width * height]; Mask = new bool[Pixels.Length];
        for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var i = py * width + px;
                var nx = (px + .5 - width / 2.0) / (width / 2.0);
                var ny = (py + .5 - height / 2.0) / (height / 2.0);
                Mask[i] = (!ellipse || nx * nx + ny * ny <= 1) && (polygon is null || PaintRaster.Inside(polygon, x + px + .5, y + py + .5));
                Pixels[i] = layer.IsRgba ? layer.RgbaPixel(x + px, y + py) : (uint)(layer.Pixel(x + px, y + py) + 1);
            }
    }
    private ArtworkSelection(ArtworkSelection other, ArtworkLayer owner)
    { Owner = owner; X = other.X; Y = other.Y; Width = other.Width; Height = other.Height; Pixels = (uint[])other.Pixels.Clone(); Mask = (bool[])other.Mask.Clone(); }
    public ArtworkSelection Copy(ArtworkLayer owner) => new(this, owner);
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height && Mask[(y - Y) * Width + x - X];
    public void Clear() => Apply(clear: true);
    public void Paste() => Apply(clear: false);
    private void Apply(bool clear)
    {
        var layerWidth = Owner.Width;
        for (var py = 0; py < Height; py++)
            for (var px = 0; px < Width; px++)
            {
                var i = py * Width + px; var x = X + px; var y = Y + py;
                if (!Mask[i] || x < 0 || y < 0 || x >= layerWidth || y >= Owner.Rows.Count) continue;
                if (Owner.IsRgba)
                { if (clear || (Pixels[i] & 255) != 0) Owner.SetRgbaPixel(x, y, clear ? 0 : Pixels[i]); }
                else if (clear || Pixels[i] > 0) Owner.SetPixel(x, y, clear ? -1 : (int)Pixels[i] - 1);
            }
    }
}
