using ComicEditor.Editing;
using ComicEditor.Format;

namespace ComicEditor.Rendering.Tests;

public sealed class ClipboardTests
{
    [Fact]
    public void ArtworkCopyPreservesColorsAcrossProjectsWithDifferentPalettes()
    {
        var source = Cutscene.Create(8, 8);
        source.Palette = ["#000000", "#EE2211"];
        source.Frames[0].Layers[0].SetPixel(1, 1, 1);
        var selected = new ArtworkSelection(source.Frames[0].Layers[0], 1, 1, 2, 2);
        var clipboard = new ArtworkClipboard(selected, source);
        var destination = Cutscene.Create(8, 8);
        destination.Palette = ["#FFFFFF", "#000000"];

        var pasted = clipboard.Paste(destination.Frames[0].Layers[0], destination, 3, 4, out var paletteChanged);

        Assert.True(paletteChanged);
        Assert.Equal("#EE2211", destination.Palette[destination.Frames[0].Layers[0].Pixel(3, 4)]);
        Assert.Equal(-1, destination.Frames[0].Layers[0].Pixel(4, 4));
        Assert.Equal((3, 4), (pasted.X, pasted.Y));
        destination.Validate();
    }

    [Fact]
    public void FrameCopyPreservesTextAndMakesIndependentKeysAndIds()
    {
        var source = Cutscene.Create(6, 4);
        source.Palette = ["#111111", "#FF3300"];
        source.Frames[0].Layers[0].SetPixel(2, 1, 1);
        source.Frames[0].TextObjects.Add(new TextObject { Key = "line", Color = 1 });
        source.Translations["en"]["line"] = "Copied text";
        source.Translations["zh-CN"] = new() { ["line"] = "复制" };
        var clipboard = new FrameClipboard(source, 0);
        source.Translations["en"]["line"] = "Changed later";
        var destination = Cutscene.Create(4, 3);
        destination.Palette = ["#FFFFFF", "#111111"];
        destination.Translations["en"]["line"] = "Original text";

        var first = clipboard.Paste(destination, out var paletteChanged);
        destination.Frames.Add(first);
        var second = clipboard.Paste(destination, out _);
        destination.Frames.Add(second);

        Assert.True(paletteChanged);
        Assert.NotEqual(source.Frames[0].Id, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("#FF3300", destination.Palette[first.Layers[0].Pixel(2, 1)]);
        Assert.Equal(-1, first.Layers[0].Pixel(3, 2));
        Assert.Equal("Original text", destination.Translations["en"]["line"]);
        Assert.Equal("Copied text", destination.Translations["en"][first.TextObjects[0].Key]);
        Assert.Equal("复制", destination.Translations["zh-CN"][first.TextObjects[0].Key]);
        Assert.NotEqual(first.TextObjects[0].Key, second.TextObjects[0].Key);
        destination.Validate();
    }

    [Fact]
    public void ArtworkCopyConvertsIndexedPixelsToRgba()
    {
        var source = Cutscene.Create(2, 2);
        source.Palette = ["#000000", "#CC8833"];
        source.Frames[0].Layers[0].SetPixel(0, 0, 1);
        var clipboard = new ArtworkClipboard(new ArtworkSelection(source.Frames[0].Layers[0], 0, 0, 1, 1), source);
        var destination = Cutscene.Create(2, 2, rgba: true);

        clipboard.Paste(destination.Frames[0].Layers[0], destination, 1, 1, out _);

        Assert.Equal(0xCC8833FFu, destination.Frames[0].Layers[0].RgbaPixel(1, 1));
        destination.Validate();
    }
}
