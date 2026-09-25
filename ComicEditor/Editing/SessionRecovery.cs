using System.Text.Json;
using System.Text.Json.Serialization;
using ComicEditor.Format;

namespace ComicEditor.Editing;

public sealed class SessionSnapshot
{
    public byte[] Project { get; set; } = [];
    public string? FileName { get; set; }
    public string? Bookmark { get; set; }
    public string? LocalPath { get; set; }
    public string? DiskHash { get; set; }
    public int Frame { get; set; }
    public bool Dirty { get; set; }

    public static SessionSnapshot Capture(EditorState editor, byte[]? project = null)
    {
        project ??= CutsceneFile.Write(editor.Scene);
        return new SessionSnapshot
        {
            Project = project,
            FileName = editor.FileName,
            Frame = editor.FrameIndex,
            Dirty = editor.DiffersFromSaved(project)
        };
    }
    public void Restore(EditorState editor)
    {
        editor.Load(Project, FileName);
        editor.SelectFrame(Math.Clamp(Frame, 0, editor.Scene.Frames.Count - 1));
        if (Dirty) editor.MarkUnsaved();
    }
    public string Serialize() => JsonSerializer.Serialize(this, SessionJson.Default.SessionSnapshot);
    public static SessionSnapshot? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize(json, SessionJson.Default.SessionSnapshot);
}

[JsonSerializable(typeof(SessionSnapshot))]
internal partial class SessionJson : JsonSerializerContext { }

public static class SessionStorage
{
    public static bool Enabled { get; set; } = true;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "last-session.json");
    public static Func<Task<string?>> Read { get; set; } = async () => File.Exists(FilePath) ? await File.ReadAllTextAsync(FilePath) : null;
    public static Func<string, Task> Write { get; set; } = json => WriteAtomic(FilePath, json);
    // Platform hosts can flush when entering the background.
    public static Func<Task>? Flush { get; set; }

    public static async Task WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
