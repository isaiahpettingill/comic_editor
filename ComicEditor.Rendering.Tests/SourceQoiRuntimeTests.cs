using System.Diagnostics;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;
using Google.Protobuf;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public Task QoiSourceAndLegacyPngSourceCompileToIdenticalPngRuntime() =>
        VerifyQoiSourceRuntime(Environment.GetEnvironmentVariable("COMIC_QOI_FIXTURES"));

    internal static async Task VerifyQoiSourceRuntime(string? folder)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(96, 64, rgba: true);
            scene.Palette = ["#FF000080", "#000000FF"];
            scene.Frames[0].Layers[0].SetRgbaPixel(0, 0, 0xff000080);
            scene.Frames[0].Layers[0].SetRgbaPixel(1, 0, 0x00ff00ff);
            scene.Frames[0].AttachAudio(QoaAudio.Encode(new short[800], 1, 8000));
            scene.Translations.Clear();
            scene.Translations["en"] = new() { ["hello"] = "Hello" };
            scene.Translations["es"] = new() { ["hello"] = "Hola" };
            scene.Translations["fr"] = new(); scene.FallbackLanguage = "en";
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "hello", X = 4, Y = 4, Width = 88, Height = 56, FontSize = 16, Color = 1 });
            var source = CutsceneFile.Write(scene);
            var legacy = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(source);
            Assert.Equal(5u, legacy.Version);
            foreach (var frame in legacy.Frames)
                foreach (var layer in frame.Layers)
                {
                    layer.RgbaPng = ByteString.CopyFrom(RgbaPng.Encode(scene.Width, scene.Height,
                        RgbaQoi.Decode(layer.RgbaQoi.Span, scene.Width, scene.Height)));
                    layer.RgbaQoi = ByteString.Empty;
                }
            legacy.Version = 4;
            var oldSource = legacy.ToByteArray();
            var runtime = CborExporter.Compile(CutsceneFile.Parse(source));
            Assert.Equal(runtime, CborExporter.Compile(CutsceneFile.Parse(oldSource)));
            var display = DisplayCompiler.Compile(CutsceneFile.Parse(source));
            Assert.Equal(3u, display.Version);
            Assert.Equal(0xff000080u, RgbaPng.Decode(display.Frames[0].RgbaArtworkPng.Span, 96, 64)[0]);
            var position = 0; var root = (Dictionary<string, object>)Read(runtime, ref position);
            Assert.Equal(2u, root["version"]);
            var frameMap = (Dictionary<string, object>)Assert.Single((List<object>)root["frames"]);
            Assert.False(frameMap.ContainsKey("qoi"));
            Assert.Equal(scene.Frames[0].AudioQoa, (byte[])frameMap["audio_qoa"]);
            Assert.Equal(0xff000080u, RgbaPng.Decode((byte[])frameMap["png"], 96, 64)[0]);
            var texts = (List<object>)root["text"]; Assert.Equal(3, texts.Count);
            foreach (var value in texts)
            {
                var text = (Dictionary<string, object>)value;
                Assert.True(text.ContainsKey("png")); Assert.False(text.ContainsKey("qoi"));
            }
            Assert.Equal(((Dictionary<string, object>)texts[0])["png"], ((Dictionary<string, object>)texts[2])["png"]);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "source-qoi-v5.ctsc"), source);
                File.WriteAllBytes(Path.Combine(folder, "source-legacy-png-v4.ctsc"), oldSource);
                File.WriteAllBytes(Path.Combine(folder, "expected-png-v2.cbor"), runtime);
            }
        }, CancellationToken.None);
    }

}

public partial class NativeCompilerTests
{
    [NativeCompilerFact]
    public async Task OfficialOfflineCompilerAcceptsQoiSourceAndPreservesPngRuntime()
    {
        var compiler = Environment.GetEnvironmentVariable("COMIC_TEST_COMPILER");
        var suppliedFixtures = Environment.GetEnvironmentVariable("COMIC_QOI_FIXTURES");
        var fixtures = suppliedFixtures ?? Directory.CreateTempSubdirectory("comic-qoi-compiler-").FullName;
        Assert.False(string.IsNullOrEmpty(compiler));
        try
        {
            await CanvasTests.VerifyQoiSourceRuntime(fixtures);
            foreach (var name in new[] { "source-qoi-v5", "source-legacy-png-v4" })
            {
                var output = Path.Combine(fixtures, name + ".cli.cbor");
                var start = new ProcessStartInfo(compiler)
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                start.ArgumentList.Add("--offline");
                start.ArgumentList.Add(Path.Combine(fixtures, name + ".ctsc")); start.ArgumentList.Add(output);
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(60_000)) { process.Kill(true); process.WaitForExit(); Assert.Fail("Compiler timed out"); }
                Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult() + stdout.GetAwaiter().GetResult());
                Assert.Equal(File.ReadAllBytes(Path.Combine(fixtures, "expected-png-v2.cbor")), File.ReadAllBytes(output));
            }
        }
        finally { if (suppliedFixtures is null) Directory.Delete(fixtures, recursive: true); }
    }
}
