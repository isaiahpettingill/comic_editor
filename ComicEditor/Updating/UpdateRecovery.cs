using System.Text.Json;
using System.Text.Json.Serialization;
using ComicEditor.Editing;
using ComicEditor.Format;

namespace ComicEditor.Updating;

public sealed class UpdateRecovery
{
    public string TargetVersion { get; set; } = "";
    public string? FileName { get; set; }
    public int Frame { get; set; }
    public bool Dirty { get; set; }
    public byte[] Project { get; set; } = [];
    public static void Save(EditorState editor, Version target, string directory)
    {
        Directory.CreateDirectory(directory);
        var snapshot = new UpdateRecovery { TargetVersion = target.ToString(), FileName = editor.FileName, Frame = editor.FrameIndex, Dirty = editor.IsDirty, Project = CutsceneFile.Write(editor.Scene) };
        File.WriteAllBytes(Path.Combine(directory, "resume.cutscene.tmp"), snapshot.Project);
        File.Move(Path.Combine(directory, "resume.cutscene.tmp"), Path.Combine(directory, "resume.cutscene"), overwrite: true);
        var file = Path.Combine(directory, "resume.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(snapshot, RecoveryJson.Default.UpdateRecovery));
        File.Move(file + ".tmp", file, overwrite: true);
    }
    public static bool Restore(EditorState editor, string directory, bool explicitRestart, bool restoreWorkspace = true)
    {
        var file = Path.Combine(directory, "resume.json");
        if (!File.Exists(file)) return false;
        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(file), RecoveryJson.Default.UpdateRecovery) ?? throw new InvalidDataException("The update recovery file is empty.");
        if (!explicitRestart && (!Version.TryParse(snapshot.TargetVersion, out var target) || ReleaseClient.Normalize(ReleaseClient.AppVersion) != ReleaseClient.Normalize(target))) return false;
        if (restoreWorkspace)
        {
            editor.Load(snapshot.Project, snapshot.FileName); editor.SelectFrame(snapshot.Frame);
            if (snapshot.Dirty) editor.MarkUnsaved();
        }
        // Keep a recoverable copy until the next update; consume the automatic restore only once.
        File.Move(file, Path.Combine(directory, "last-session.json"), overwrite: true);
        return restoreWorkspace;
    }
}
[JsonSerializable(typeof(UpdateRecovery))]
internal partial class RecoveryJson : JsonSerializerContext { }
