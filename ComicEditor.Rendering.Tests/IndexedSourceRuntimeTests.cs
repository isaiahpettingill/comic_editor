using System.Diagnostics;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;
using Google.Protobuf;

namespace ComicEditor.Rendering.Tests;

// Avalonia has a process-global UI dispatcher; compiler fixture generation must
// not create a second headless app alongside the canvas tests.
[CollectionDefinition("Avalonia app", DisableParallelization = true)]
public sealed class AvaloniaAppCollection { }

[Collection("Avalonia app")]
public partial class CanvasTests
{
    [Fact]
    public Task IndexedPngAndLegacyRawSourceCompileIdentically() => VerifyIndexedSourceRuntime(null);

    internal static async Task VerifyIndexedSourceRuntime(string? folder)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(96, 64);
            scene.Palette = ["#123456", "#123456", "#FFFFFF"];
            scene.Frames[0].Layers[0].SetPixel(0, 0, 0);
            scene.Frames[0].Layers[0].SetPixel(1, 0, 1);
            scene.Frames[0].AttachAudio(QoaAudio.Encode(new short[800], 1, 8000));
            scene.Translations["en"]["hello"] = "Hello";
            scene.Translations["es"] = new() { ["hello"] = "Hola" };
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "hello", X = 4, Y = 4, Width = 88, Height = 56, FontSize = 16, Color = 2 });
            var source = CutsceneFile.Write(scene);
            var legacy = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(source);
            Assert.Equal(6u, legacy.Version);
            foreach (var frame in legacy.Frames) foreach (var layer in frame.Layers)
            {
                layer.Pixels = ByteString.CopyFrom(IndexedSourcePng.Decode(layer.IndexedPng.Span, scene.Width, scene.Height, scene.Palette));
                layer.IndexedPng = ByteString.Empty;
            }
            legacy.Version = 3;
            var oldSource = legacy.ToByteArray();
            var runtime = CborExporter.Compile(CutsceneFile.Parse(source));
            Assert.Equal(runtime, CborExporter.Compile(CutsceneFile.Parse(oldSource)));
            var display = DisplayCompiler.Compile(CutsceneFile.Parse(source));
            Assert.Equal(2u, display.Version);
            Assert.Equal(display.ToByteArray(), DisplayCompiler.Compile(CutsceneFile.Parse(oldSource)).ToByteArray());
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "indexed-v6.ctsc"), source);
                File.WriteAllBytes(Path.Combine(folder, "indexed-v3.ctsc"), oldSource);
                File.WriteAllBytes(Path.Combine(folder, "expected.cbor"), runtime);
            }
        }, CancellationToken.None);
    }
}

[Collection("Avalonia app")]
public partial class NativeCompilerTests
{
    [NativeCompilerFact]
    public async Task OfficialOfflineCompilerAcceptsIndexedPngAndLegacySources()
    {
        var compiler = Environment.GetEnvironmentVariable("COMIC_TEST_COMPILER");
        Assert.False(string.IsNullOrEmpty(compiler));
        var folder = Directory.CreateTempSubdirectory("comic-indexed-compiler-").FullName;
        try
        {
            await CanvasTests.VerifyIndexedSourceRuntime(folder);
            foreach (var name in new[] { "indexed-v6", "indexed-v3" })
            {
                var output = Path.Combine(folder, name + ".cbor");
                var start = new ProcessStartInfo(compiler) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                start.ArgumentList.Add("--offline"); start.ArgumentList.Add(Path.Combine(folder, name + ".ctsc")); start.ArgumentList.Add(output);
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(60_000)) { process.Kill(true); process.WaitForExit(); Assert.Fail("Compiler timed out"); }
                Assert.True(process.ExitCode == 0, await error + await stdout);
                Assert.Equal(File.ReadAllBytes(Path.Combine(folder, "expected.cbor")), File.ReadAllBytes(output));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
