using System.Diagnostics;
using System.Security.Cryptography;
using ComicEditor.Format;
using Old = BaselineComicEditor.Format;

Console.WriteLine("Source save/load benchmark (.NET Release,720x480 RGBA,seed42,warm2,measure5). Not UI latency; local filesystem/page cache.");
Console.WriteLine("case,frames,format,bytes,save_ms,load_ms,save_alloc_MiB,load_alloc_MiB");
var directory = Path.Combine(Path.GetTempPath(), "comic-source-bench-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
try
{
    foreach (var name in new[] { "blank", "sparse-line", "pixel-art", "gradient", "dense-noise" })
    foreach (var frameCount in new[] { 1, 12 })
    {
        var old = Old.Cutscene.Create(720, 480, true); var random = new Random(42);
        uint[] colors = [0x203060ff, 0xf0b040ff, 0x90b0e0ff, 0x50407080];
        for (var y=0;y<480;y++) for(var x=0;x<720;x++)
        {
            uint pixel = name switch {
                "sparse-line" => y == 240 ? 0xff0000ffu : 0,
                "pixel-art" => x < 40 || y < 40 || x >= 680 || y >= 440 ? 0 : colors[(x/24+y/24)%4],
                "gradient" => (uint)((x*255/719<<24)|(y*255/479<<16)|0x6080),
                "dense-noise" => (uint)random.NextInt64(1L<<32), _ => 0 };
            old.Frames[0].Layers[0].SetRgbaPixel(x,y,pixel);
        }
        for(var i=1;i<frameCount;i++) old.Frames.Add(old.Frames[0].Snapshot());
        var oldBytes=Old.CutsceneFile.Write(old); var scene=CutsceneFile.Parse(oldBytes);
        var newBytes=CutsceneFile.Write(scene); var roundTrip=CutsceneFile.Parse(newBytes);
        for(var y=0;y<480;y++) for(var x=0;x<720;x++)
            if(old.Frames[0].Layers[0].RgbaPixel(x,y)!=roundTrip.Frames[0].Layers[0].RgbaPixel(x,y)) throw new Exception("Round-trip pixels differ");
        foreach(var qoi in new[]{false,true})
        {
            var path=Path.Combine(directory,qoi?"new.ctsc":"old.ctsc");
            var bytes=qoi?newBytes:oldBytes; File.WriteAllBytes(path,bytes);
            var save=Measure(()=>{
                // New revision: immutable snapshot + complete validation/serialization,
                // content hash, existing-file conflict hash, atomic local replacement.
                var data=qoi?CutsceneFile.Write(scene.Snapshot()):Old.CutsceneFile.Write(old.Snapshot());
                _=SHA256.HashData(data); _=SHA256.HashData(File.ReadAllBytes(path));
                File.WriteAllBytes(path+".tmp",data); File.Move(path+".tmp",path,true); return data;
            });
            var load=Measure(()=>qoi?(object)CutsceneFile.Parse(File.ReadAllBytes(path)):Old.CutsceneFile.Parse(File.ReadAllBytes(path)));
            Console.WriteLine($"{name},{frameCount},{(qoi?"QOI-v5":"PNG-v4")},{bytes.Length},{save.ms:F2},{load.ms:F2},{save.mib:F2},{load.mib:F2}");
        }
    }
}
finally { Directory.Delete(directory,true); }
static (double ms,double mib) Measure(Func<object> operation)
{
    for(var i=0;i<2;i++) GC.KeepAlive(operation());
    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
    var allocation=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
    for(var i=0;i<5;i++) GC.KeepAlive(operation());
    return(watch.Elapsed.TotalMilliseconds/5,(GC.GetAllocatedBytesForCurrentThread()-allocation)/5.0/1024/1024);
}
