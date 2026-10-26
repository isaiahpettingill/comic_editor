using System.Collections;
using System.Buffers.Binary;

namespace ComicEditor.Format;

public sealed class ArtworkLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Artwork";
    public bool Visible { get; set; } = true;
    private ArtworkBuffer? pixels;
    private IList<string>? legacyRows = new List<string>();
    private RowAdapter? adapter;
    private bool rgba;

    // Compatibility boundary for old JSON/hex row consumers. Drawing, snapshots,
    // rendering and protobuf saves use the packed buffer directly, never this view.
    public IList<string> Rows
    {
        get => adapter ??= new RowAdapter(this);
        set { legacyRows = value is RowAdapter ? value.ToList() : value; pixels = null; }
    }
    public bool IsRgba
    {
        get => rgba;
        set
        {
            if (rgba == value) return;
            if (pixels is not null) { legacyRows = Rows.ToList(); pixels = null; }
            rgba = value;
        }
    }
    public int PixelStride => IsRgba ? 8 : 2;
    public int Width => Buffer.Width;
    public int Height => Buffer.Height;
    public int AllocatedTileCount => Buffer.AllocatedTileCount;

    private ArtworkBuffer Buffer
    {
        get
        {
            if (pixels is not null) return pixels;
            var rows = legacyRows ?? [];
            var width = rows.Count == 0 ? 0 : rows[0].Length / PixelStride;
            var decoded = new ArtworkBuffer(width, rows.Count, IsRgba);
            try
            {
                for (var y = 0; y < rows.Count; y++)
                {
                    if (rows[y].Length != width * PixelStride) throw new InvalidDataException("Artwork has an invalid pixel count.");
                    var bytes = Convert.FromHexString(rows[y]);
                    for (var x = 0; x < width; x++)
                        decoded.Set(x, y, IsRgba ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(x * 4, 4)) : bytes[x]);
                }
            }
            catch (FormatException ex) { throw new InvalidDataException("Artwork contains invalid hexadecimal pixels.", ex); }
            pixels = decoded; legacyRows = null;
            return decoded;
        }
    }
    public static ArtworkLayer Create(string name, int width, int height, bool rgba = false) => new()
    { Name = name, rgba = rgba, pixels = new ArtworkBuffer(width, height, rgba), legacyRows = null };

    public ArtworkLayer Snapshot() => new()
    { Id = Id, Name = Name, Visible = Visible, rgba = IsRgba, pixels = Buffer.Snapshot(), legacyRows = null };

    public void RestorePixels(ArtworkLayer source)
    { rgba = source.IsRgba; pixels = source.Buffer.Snapshot(); legacyRows = null; }

    public bool PixelsEqual(ArtworkLayer other) => Buffer.PixelsEqual(other.Buffer);

    public int Pixel(int x, int y)
    {
        if (IsRgba) throw new InvalidOperationException("Use RgbaPixel for RGBA artwork.");
        var value = Buffer.Get(x, y); return value == 255 ? -1 : (int)value;
    }
    public void SetPixel(int x, int y, int color)
    {
        if (IsRgba) throw new InvalidOperationException("Use SetRgbaPixel for RGBA artwork.");
        if (color is < -1 or > 254) throw new ArgumentOutOfRangeException(nameof(color));
        Buffer.Set(x, y, color < 0 ? 255u : (uint)color);
    }
    public uint RgbaPixel(int x, int y)
    {
        if (!IsRgba) throw new InvalidOperationException("Use Pixel for indexed artwork.");
        return Buffer.Get(x, y);
    }
    public void SetRgbaPixel(int x, int y, uint color)
    {
        if (!IsRgba) throw new InvalidOperationException("Use SetPixel for indexed artwork.");
        Buffer.Set(x, y, color);
    }
    public void ReadIndexedRow(int y, Span<byte> target) => Buffer.ReadIndexedRow(y, target);
    public void ReadRgbaRow(int y, Span<uint> target) => Buffer.ReadRgbaRow(y, target);
    internal bool HasValidPalette(int size) => Buffer.ValidPalette(size);

    private string GetRow(int y)
    {
        if (legacyRows is not null) return legacyRows[y];
        var buffer = Buffer;
        var bytes = new byte[buffer.Width * (IsRgba ? 4 : 1)];
        if (IsRgba)
        {
            var row = new uint[buffer.Width]; buffer.ReadRgbaRow(y, row);
            for (var x = 0; x < row.Length; x++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(x * 4), row[x]);
        }
        else buffer.ReadIndexedRow(y, bytes);
        return Convert.ToHexString(bytes);
    }
    private IList<string> EditableRows()
    {
        if (legacyRows is null) legacyRows = Enumerable.Range(0, Height).Select(GetRow).ToList();
        pixels = null; return legacyRows;
    }
    private sealed class RowAdapter(ArtworkLayer owner) : IList<string>
    {
        public string this[int index] { get => owner.GetRow(index); set => owner.EditableRows()[index] = value; }
        public int Count => owner.legacyRows?.Count ?? owner.Buffer.Height;
        public bool IsReadOnly => false;
        public void Add(string item) => owner.EditableRows().Add(item);
        public void Clear() => owner.EditableRows().Clear();
        public bool Contains(string item) => this.Any(row => row == item);
        public void CopyTo(string[] array, int arrayIndex) { foreach (var row in this) array[arrayIndex++] = row; }
        public IEnumerator<string> GetEnumerator() { for (var y = 0; y < Count; y++) yield return this[y]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(string item) { for (var y = 0; y < Count; y++) if (this[y] == item) return y; return -1; }
        public void Insert(int index, string item) => owner.EditableRows().Insert(index, item);
        public bool Remove(string item) => owner.EditableRows().Remove(item);
        public void RemoveAt(int index) => owner.EditableRows().RemoveAt(index);
    }
}
