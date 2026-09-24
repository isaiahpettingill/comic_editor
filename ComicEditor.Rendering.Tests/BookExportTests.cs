using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task BookExportDefaultsToPreviewLanguage()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show(); _ = Capture(window);
            State(view).Language = "pt";
            Invoke(view, "ExportBook", BookFormat.Epub); _ = Capture(window);
            Assert.Equal("pt", Named<ComboBox>(window, "ExportLanguage").SelectedItem);
            Invoke(view, "CloseModal");
            Assert.Equal("pt", State(view).Language);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BookFormatsContainEveryFrameInSelectedLanguage()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(160, 90);
            scene.Frames[0].Layers[0].SetPixel(2, 3, 0);
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "line", X = 5, Y = 5, Width = 150, Height = 50, FontSize = 16, Color = 0 });
            scene.Translations["en"]["line"] = "Hello";
            scene.Translations["es"]["line"] = "Hola";
            scene.Frames.Add(Frame.Create(160, 90));
            scene.Frames[1].Layers[0].SetPixel(20, 30, 3);
            using var english = new MemoryStream(); using var spanish = new MemoryStream();
            PngExporter.Write(english, scene, 0, "en"); PngExporter.Write(spanish, scene, 0, "es");
            Assert.NotEqual(english.ToArray(), spanish.ToArray());

            foreach (var format in new[] { BookFormat.Cbz, BookFormat.Epub })
            {
                using var result = new MemoryStream(); BookExporter.Write(result, scene, "es", format);
                result.Position = 0; using var zip = new ZipArchive(result, ZipArchiveMode.Read, leaveOpen: true);
                if (format == BookFormat.Epub)
                {
                    Assert.Equal("mimetype", zip.Entries[0].FullName);
                    Assert.Equal("application/epub+zip", Read(zip.GetEntry("mimetype")!));
                    Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
                    var container = XDocument.Parse(Read(zip.GetEntry("META-INF/container.xml")!));
                    Assert.Equal("OEBPS/content.opf", container.Descendants().Single(e => e.Name.LocalName == "rootfile").Attribute("full-path")?.Value);
                    var package = XDocument.Parse(Read(zip.GetEntry("OEBPS/content.opf")!));
                    Assert.Equal("es", package.Descendants().Single(e => e.Name.LocalName == "language").Value);
                    Assert.Equal("pre-paginated", package.Descendants().Single(e => e.Attribute("property")?.Value == "rendition:layout").Value);
                    Assert.Equal(2, package.Descendants().Count(e => e.Name.LocalName == "itemref"));
                    Assert.Equal("width=160, height=90", XDocument.Parse(Read(zip.GetEntry("OEBPS/page-0001.xhtml")!))
                        .Descendants().Single(e => e.Name.LocalName == "meta").Attribute("content")?.Value);
                    XDocument.Parse(Read(zip.GetEntry("OEBPS/nav.xhtml")!));
                }
                var prefix = format == BookFormat.Epub ? "OEBPS/images/" : "";
                Assert.Equal(spanish.ToArray(), ReadBytes(zip.GetEntry(prefix + "frame-0001.png")!));
                using var second = new MemoryStream(); PngExporter.Write(second, scene, 1, "es");
                Assert.Equal(second.ToArray(), ReadBytes(zip.GetEntry(prefix + "frame-0002.png")!));
            }

            using var pdf = new MemoryStream(); BookExporter.Write(pdf, scene, "es", BookFormat.Pdf);
            var bytes = pdf.ToArray();
            Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(bytes));
            Assert.Contains("/Count 2", Encoding.ASCII.GetString(bytes));
            Assert.Contains("/Indexed /DeviceRGB", Encoding.ASCII.GetString(bytes));
            Assert.EndsWith("%%EOF\n", Encoding.ASCII.GetString(bytes));
            if (Environment.GetEnvironmentVariable("COMIC_BOOK_SAMPLE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "sample.pdf"), bytes);
                using var epub = new MemoryStream(); BookExporter.Write(epub, scene, "es", BookFormat.Epub);
                File.WriteAllBytes(Path.Combine(directory, "sample.epub"), epub.ToArray());
            }
        }, CancellationToken.None);
    }

    private static string Read(ZipArchiveEntry entry) => Encoding.UTF8.GetString(ReadBytes(entry));
    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var output = new MemoryStream(); using var input = entry.Open(); input.CopyTo(output); return output.ToArray();
    }
}
