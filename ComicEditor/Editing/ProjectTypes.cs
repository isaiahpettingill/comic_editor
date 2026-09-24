using Avalonia.Platform.Storage;

namespace ComicEditor.Editing;

public static class ProjectTypes
{
    public const string MimeType = "application/vnd.comiceditor.cutscene";
    public const string AppleType = "org.comiceditor.cutscene";
    public static FilePickerFileType Editable { get; } = new("ComicEditor cutscene (.ctsc, .cutscene)")
    {
        Patterns = ["*.ctsc", "*.cutscene"],
        MimeTypes = [MimeType, "application/octet-stream"],
        AppleUniformTypeIdentifiers = [AppleType, "public.data"]
    };
    public static bool IsProject(string name) => Path.GetExtension(name).Equals(".ctsc", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(name).Equals(".cutscene", StringComparison.OrdinalIgnoreCase);
}
