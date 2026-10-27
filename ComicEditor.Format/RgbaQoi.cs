using System.Buffers;
using System.Buffers.Binary;

namespace ComicEditor.Format;

/// <summary>Bounded, straight-alpha adapter for the pinned MIT QoiFu codec.</summary>
public static class RgbaQoi
{
    private static ReadOnlySpan<byte> Padding => [0, 0, 0, 0, 0, 0, 0, 1];

    public static byte[] Encode(int width, int height, ReadOnlySpan<uint> rgba)
    {
        var count = PixelCount(width, height);
        if (rgba.Length != count) throw new ArgumentException("Invalid RGBA pixel count.", nameof(rgba));
        var argb = ArrayPool<int>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++) argb[i] = unchecked((int)(rgba[i] >> 8 | rgba[i] << 24));
            var encoder = new QOIEncoder();
            if (!encoder.Encode(width, height, argb, alpha: true, linearColorspace: false))
                throw new InvalidDataException("QOI encoding failed.");
            return encoder.GetEncoded().AsSpan(0, encoder.GetEncodedSize()).ToArray();
        }
        finally { ArrayPool<int>.Shared.Return(argb); }
    }

    public static uint[] Decode(ReadOnlySpan<byte> qoi, int width, int height)
    {
        var count = PixelCount(width, height);
        Validate(qoi, width, height, count);
        var decoder = new QOIDecoder();
        // Upstream takes a byte array. Check all bounds before copying or decoding.
        var encoded = qoi.ToArray();
        if (!decoder.Decode(encoded, encoded.Length)) throw new InvalidDataException("Invalid QOI image.");
        var argb = decoder.GetPixels();
        var rgba = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var pixel = unchecked((uint)argb[i]);
            rgba[i] = pixel << 8 | pixel >> 24;
        }
        return rgba;
    }

    private static int PixelCount(int width, int height)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048)
            throw new InvalidDataException("QOI dimensions must be between 1 and 2048 pixels.");
        return checked(width * height);
    }

    // Structural preflight, not another codec: no pixel reconstruction or color
    // index implementation. Prevent chunks from consuming the mandatory trailer.
    private static void Validate(ReadOnlySpan<byte> qoi, int width, int height, int count)
    {
        if (qoi.Length < 23 || qoi.Length > 22 + count * 5 || !qoi[..4].SequenceEqual("qoif"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(qoi[4..8]) != width ||
            BinaryPrimitives.ReadUInt32BigEndian(qoi[8..12]) != height ||
            qoi[12] is not (3 or 4) || qoi[13] > 1 || !qoi[^8..].SequenceEqual(Padding))
            throw new InvalidDataException("Invalid QOI header, dimensions, or end marker.");
        var offset = 14; var end = qoi.Length - 8; var remaining = count;
        while (remaining > 0)
        {
            if (offset >= end) throw new InvalidDataException("Truncated QOI image.");
            var tag = qoi[offset++];
            var extra = tag == 0xff ? 4 : tag == 0xfe ? 3 : (tag & 0xc0) == 0x80 ? 1 : 0;
            if (extra > end - offset) throw new InvalidDataException("Truncated QOI chunk.");
            offset += extra;
            var pixels = tag is >= 0xc0 and < 0xfe ? (tag & 0x3f) + 1 : 1;
            if (pixels > remaining) throw new InvalidDataException("QOI run exceeds image dimensions.");
            remaining -= pixels;
        }
        if (offset != end) throw new InvalidDataException("QOI contains trailing pixel data.");
    }
}
