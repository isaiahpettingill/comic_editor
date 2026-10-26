using System.Runtime.CompilerServices;
using ComicEditor.Format;

namespace ComicEditor.Editing;

// Capture mutable metadata on the UI thread once per revision. Serialization runs
// on a worker; recovery and manual/autosave share its immutable result.
internal sealed class ProjectSnapshotCache
{
    internal sealed record Capture(Cutscene Scene, long Revision, Task<byte[]> Bytes, bool Stable);
    private readonly ConditionalWeakTable<EditorState, Entry> entries = new();
    private sealed class Entry { public Capture? Value; }

    public Capture Get(EditorState state, bool allowReuse = true)
    {
        var entry = entries.GetOrCreateValue(state);
        if (allowReuse && entry.Value is { } prior && ReferenceEquals(prior.Scene, state.Scene) && prior.Revision == state.Revision && !prior.Bytes.IsFaulted)
            return prior;
        var scene = state.Scene;
        var revision = state.Revision;
        var snapshot = scene.Snapshot();
        var capture = new Capture(scene, revision, Task.Run(() => CutsceneFile.Write(snapshot)), allowReuse);
        // A live stroke/text edit may mutate repeatedly within one undo revision.
        entry.Value = allowReuse ? capture : null;
        return capture;
    }
}
