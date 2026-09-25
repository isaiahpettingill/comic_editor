using System.Diagnostics;
using ComicEditor.Display;
using ComicEditor.Format;

namespace ComicEditor.Rendering.Tests;

public class NativeCompilerTests
{
    private sealed class NativeCompilerFactAttribute : FactAttribute
    {
        public NativeCompilerFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("COMIC_TEST_COMPILER") is null)
                Skip = "Requires the published Native AOT compiler.";
        }
    }

    [NativeCompilerFact]
    public void PublishedCompilerPreservesArtworkAndRendersCachedMultilingualFonts()
    {
        var folder = Directory.CreateTempSubdirectory("comic native compiler ");
        try
        {
            var cache = Path.Combine(folder.FullName, "fonts"); FontFixture.SeedCache(cache);
            var scene = Cutscene.Create(320, 100);
            scene.Frames[0].Layers[0].SetPixel(0, 0, 127);
            scene.Translations.Clear();
            scene.Translations["en"] = new() { ["warning"] = "Ocean warning!" };
            scene.Translations["es"] = new() { ["warning"] = "¡Cuidado con el océano!" };
            scene.Translations["zh-CN"] = new() { ["warning"] = "海洋警告" };
            foreach (var font in new[] { "comic-shanns", "google:Anton", "google:Permanent Marker" })
            {
                scene.Frames[0].TextObjects.Clear();
                scene.Frames[0].TextObjects.Add(new TextObject
                {
                    Key = "warning",
                    FontId = font,
                    X = 2,
                    Y = 2,
                    Width = 310,
                    Height = 96,
                    FontSize = 20,
                    Bold = true,
                    Italic = true
                });
                var input = Path.Combine(folder.FullName, "source.cutscene");
                var output = Path.Combine(folder.FullName, "compiled.cutscene.runtime");
                File.WriteAllBytes(input, CutsceneFile.Write(scene));
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("COMIC_TEST_COMPILER")!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                start.Environment["COMIC_EDITOR_FONT_CACHE"] = cache;
                start.ArgumentList.Add(input); start.ArgumentList.Add(output);
                using var process = Process.Start(start)!;
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(60_000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    Assert.Fail("Native compiler timed out.");
                }
                var stderr = stderrTask.GetAwaiter().GetResult();
                var stdout = stdoutTask.GetAwaiter().GetResult();
                Assert.True(process.ExitCode == 0, $"{font}: compiler exited {process.ExitCode}. stderr: {stderr} stdout: {stdout}");
                var compiled = DisplayCutscene.Parser.ParseFrom(File.ReadAllBytes(output));
                var frame = Assert.Single(compiled.Frames);
                Assert.Equal(127, frame.IndexedArtwork[0]);
                Assert.Equal(255, frame.IndexedArtwork[1]);
                Assert.Equal(new[] { "en", "es", "zh-CN" }, compiled.Languages);
                foreach (var text in frame.Text)
                {
                    var raster = Assert.Single(text.Runs);
                    Assert.Equal(raster.Width * raster.Height, (uint)raster.Alpha.Length);
                    Assert.Contains(raster.Alpha, alpha => alpha != 0);
                }
                Assert.NotEqual(frame.Text[0].Runs[0].Alpha, frame.Text[2].Runs[0].Alpha);
            }
        }
        finally { folder.Delete(recursive: true); }
    }
}
