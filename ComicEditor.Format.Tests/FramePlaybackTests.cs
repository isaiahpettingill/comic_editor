using Google.Protobuf;

namespace ComicEditor.Format.Tests;

public sealed class FramePlaybackTests
{
    [Fact]
    public void RoundTripAndSnapshotsPreserveTimingAndRequirementsAndLegacyGetsDefaults()
    {
        var scene = Cutscene.Create(2, 2);
        scene.Frames[0].DurationMs = 80; scene.Frames[0].Requirement = "not PLAYER_A";
        var result = CutsceneFile.Parse(CutsceneFile.Write(scene));
        Assert.Equal(80, result.Frames[0].DurationMs); Assert.Equal("not PLAYER_A", result.Frames[0].Requirement);
        Assert.Equal(80, scene.Snapshot().Frames[0].DurationMs); Assert.Equal("not PLAYER_A", scene.Frames[0].Snapshot().Requirement);
        var wire = Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(scene));
        wire.Frames[0].DurationMs = 0; wire.Frames[0].Req = "";
        var legacy = CutsceneFile.Parse(wire.ToByteArray()).Frames[0];
        Assert.Equal(1000, legacy.DurationMs); Assert.Equal("always", legacy.Requirement);
    }

    [Fact]
    public void ConditionsAreCaseSensitiveAndSkippedFramesConsumeNoTime()
    {
        var scene = Cutscene.Create(2, 2); scene.Frames.Clear();
        foreach (var req in new[] { "never", "A", "not A", "always", "B" })
        { var frame = Frame.Create(2, 2); frame.Requirement = req; frame.DurationMs = 50; scene.Frames.Add(frame); }
        Assert.Equal(new[] { "A", "B" }, FramePlayback.Variables(scene));
        Assert.Equal(new[] { 1, 3 }, FramePlayback.SelectFrames(scene, ["A"]));
        Assert.Equal(new[] { 2, 3 }, FramePlayback.SelectFrames(scene, ["a"]));
        var player = new FramePlayer(scene, ["A"]);
        Assert.Equal(1, player.FrameIndex);
        player.Advance(TimeSpan.FromMilliseconds(49)); Assert.Equal(1, player.FrameIndex);
        player.Advance(TimeSpan.FromMilliseconds(1)); Assert.Equal(3, player.FrameIndex);
        player.Advance(TimeSpan.FromMilliseconds(50)); Assert.True(player.Finished); Assert.Equal(-1, player.FrameIndex);
        player = new FramePlayer(scene, ["A", "B"]); player.Advance(TimeSpan.FromMilliseconds(100)); Assert.Equal(4, player.FrameIndex);
        player.Advance(TimeSpan.FromMilliseconds(50)); Assert.True(player.Finished);
        scene.Frames.ForEach(f => f.Requirement = "never"); Assert.True(new FramePlayer(scene, []).Finished);
    }

    [Theory]
    [InlineData("not ")]
    [InlineData("A && B")]
    [InlineData("not not A")]
    [InlineData("1A")]
    public void InvalidRequirementsFailValidation(string req)
    { var scene = Cutscene.Create(2, 2); scene.Frames[0].Requirement = req; Assert.Throws<InvalidDataException>(scene.Validate); }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3600001)]
    public void InvalidDurationsFailValidation(int duration)
    { var scene = Cutscene.Create(2, 2); scene.Frames[0].DurationMs = duration; Assert.Throws<InvalidDataException>(scene.Validate); }
}
