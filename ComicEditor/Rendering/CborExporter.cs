using System.Buffers.Binary;
using System.Text;
using ComicEditor.Format;

namespace ComicEditor.Rendering;

/// <summary>Self-contained PNG artwork and localized text, encoded without reflection for Native AOT.</summary>
public static class CborExporter
{
    public static byte[] Compile(Cutscene scene)
    {
        var display = DisplayCompiler.Compile(scene);
        using var output = new MemoryStream();
        var writer = new Writer(output);
        writer.Map(8);
        writer.Text("version"); writer.Number(2);
        writer.Text("width"); writer.Number(display.CanvasWidth);
        writer.Text("height"); writer.Number(display.CanvasHeight);
        writer.Text("languages"); writer.Array(display.Languages.Count);
        foreach (var language in display.Languages) writer.Text(language);
        writer.Text("fallback_language"); writer.Text(display.Languages[(int)display.FallbackLanguageIndex]);
        writer.Text("vars"); writer.Array(display.Vars.Count);
        foreach (var variable in display.Vars) writer.Text(variable);
        writer.Text("frames"); writer.Array(display.Frames.Count);
        foreach (var frame in display.Frames)
        {
            writer.Map(3);
            writer.Text("duration_ms"); writer.Number(frame.DurationMs);
            writer.Text("req"); writer.Text(frame.Req);
            writer.Text("png");
            if (scene.IsRgba) writer.Bytes(frame.RgbaArtworkPng.Span);
            else
            {
                var pixels = frame.IndexedArtwork.Select(index => index == 255 ? 0u : (display.PaletteRgb[index] << 8) | 255u).ToArray();
                writer.Bytes(RgbaPng.Encode(scene.Width, scene.Height, pixels));
            }
        }
        writer.Text("text"); writer.Array(display.Frames.Sum(frame => frame.Text.Sum(text => text.Runs.Count)));
        for (var frameIndex = 0; frameIndex < display.Frames.Count; frameIndex++)
            for (var languageIndex = 0; languageIndex < display.Languages.Count; languageIndex++)
                foreach (var run in display.Frames[frameIndex].Text[languageIndex].Runs)
                {
                    var ink = scene.IsRgba ? display.PaletteRgba[(int)run.PaletteIndex] : (display.PaletteRgb[(int)run.PaletteIndex] << 8) | 255u;
                    var pixels = run.Alpha.Select(alpha => (ink & 0xffffff00u) | (((ink & 255u) * alpha + 127u) / 255u)).ToArray();
                    writer.Map(5);
                    writer.Text("frame"); writer.Number((uint)frameIndex);
                    writer.Text("language"); writer.Text(display.Languages[languageIndex]);
                    writer.Text("x"); writer.Number(run.X);
                    writer.Text("y"); writer.Number(run.Y);
                    writer.Text("png"); writer.Bytes(RgbaPng.Encode((int)run.Width, (int)run.Height, pixels));
                }
        return output.ToArray();
    }

    // Only definite-length maps/arrays, UTF-8 strings, byte strings and unsigned integers are needed.
    private sealed class Writer(Stream stream)
    {
        public void Map(int count) => Header(5, (uint)count);
        public void Array(int count) => Header(4, (uint)count);
        public void Number(uint value) => Header(0, value);
        public void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); Header(3, (uint)bytes.Length); stream.Write(bytes); }
        public void Bytes(ReadOnlySpan<byte> bytes) { Header(2, (uint)bytes.Length); stream.Write(bytes); }
        private void Header(int major, uint value)
        {
            if (value < 24) { stream.WriteByte((byte)((major << 5) | (int)value)); return; }
            var size = value <= byte.MaxValue ? 1 : value <= ushort.MaxValue ? 2 : 4;
            stream.WriteByte((byte)((major << 5) | (size == 1 ? 24 : size == 2 ? 25 : 26)));
            Span<byte> buffer = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer[(4 - size)..]);
        }
    }
}
