using Avalonia;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;
using Google.Protobuf;

var downloadFonts = args.Contains("--download-fonts");
args = args.Where(a => a != "--download-fonts").ToArray();
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: comic-compile [--download-fonts] INPUT.cutscene OUTPUT.cutscene.runtime\nResolves font references, flattens artwork and rasterizes all languages.\n--download-fonts permits downloading missing language fonts from Google Fonts; cached fonts work offline.");
    return 0;
}
if (args.Length != 2) { Console.Error.WriteLine("Usage: comic-compile [--download-fonts] INPUT.cutscene OUTPUT.cutscene.runtime"); return 2; }
try
{
    if (Path.GetFullPath(args[0]).Equals(Path.GetFullPath(args[1]), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new ArgumentException("Input and output must be different files.");
    var scene = CutsceneFile.Parse(File.ReadAllBytes(args[0]));
    Console.Error.WriteLine("Initializing renderer...");
    AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    SynchronizationContext.SetSynchronizationContext(null);
    Console.Error.WriteLine("Resolving fonts...");
    CutsceneFonts.EnsureAsync(scene).GetAwaiter().GetResult();
    if (downloadFonts)
        foreach (var font in CutsceneFonts.Missing(scene).Where(f => f.Family.Length > 0))
        {
            Console.Error.WriteLine("Downloading " + font.Family + "…");
            CutsceneFonts.LoadGoogleAsync(font.Link).GetAwaiter().GetResult();
        }
    Console.Error.WriteLine("Compiling frames...");
    var compiled = DisplayCompiler.Compile(scene);
    var target = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try { File.WriteAllBytes(temporary, compiled.ToByteArray()); File.Move(temporary, target, overwrite: true); }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
    Console.WriteLine($"Compiled {compiled.Frames.Count} frames, {compiled.Languages.Count} languages -> {target}");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("Conversion failed: " + ex.Message); return 1; }
