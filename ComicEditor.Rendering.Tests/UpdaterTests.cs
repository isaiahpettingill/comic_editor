using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Updating;

namespace ComicEditor.Rendering.Tests;

public class UpdaterTests
{
    private sealed class Temporary : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("comic update 'quotes' $").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private static JsonObject Release(string runtime, byte[] bytes)
    {
        var name = ReleaseClient.AssetName(runtime)!;
        return new JsonObject
        {
            ["tag_name"] = "v1.2.3",
            ["draft"] = false,
            ["prerelease"] = false,
            ["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = name,
                ["size"] = bytes.Length,
                ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
                ["browser_download_url"] = $"https://github.com/{ReleaseClient.Repository}/releases/download/v1.2.3/{name}"
            })
        };
    }
    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public void SelectsNewStableAssetsForEachPlatform(string runtime)
    {
        var json = Release(runtime, [1, 2, 3]);
        Assert.NotNull(ReleaseClient.Select(json.ToJsonString(), new(new Version(1, 2, 2, 0), runtime)));
        Assert.Null(ReleaseClient.Select(json.ToJsonString(), new(new Version(1, 2, 3, 0), runtime)));
        Assert.Null(ReleaseClient.Select(json.ToJsonString(), new(new Version(2, 0, 0), runtime)));
        json["prerelease"] = true; Assert.Null(ReleaseClient.Select(json.ToJsonString(), new(new Version(1, 0), runtime)));
        json["prerelease"] = false; json["draft"] = true; Assert.Null(ReleaseClient.Select(json.ToJsonString(), new(new Version(1, 0), runtime)));
    }
    [Theory]
    [InlineData("digest", "sha256:broken")]
    [InlineData("browser_download_url", "https://example.com/malicious.zip")]
    [InlineData("name", "ComicEditor-win-arm64.zip")]
    public void RejectsWrongOrUnverifiedAssets(string key, string value)
    {
        var release = Release("win-x64", [1]); release["assets"]![0]![key] = value;
        Assert.Throws<InvalidDataException>(() => ReleaseClient.Select(release.ToJsonString(), new(new Version(1, 0), "win-x64")));
    }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    public async Task DownloadsVerifyDigestAndLengthAndCleanPartialFiles(bool corrupt, long size)
    {
        using var temp = new Temporary(); byte[] bytes = [1, 2, 3];
        using var http = new HttpClient(new Handler(bytes));
        var release = new UpdateRelease(new Version(1, 2, 3), "win-x64", ReleaseClient.AssetName("win-x64")!, new Uri("https://example.test/package"), corrupt ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes)), size);
        if (corrupt || size != bytes.Length) await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseClient.Download(release, temp.Root, client: http));
        else Assert.Equal(bytes, await File.ReadAllBytesAsync(await ReleaseClient.Download(release, temp.Root, client: http)));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.partial"));
    }

    private static string Manifest(string runtime, string version) => new JsonObject { ["runtime"] = runtime, ["version"] = version }.ToJsonString();
    private static (UpdatePlan Plan, string Work) Prepare(Temporary temp, bool tar, bool missingExecutable = false)
    {
        var runtime = tar ? "linux-x64" : "win-x64"; var exe = tar ? "ComicEditor.Desktop" : "ComicEditor.Desktop.exe";
        var target = Path.Combine(temp.Root, "installed app"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "update.json"), Manifest(runtime, "1.0.0"));
        File.WriteAllText(Path.Combine(target, exe), "old executable");
        File.WriteAllText(Path.Combine(target, "my-project.cutscene"), "preserve this user file");
        var archive = Path.Combine(temp.Root, ReleaseClient.AssetName(runtime)!);
        var files = new Dictionary<string, string> { ["update.json"] = Manifest(runtime, "1.2.3"), ["compiler/tool.txt"] = "new compiler" };
        if (!missingExecutable) files[exe] = "new executable";
        using (var file = File.Create(archive))
        {
            if (tar)
            {
                using var gzip = new GZipStream(file, CompressionMode.Compress); using var writer = new TarWriter(gzip);
                foreach (var (name, contents) in files)
                {
                    using var data = new MemoryStream(Encoding.UTF8.GetBytes(contents));
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data, Mode = (UnixFileMode)0x1ed });
                }
            }
            else
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                foreach (var (name, contents) in files) { using var entry = new StreamWriter(zip.CreateEntry(name).Open()); entry.Write(contents); }
            }
        }
        var bytes = File.ReadAllBytes(archive);
        var release = new UpdateRelease(new Version(1, 2, 3), runtime, Path.GetFileName(archive), new Uri("https://example.test/archive"), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var work = Path.Combine(temp.Root, "work");
        return (UpdateInstaller.Prepare(archive, release, target, work), work);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StagesThenSwapsPreservingUserFilesAndKeepsBackupUntilReopen(bool tar)
    {
        using var temp = new Temporary(); var (plan, work) = Prepare(temp, tar);
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(plan.Target, plan.Executable)));
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(plan.Prepared, plan.Executable)));
        UpdateInstaller.Apply(plan);
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(plan.Target, plan.Executable)));
        Assert.Equal("preserve this user file", File.ReadAllText(Path.Combine(plan.Target, "my-project.cutscene")));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(plan.Backup, plan.Executable)));
        UpdateInstaller.Complete(Path.Combine(work, "plan.json"), plan.Target);
        Assert.False(Directory.Exists(plan.Backup)); Assert.True(Directory.Exists(plan.Target));
    }
    [Fact]
    public void FailedSwapRollsBackBeforeRelaunch()
    {
        using var temp = new Temporary(); var (plan, _) = Prepare(temp, false); var calls = 0;
        Assert.Throws<IOException>(() => UpdateInstaller.Apply(plan, (from, to) =>
        {
            if (++calls == 2) throw new IOException("Simulated locked installation directory.");
            Directory.Move(from, to);
        }));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(plan.Target, plan.Executable)));
        Assert.False(Directory.Exists(plan.Backup)); Assert.True(Directory.Exists(plan.Prepared));
    }
    [Fact]
    public void RejectsMissingExecutableInsteadOfReusingTheOldOne()
    {
        using var temp = new Temporary(); Assert.Throws<InvalidDataException>(() => Prepare(temp, false, missingExecutable: true));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(temp.Root, "installed app", "ComicEditor.Desktop.exe")));
        Assert.Empty(Directory.GetDirectories(temp.Root, "*.update-*"));
    }
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/escape.txt")]
    public void RejectsArchiveTraversal(string entryName)
    {
        using var temp = new Temporary(); var archive = Path.Combine(temp.Root, "evil.zip");
        using (var zip = new ZipArchive(File.Create(archive), ZipArchiveMode.Create)) { using var entry = new StreamWriter(zip.CreateEntry(entryName).Open()); entry.Write("bad"); }
        var destination = Path.Combine(temp.Root, "payload"); Directory.CreateDirectory(destination);
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(archive, destination));
        Assert.False(File.Exists(Path.Combine(temp.Root, "escape.txt")));
    }
    [Fact]
    public void RejectsTarSymlinks()
    {
        using var temp = new Temporary(); var archive = Path.Combine(temp.Root, "evil.tar.gz");
        using (var gzip = new GZipStream(File.Create(archive), CompressionMode.Compress))
        using (var tar = new TarWriter(gzip)) tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(archive, temp.Root));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateRestoresOpenProjectAndDirtyStateExactlyOnce(bool dirty)
    {
        using var temp = new Temporary(); var editor = new EditorState();
        editor.AddFrame(true); editor.Scene.Frames[1].Layers[0].SetPixel(5, 6, 3); editor.FileName = "my cutscene.cutscene";
        if (!dirty) editor.MarkSaved();
        UpdateRecovery.Save(editor, new Version(99, 0, 0), temp.Root);
        var restored = new EditorState(); Assert.False(UpdateRecovery.Restore(restored, temp.Root, explicitRestart: false));
        Assert.True(UpdateRecovery.Restore(restored, temp.Root, explicitRestart: true));
        Assert.Equal(1, restored.FrameIndex); Assert.Equal(3, restored.Layer.Pixel(5, 6)); Assert.Equal(dirty, restored.IsDirty);
        Assert.Equal(editor.FileName, restored.FileName); Assert.False(restored.CanUndo);
        Assert.False(UpdateRecovery.Restore(restored, temp.Root, explicitRestart: true));
        Assert.Equal(2, CutsceneFile.Parse(File.ReadAllBytes(Path.Combine(temp.Root, "resume.cutscene"))).Frames.Count);
    }
}
