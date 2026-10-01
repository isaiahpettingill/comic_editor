namespace ComicEditor.Format;

/// <summary>A one-shot timeline. Excluded frames consume no time; elapsed time can cross several frames.</summary>
public sealed class FramePlayer
{
    private readonly (int Index, int Duration)[] frames;
    private int position;
    private double elapsedMs;
    public bool Finished => position >= frames.Length;
    public int FrameIndex => Finished ? -1 : frames[position].Index;

    public FramePlayer(Cutscene scene, IEnumerable<string> variables)
    {
        scene.Validate();
        frames = FramePlayback.SelectFrames(scene, variables).Select(index => (index, scene.Frames[index].DurationMs)).ToArray();
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        elapsedMs += elapsed.TotalMilliseconds;
        while (!Finished && elapsedMs >= frames[position].Duration)
        { elapsedMs -= frames[position].Duration; position++; }
    }
}
