using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using ComicEditor.Format;

namespace ComicEditor.Rendering;

public enum BookFormat { Pdf, Epub, Cbz }

/// <summary>One rendered, localized frame per page; the editable project remains separate.</summary>
public static class BookExporter
{
    public static void Write(Stream output, Cutscene scene, string language, BookFormat format)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Frames.Count == 0) throw new InvalidDataException("A book needs at least one frame.");
        CutsceneFonts.RequireAvailable(scene, language);
        switch (format)
        {
            case BookFormat.Pdf: WritePdf(output, scene, language); break;
            case BookFormat.Epub: WriteZip(output, scene, language, epub: true); break;
            case BookFormat.Cbz: WriteZip(output, scene, language, epub: false); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void WriteZip(Stream output, Cutscene scene, string language, bool epub)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        if (epub)
        {
            // EPUB requires the first entry to be uncompressed and contain exactly this MIME type.
            Entry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Entry(zip, "META-INF/container.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>" +
                "</rootfiles></container>");
            Entry(zip, "OEBPS/content.opf", Package(scene, language));
            Entry(zip, "OEBPS/nav.xhtml", Navigation(scene.Frames.Count, language));
        }
        for (var i = 0; i < scene.Frames.Count; i++)
        {
            var name = $"frame-{i + 1:D4}.png";
            if (epub)
                Entry(zip, $"OEBPS/page-{i + 1:D4}.xhtml", Page(scene, language, i));
            using var image = zip.CreateEntry(epub ? "OEBPS/images/" + name : name, CompressionLevel.NoCompression).Open();
            PngExporter.Write(image, scene, i, language);
        }
    }

    private static void Entry(ZipArchive zip, string path, string text, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var stream = zip.CreateEntry(path, compression).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string Package(Cutscene scene, string language)
    {
        var items = new StringBuilder(); var spine = new StringBuilder();
        for (var i = 0; i < scene.Frames.Count; i++)
        {
            var number = (i + 1).ToString("D4", CultureInfo.InvariantCulture);
            items.Append($"<item id=\"page{number}\" href=\"page-{number}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            items.Append($"<item id=\"image{number}\" href=\"images/frame-{number}.png\" media-type=\"image/png\"/>");
            spine.Append($"<itemref idref=\"page{number}\"/>");
        }
        var modified = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"book-id\" xml:lang=\"" + Xml(language) + "\">" +
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            "<dc:identifier id=\"book-id\">urn:uuid:" + Guid.NewGuid().ToString("D") + "</dc:identifier>" +
            "<dc:title>Cutscene</dc:title><dc:language>" + Xml(language) + "</dc:language>" +
            "<meta property=\"dcterms:modified\">" + modified + "</meta>" +
            "<meta property=\"rendition:layout\">pre-paginated</meta>" +
            "<meta property=\"rendition:spread\">none</meta></metadata>" +
            "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>" +
            items + "</manifest><spine>" + spine + "</spine></package>";
    }

    private static string Navigation(int count, string language)
    {
        var links = new StringBuilder();
        for (var i = 0; i < count; i++)
            links.Append($"<li><a href=\"page-{i + 1:D4}.xhtml\">Frame {i + 1}</a></li>");
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"" + Xml(language) + "\">" +
            "<head><title>Contents</title></head><body><nav epub:type=\"toc\" id=\"toc\"><h1>Contents</h1><ol>" +
            links + "</ol></nav></body></html>";
    }

    private static string Page(Cutscene scene, string language, int index) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"" + Xml(language) + "\">" +
        "<head><title>Frame " + (index + 1) + "</title><meta name=\"viewport\" content=\"width=" + scene.Width + ", height=" + scene.Height +
        "\"/><style>html,body{margin:0;padding:0;width:100%;height:100%;background:white}img{display:block;width:100%;height:100%}</style></head>" +
        "<body><img src=\"images/frame-" + (index + 1).ToString("D4", CultureInfo.InvariantCulture) +
        ".png\" alt=\"Cutscene frame " + (index + 1) + "\"/></body></html>";

    private static string Xml(string value) => SecurityElement.Escape(value) ?? "";

    private static void WritePdf(Stream output, Cutscene scene, string language)
    {
        var offsets = new List<long> { 0 }; long position = 0;
        void Bytes(ReadOnlySpan<byte> data) { output.Write(data); position += data.Length; }
        void Ascii(string value) => Bytes(Encoding.ASCII.GetBytes(value));
        void Begin(int number) { offsets.Add(position); Ascii($"{number} 0 obj\n"); }
        void End() => Ascii("endobj\n");

        Ascii("%PDF-1.4\n");
        Bytes([37, 226, 227, 207, 211, 10]);
        Begin(1); Ascii("<< /Type /Catalog /Pages 2 0 R >>\n"); End();
        Begin(2); Ascii("<< /Type /Pages /Count " + scene.Frames.Count + " /Kids [");
        for (var i = 0; i < scene.Frames.Count; i++) Ascii($"{3 + 3 * i} 0 R ");
        Ascii("] >>\n"); End();
        for (var i = 0; i < scene.Frames.Count; i++)
        {
            using var png = new MemoryStream(); PngExporter.Write(png, scene, i, language);
            var image = PdfImage.FromPng(png.ToArray());
            var page = 3 + 3 * i; var imageId = page + 1; var contentId = page + 2;
            var width = scene.Width * 2; var height = scene.Height * 2;
            Begin(page);
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] " +
                $"/Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>\n"); End();
            Begin(imageId);
            Ascii($"<< /Type /XObject /Subtype /Image /Width {scene.Width} /Height {scene.Height} " +
                $"/ColorSpace [/Indexed /DeviceRGB {image.Palette.Length / 3 - 1} <{Convert.ToHexString(image.Palette)}>] " +
                $"/BitsPerComponent {image.Depth} /Filter /FlateDecode " +
                $"/DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent {image.Depth} /Columns {scene.Width} >> " +
                $"/Length {image.Compressed.Length} >>\nstream\n");
            Bytes(image.Compressed); Ascii("\nendstream\n"); End();
            var commands = Encoding.ASCII.GetBytes($"q\n{width} 0 0 {height} 0 0 cm\n/Im0 Do\nQ\n");
            Begin(contentId); Ascii($"<< /Length {commands.Length} >>\nstream\n");
            Bytes(commands); Ascii("endstream\n"); End();
        }
        var xref = position;
        Ascii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Ascii($"{offset:D10} 00000 n \n");
        Ascii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private sealed record PdfImage(byte Depth, byte[] Palette, byte[] Compressed)
    {
        public static PdfImage FromPng(byte[] png)
        {
            if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("Expected a PNG frame.");
            byte depth = 0; byte[]? palette = null;
            using var compressed = new MemoryStream();
            for (var offset = 8; offset + 12 <= png.Length;)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
                if (length < 0 || length > png.Length - offset - 12) throw new InvalidDataException("Invalid PNG chunk.");
                var type = png.AsSpan(offset + 4, 4); var data = png.AsSpan(offset + 8, length);
                if (type.SequenceEqual("IHDR"u8)) depth = data[8];
                else if (type.SequenceEqual("PLTE"u8)) palette = data.ToArray();
                else if (type.SequenceEqual("IDAT"u8)) compressed.Write(data);
                offset += 12 + length;
            }
            if (palette is null || depth is not (1 or 2 or 4 or 8) || compressed.Length == 0)
                throw new InvalidDataException("Expected an indexed PNG frame.");
            return new PdfImage(depth, palette, compressed.ToArray());
        }
    }
}
