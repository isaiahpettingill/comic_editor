using ComicEditor.Format;
using System.Diagnostics;
using Google.Protobuf;
using Wire = ComicEditor.Wire;
const int width = 1080, height = 720;
double Measure(Action action) {
    action(); action();
    var values = new List<double>();
    for (var i = 0; i < 7; i++) { var clock = Stopwatch.StartNew(); action(); values.Add(clock.Elapsed.TotalMilliseconds); }
    return values.Order().ElementAt(3);
}
void Compare(string name, Cutscene scene) {
    byte[] png = [], qoi = [], raw = [];
    var indexedSave = Measure(() => png = CutsceneFile.Write(scene.Snapshot()));
    var indexedLoad = Measure(() => CutsceneFile.Parse(png));
    var legacy = Wire.CutsceneDocument.Parser.ParseFrom(png); legacy.Version = 3;
    foreach (var f in legacy.Frames) foreach (var l in f.Layers) {
        l.Pixels = ByteString.CopyFrom(IndexedSourcePng.Decode(l.IndexedPng.Span, scene.Width, scene.Height, scene.Palette)); l.IndexedPng = ByteString.Empty;
    }
    raw = legacy.ToByteArray();
    var rawLoad = Measure(() => CutsceneFile.Parse(raw));
    var rgba = scene.Snapshot(); rgba.ConvertToRgba();
    var rgbaSave = Measure(() => qoi = CutsceneFile.Write(rgba.Snapshot()));
    var rgbaLoad = Measure(() => CutsceneFile.Parse(qoi));
    // Exact-index QOI alternative, indices carried as opaque grayscale, not mapped colors:
    // mapping palette colors alone would lose duplicate-color slot identity.
    long indexQoiBytes = 0; double indexQoiEncode = 0, indexQoiDecode = 0;
    foreach (var f in legacy.Frames) foreach (var l in f.Layers) {
        var indices = l.Pixels.ToByteArray(); var packed = indices.Select(i => (uint)(i * 0x01010100u | 255u)).ToArray(); byte[] data = [];
        indexQoiEncode += Measure(() => data = RgbaQoi.Encode(scene.Width, scene.Height, packed));
        indexQoiDecode += Measure(() => RgbaQoi.Decode(data, scene.Width, scene.Height));
        if (!packed.SequenceEqual(RgbaQoi.Decode(data,scene.Width,scene.Height))) throw new Exception("Index QOI mismatch");
        indexQoiBytes += data.Length;
    }
    var decoded = CutsceneFile.Parse(png);
    if (!CutsceneFile.Write(decoded).SequenceEqual(png)) throw new Exception("Indexed round trip mismatch");
    Console.WriteLine($"{name}: raw={raw.Length} rawLoad={rawLoad:F2}ms; indexedPNG={png.Length} save={indexedSave:F2}ms load={indexedLoad:F2}ms; RGBA-QOI={qoi.Length} save={rgbaSave:F2}ms load={rgbaLoad:F2}ms; exact-index-QOI payload={indexQoiBytes} encode={indexQoiEncode:F2}ms decode={indexQoiDecode:F2}ms");
}
foreach (var name in new[] { "blank", "sparse", "blocks", "random" }) {
    var scene = Cutscene.Create(width, height); var layer = scene.Frames[0].Layers[0]; var rng = new Random(42);
    if (name == "sparse") for (var i=0;i<720;i++) layer.SetPixel(i,i,1);
    if (name is "blocks" or "random") for (var y=0;y<height;y++) for (var x=0;x<width;x++) layer.SetPixel(x,y,name=="random"?rng.Next(scene.Palette.Count):((x/24+y/24)%scene.Palette.Count));
    Compare(name,scene);
}
// Optional local input is never copied to the repository or fixtures.
foreach (var path in args) Compare("local-input",CutsceneFile.Parse(File.ReadAllBytes(path)));
