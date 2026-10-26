using System.Diagnostics;
using ComicEditor.Format;
using Old = BaselineComicEditor.Format;
foreach (var dense in new[] { false, true }) foreach (var count in new[] { 1, 12 })
{
    var old = Old.Cutscene.Create(720, 480, true);
    var random = new Random(42);
    if (dense) for (var y = 0; y < 480; y++)
    {
        var data = new byte[720 * 4]; random.NextBytes(data);
        old.Frames[0].Layers[0].Rows[y] = Convert.ToHexString(data);
    }
    else for (var x = 0; x < 720; x++) old.Frames[0].Layers[0].SetRgbaPixel(x, 240, 0xff0000ff);
    for (var i = 1; i < count; i++) old.Frames.Add(old.Frames[0].Snapshot());
    var before = Old.CutsceneFile.Write(old); var scene = CutsceneFile.Parse(before);
    if (!before.AsSpan().SequenceEqual(CutsceneFile.Write(scene))) throw new Exception("Wire bytes differ");
    Console.WriteLine($"{(dense ? "dense" : "sparse")} {count} frames 720x480 RGBA; byte-identical {before.Length} bytes");
    Measure("save before", () => Old.CutsceneFile.Write(old));
    Measure("save after", () => CutsceneFile.Write(scene));
    Measure("snapshot before", () => old.Snapshot(), 50);
    Measure("snapshot after", () => scene.Snapshot(), 50);
}
var oldStroke = Old.ArtworkLayer.Create("art",720,480,true); var stroke = ArtworkLayer.Create("art",720,480,true);
var oldPass = 1; var newPass = 1;
Measure("1000 changed pixel stroke before", () => { oldPass++; for(var i=0;i<1000;i++) oldStroke.SetRgbaPixel(i%720, i/720, (uint)(i + oldPass)); return oldStroke; });
Measure("1000 changed pixel stroke after", () => { newPass++; for(var i=0;i<1000;i++) stroke.SetRgbaPixel(i%720, i/720, (uint)(i + newPass)); return stroke; });
void Measure(string label, Func<object> action, int runs=3)
{
    action(); GC.Collect(); GC.WaitForPendingFinalizers();
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var time = Stopwatch.StartNew();
    for (var i = 0; i < runs; i++) action();
    Console.WriteLine($"  {label}: {time.Elapsed.TotalMilliseconds / runs:F3} ms; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)runs / 1024:F2} KiB allocated");
}
