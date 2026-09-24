using ComicEditor.Format;

namespace ComicEditor.Editing;

public sealed class PaletteLibrary(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "palettes");
    public static Func<Task<string[]>> List { get; set; } = () => Task.FromResult(new PaletteLibrary(DefaultDirectory).Files());
    public static Func<string, Task<string>> Read { get; set; } = name => new PaletteLibrary(DefaultDirectory).Load(name);
    public static Func<string, string, Task> Write { get; set; } = (name, contents) => new PaletteLibrary(DefaultDirectory).Save(name, contents);

    public string[] Files()
    {
        Directory.CreateDirectory(DirectoryPath);
        return Directory.EnumerateFiles(DirectoryPath).Where(p => Path.GetExtension(p).Equals(".gpl", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFileName(p)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string FileName(string name)
    {
        var stem = string.Concat(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')).Trim();
        if (stem.Length == 0) stem = "Palette";
        if (stem.Length > 80) stem = stem[..80];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) stem = "Palette-" + stem;
        return stem + ".gpl";
    }
    private string PathFor(string file)
    {
        if (file != Path.GetFileName(file) || file.IndexOfAny(['/', '\\', ':']) >= 0 || !file.EndsWith(".gpl", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a palette filename inside the palette folder.");
        var path = Path.Combine(DirectoryPath, file);
        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null) throw new IOException("Palette preset files must not be symbolic links.");
        return path;
    }
    public async Task<string> Load(string name)
    {
        var path = PathFor(name);
        if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("The palette file is too large.");
        return await File.ReadAllTextAsync(path);
    }
    public async Task Save(string name, string contents)
    {
        _ = GplPalette.Parse(contents); // Validate before replacing any saved preset.
        await SessionStorage.WriteAtomic(PathFor(name), contents);
    }
}
