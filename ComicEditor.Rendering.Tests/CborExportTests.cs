using System.Buffers.Binary;
using System.Text;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlatExportPreservesArtworkAndLocalizedText(bool rgba)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(160, 80, rgba: rgba);
            scene.Palette = rgba ? ["#FF000080", "#00000080"] : ["#FF0000", "#000000"];
            if (rgba) scene.Frames[0].Layers[0].SetRgbaPixel(0, 0, 0xff000080);
            else scene.Frames[0].Layers[0].SetPixel(0, 0, 0);
            var hidden = ArtworkLayer.Create("hidden", 160, 80, rgba: rgba); hidden.Visible = false;
            if (rgba) hidden.SetRgbaPixel(0, 0, 0x000000ff); else hidden.SetPixel(0, 0, 1);
            scene.Frames[0].Layers.Add(hidden);
            scene.Translations.Clear();
            scene.Translations["en"] = new() { ["hello"] = "Hello" };
            scene.Translations["es"] = new() { ["hello"] = "Hola" };
            scene.Translations["fr"] = new(); scene.FallbackLanguage = "en";
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "hello", X = 10, Y = 10, Width = 130, Height = 60, FontSize = 20, Color = 1 });
            var compiled = DisplayCompiler.Compile(scene);
            var bytes = CborExporter.Compile(scene); var position = 0;
            var root = (Dictionary<string, object>)Read(bytes, ref position);
            Assert.Equal(bytes.Length, position);
            Assert.Equal(1u, root["version"]); Assert.Equal(160u, root["width"]); Assert.Equal(80u, root["height"]);
            Assert.Equal("en", root["fallback_language"]);
            Assert.Equal(new object[] { "en", "es", "fr" }, (List<object>)root["languages"]);
            var png = (byte[])Assert.Single((List<object>)root["frames"]);
            var art = RgbaPng.Decode(png, 160, 80);
            Assert.Equal(rgba ? 0xff000080u : 0xff0000ffu, art[0]);
            Assert.All(art.Skip(1), pixel => Assert.Equal(0u, pixel)); // Base contains no text.
            var text = (List<object>)root["text"]; Assert.Equal(3, text.Count);
            for (var i = 0; i < text.Count; i++)
            {
                var entry = (Dictionary<string, object>)text[i]; var run = Assert.Single(compiled.Frames[0].Text[i].Runs);
                Assert.Equal(0u, entry["frame"]); Assert.Equal(compiled.Languages[i], entry["language"]);
                Assert.Equal(run.X, entry["x"]); Assert.Equal(run.Y, entry["y"]);
                var pixels = RgbaPng.Decode((byte[])entry["png"], (int)run.Width, (int)run.Height);
                for (var p = 0; p < pixels.Length; p++)
                    Assert.Equal((uint)((run.Alpha[p] * (rgba ? 128 : 255) + 127) / 255), pixels[p]);
            }
            Assert.Equal(((Dictionary<string, object>)text[0])["png"], ((Dictionary<string, object>)text[2])["png"]);
            scene.Frames[0].TextVisible = false;
            bytes = CborExporter.Compile(scene); position = 0;
            root = (Dictionary<string, object>)Read(bytes, ref position);
            Assert.Empty((List<object>)root["text"]);
        }, CancellationToken.None);
    }

    // Independent reader for the standard CBOR subset used by the wire format.
    private static object Read(byte[] data, ref int position)
    {
        var initial = data[position++]; var major = initial >> 5; var additional = initial & 31;
        uint length;
        if (additional < 24) length = (uint)additional;
        else if (additional == 24) length = data[position++];
        else if (additional == 25) { length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position, 2)); position += 2; }
        else if (additional == 26) { length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4)); position += 4; }
        else throw new InvalidDataException("Unexpected CBOR length");
        if (major == 0) return length;
        if (major is 2 or 3)
        {
            var bytes = data.AsSpan(position, checked((int)length)).ToArray(); position += bytes.Length;
            return major == 2 ? bytes : Encoding.UTF8.GetString(bytes);
        }
        if (major == 4)
        {
            var items = new List<object>(); for (var i = 0u; i < length; i++) items.Add(Read(data, ref position)); return items;
        }
        if (major == 5)
        {
            var map = new Dictionary<string, object>();
            for (var i = 0u; i < length; i++) { var key = (string)Read(data, ref position); map.Add(key, Read(data, ref position)); }
            return map;
        }
        throw new InvalidDataException("Unexpected CBOR type");
    }
}
