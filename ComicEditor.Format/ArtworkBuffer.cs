using System.Buffers.Binary;

namespace ComicEditor.Format;

// Fixed-size sparse tiles avoid a per-pixel dictionary and make dense artwork
// equally cheap to address. Missing tiles are transparent. Snapshots share tiles;
// the first edit after a snapshot clones only that tile, never the whole canvas.
internal sealed class ArtworkBuffer
{
    internal const int TileSize = 32;
    private sealed class Tile(byte[] data, int populated)
    {
        public byte[] Data = data;
        public int Populated = populated;
        public bool Shared;
    }
    private readonly Tile?[] tiles;
    private readonly int columns, stride;
    public int Width { get; }
    public int Height { get; }
    public bool IsRgba { get; }
    private uint Empty => IsRgba ? 0u : 255u;
    internal int AllocatedTileCount => tiles.Count(tile => tile is not null);

    public ArtworkBuffer(int width, int height, bool rgba)
    {
        if (width < 0 || height < 0 || width > 2048 || height > 2048) throw new InvalidDataException("Invalid artwork dimensions.");
        Width = width; Height = height; IsRgba = rgba; stride = rgba ? 4 : 1;
        columns = (width + TileSize - 1) / TileSize;
        tiles = new Tile?[columns * ((height + TileSize - 1) / TileSize)];
    }
    private ArtworkBuffer(ArtworkBuffer source)
    {
        Width = source.Width; Height = source.Height; IsRgba = source.IsRgba;
        columns = source.columns; stride = source.stride;
        tiles = (Tile?[])source.tiles.Clone();
        foreach (var tile in tiles) if (tile is not null) tile.Shared = true;
    }
    public ArtworkBuffer Snapshot() => new(this);
    public uint Get(int x, int y)
    {
        Check(x, y);
        var tile = tiles[y / TileSize * columns + x / TileSize];
        if (tile is null) return Empty;
        var offset = (y % TileSize * TileSize + x % TileSize) * stride;
        return IsRgba ? BinaryPrimitives.ReadUInt32LittleEndian(tile.Data.AsSpan(offset, 4)) : tile.Data[offset];
    }
    public void Set(int x, int y, uint value)
    {
        Check(x, y);
        var slot = y / TileSize * columns + x / TileSize;
        var tile = tiles[slot];
        var offset = (y % TileSize * TileSize + x % TileSize) * stride;
        var previous = tile is null ? Empty : IsRgba ? BinaryPrimitives.ReadUInt32LittleEndian(tile.Data.AsSpan(offset, 4)) : tile.Data[offset];
        if (previous == value) return;
        if (tile is null)
        {
            var data = new byte[TileSize * TileSize * stride];
            if (!IsRgba) Array.Fill(data, (byte)255);
            tiles[slot] = tile = new Tile(data, 0);
        }
        else if (tile.Shared) tiles[slot] = tile = new Tile((byte[])tile.Data.Clone(), tile.Populated);
        if (previous == Empty) tile.Populated++;
        if (value == Empty && --tile.Populated == 0) { tiles[slot] = null; return; }
        if (IsRgba) BinaryPrimitives.WriteUInt32LittleEndian(tile.Data.AsSpan(offset, 4), value);
        else tile.Data[offset] = checked((byte)value);
    }
    public void ReadIndexedRow(int y, Span<byte> target)
    {
        if (IsRgba || target.Length < Width || y < 0 || y >= Height) throw new ArgumentException("Invalid indexed row.");
        target[..Width].Fill(255);
        for (var column = 0; column < columns; column++)
        {
            var tile = tiles[y / TileSize * columns + column]; if (tile is null) continue;
            var x = column * TileSize; var count = Math.Min(TileSize, Width - x);
            tile.Data.AsSpan(y % TileSize * TileSize, count).CopyTo(target[x..]);
        }
    }
    public void ReadRgbaRow(int y, Span<uint> target)
    {
        if (!IsRgba || target.Length < Width || y < 0 || y >= Height) throw new ArgumentException("Invalid RGBA row.");
        target[..Width].Clear();
        for (var column = 0; column < columns; column++)
        {
            var tile = tiles[y / TileSize * columns + column]; if (tile is null) continue;
            var x = column * TileSize; var count = Math.Min(TileSize, Width - x);
            var offset = y % TileSize * TileSize * 4;
            for (var i = 0; i < count; i++) target[x + i] = BinaryPrimitives.ReadUInt32LittleEndian(tile.Data.AsSpan(offset + i * 4, 4));
        }
    }
    public bool PixelsEqual(ArtworkBuffer other)
    {
        if (Width != other.Width || Height != other.Height || IsRgba != other.IsRgba) return false;
        for (var i = 0; i < tiles.Length; i++)
        {
            var a = tiles[i]; var b = other.tiles[i];
            if (ReferenceEquals(a, b)) continue;
            if (a is null || b is null || !a.Data.AsSpan().SequenceEqual(b.Data)) return false;
        }
        return true;
    }
    public bool ValidPalette(int paletteSize)
    {
        if (IsRgba) return true;
        foreach (var tile in tiles)
            if (tile is not null)
                foreach (var value in tile.Data)
                    if (value != 255 && value >= paletteSize) return false;
        return true;
    }
    private void Check(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x));
    }
}
