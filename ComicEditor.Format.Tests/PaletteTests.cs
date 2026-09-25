using Google.Protobuf;

namespace ComicEditor.Format.Tests;

public sealed class PaletteTests
{
    [Fact]
    public void GplReadsNamesWhitespaceAndLegacyFilesAndWritesPortableText()
    {
        var palette = GplPalette.Parse("\uFEFFGIMP Palette\r\nName: Céu\r\nColumns: 0\r\n#comment\r\n 1\t2  3  azul claro\r\n255 254 253\r\n");
        Assert.Equal("Céu", palette.Name); Assert.Equal(0, palette.Columns);
        Assert.Equal(new GplColor("#010203", "azul claro"), palette.Colors[0]);
        Assert.Equal("#FFFEFD", palette.Colors[1].Hex);
        var restored = GplPalette.Parse(palette.Write()); Assert.Equal(palette.Colors, restored.Colors);
        Assert.DoesNotContain('\r', palette.Write());
        Assert.Equal("Legacy", GplPalette.Parse("GIMP Palette\n#old format\n0 0 0\n255 255 255", "Legacy").Name);
    }

    [Theory]
    [InlineData("wrong\n0 0 0\n1 1 1")]
    [InlineData("GIMP Palette\n0 0 0")]
    [InlineData("GIMP Palette\n-1 0 0\n1 1 1")]
    [InlineData("GIMP Palette\n256 0 0\n1 1 1")]
    [InlineData("GIMP Palette\n1 2\n1 1 1")]
    [InlineData("GIMP Palette\nColumns: 256\n0 0 0\n1 1 1")]
    public void GplRejectsInvalidPalettes(string text) => Assert.Throws<InvalidDataException>(() => GplPalette.Parse(text));

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(128)]
    [InlineData(255)]
    public void VariablePalettesRoundTripWithReservedTransparency(int count)
    {
        var scene = Cutscene.Create(2, 1); scene.Palette = Enumerable.Range(0, count).Select(i => $"#{i:X6}").ToList();
        scene.Frames[0].Layers[0].SetPixel(0, 0, count - 1);
        var bytes = CutsceneFile.Write(scene); var result = CutsceneFile.Parse(bytes);
        Assert.Equal(3, result.Version); Assert.Equal(count, result.Palette.Count);
        Assert.Equal(count - 1, result.Frames[0].Layers[0].Pixel(0, 0)); Assert.Equal(-1, result.Frames[0].Layers[0].Pixel(1, 0));
        var wire = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(bytes);
        Assert.Equal(new byte[] { (byte)(count - 1), 255 }, wire.Frames[0].Layers[0].Pixels.ToByteArray());
        var gpl = new GplPalette("Test", scene.Palette.Select(hex => new GplColor(hex)).ToArray());
        Assert.Equal(count, GplPalette.Parse(gpl.Write()).Colors.Count);
    }

    [Fact]
    public void IndexedModeRejects256ColorsAndStillReadsVersionTwo()
    {
        var tooMany = "GIMP Palette\n" + string.Join('\n', Enumerable.Repeat("0 0 0", 256));
        Assert.Equal(256, GplPalette.Parse(tooMany).Colors.Count);
        var scene = Cutscene.Create(1, 1); scene.Palette = Enumerable.Repeat("#000000", 256).ToList();
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Write(scene));
        var old = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(Cutscene.Create(2, 1))); old.Version = 2;
        old.Frames[0].Layers[0].Pixels = ByteString.CopyFrom([127, 255]);
        var restored = CutsceneFile.Parse(old.ToByteArray()); Assert.Equal(3, restored.Version);
        Assert.Equal(127, restored.Frames[0].Layers[0].Pixel(0, 0)); Assert.Equal(-1, restored.Frames[0].Layers[0].Pixel(1, 0));
    }

    [Fact]
    public void GplPreservesAlphaInPortableComments()
    {
        var source = new GplPalette("RGBA", [new("#FF000080", "red"), new("#0000FFFF", "blue")]);
        var text = source.Write();
        Assert.Contains("255   0   0", text);
        Assert.Equal(source.Colors, GplPalette.Parse(text).Colors);
    }
}
