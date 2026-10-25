using Google.Protobuf;

namespace ComicEditor.Format.Tests;

public sealed class FrameAudioTests
{
    [Fact]
    public void NewFramesDefaultToFourSecondsButLegacyDurationsAreNotRewritten()
    {
        var scene = Cutscene.Create(2, 2); Assert.Equal(4000, scene.Frames[0].DurationMs);
        var document = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(scene));
        document.Frames[0].DurationMs = 0;
        Assert.Equal(1000, CutsceneFile.Parse(document.ToByteArray()).Frames[0].DurationMs);
        document.Frames[0].DurationMs = 1234;
        Assert.Equal(1234, CutsceneFile.Parse(document.ToByteArray()).Frames[0].DurationMs);
    }

    [Fact]
    public void AttachReplaceRemoveAndSnapshotsPreserveLongerTimingAndExactBytes()
    {
        var scene = Cutscene.Create(2, 2); var frame = scene.Frames[0];
        var audio = QoaAudio.Encode(new short[48001 * 2], 2, 48000);
        frame.DurationMs = 500; frame.AttachAudio(audio);
        Assert.Equal(1001, frame.MinimumDurationMs); Assert.Equal(1001, frame.DurationMs);
        Assert.NotSame(audio, frame.AudioQoa);
        var roundtrip = CutsceneFile.Parse(CutsceneFile.Write(scene));
        Assert.Equal(audio, roundtrip.Frames[0].AudioQoa); Assert.Equal(1001, roundtrip.Frames[0].DurationMs);
        var snapshot = scene.Snapshot(); Assert.NotSame(frame.AudioQoa, snapshot.Frames[0].AudioQoa);
        frame.DurationMs = 6000; frame.AttachAudio(QoaAudio.Encode(new short[800], 1, 8000));
        Assert.Equal(6000, frame.DurationMs); frame.RemoveAudio(); Assert.Empty(frame.AudioQoa);
        Assert.Equal(6000, frame.DurationMs); Assert.Equal(audio, snapshot.Frames[0].AudioQoa);
    }

    [Fact]
    public void TooShortOrCorruptAudioIsRejectedAndFailedReplacementIsAtomic()
    {
        var scene = Cutscene.Create(2, 2); var frame = scene.Frames[0];
        frame.AttachAudio(QoaAudio.Encode(new short[8001], 1, 8000)); var before = frame.AudioQoa;
        Assert.Throws<InvalidDataException>(() => frame.AttachAudio([1, 2, 3])); Assert.Same(before, frame.AudioQoa);
        frame.DurationMs = 1000; Assert.Throws<InvalidDataException>(scene.Validate);
        frame.DurationMs = 1001; scene.Validate();
        var player = new FramePlayer(scene, []); player.Advance(TimeSpan.FromMilliseconds(1000)); Assert.False(player.Finished);
        player.Advance(TimeSpan.FromMilliseconds(1)); Assert.True(player.Finished);
    }
}
