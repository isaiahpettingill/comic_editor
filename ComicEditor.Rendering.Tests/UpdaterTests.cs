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
    private sealed class SignedBinaryFactAttribute : FactAttribute
    {
        public SignedBinaryFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("COMIC_TEST_SIGNED_BINARY") is null)
                Skip = "Requires a release executable signed by the CI signing step.";
        }
    }

    [SignedBinaryFact]
    public void VerifyPublishedDesktopSignature()
    {
        var path = Environment.GetEnvironmentVariable("COMIC_TEST_SIGNED_BINARY")!;
        using var file = File.OpenRead(path);
        var hash = SHA256.HashData(file);
        var signature = File.ReadAllBytes(path + ".sig");
        ReleaseSignature.Verify(hash, signature, ReleaseSignature.PublicKey);
        hash[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => ReleaseSignature.Verify(hash, signature, ReleaseSignature.PublicKey));
    }

    private sealed class WindowsInstallerFactAttribute : FactAttribute
    {
        public WindowsInstallerFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("COMIC_TEST_SETUP") is null)
                Skip = "Requires the Windows job's compiled NSIS installer.";
        }
    }

    [WindowsInstallerFact]
    public void RealNsisPackageStagesAndSwapsThroughTheUpdater()
    {
        using var temp = new Temporary();
        var package = Environment.GetEnvironmentVariable("COMIC_TEST_SETUP")!;
        var payload = Environment.GetEnvironmentVariable("COMIC_TEST_SETUP_PAYLOAD")!;
        var next = ReleaseClient.ReadInstallation(payload)!;
        var bytes = File.ReadAllBytes(package);
        var release = new UpdateRelease(next.Version, "win-x64", ReleaseClient.AssetName("win-x64")!, new Uri("https://example.test/setup"), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var target = Path.Combine(temp.Root, "installed app"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "update.json"), Manifest("win-x64", "0.0.0"));
        File.WriteAllText(Path.Combine(target, "ComicEditor.Desktop.exe"), "previous executable");
        File.WriteAllText(Path.Combine(target, "my-project.cutscene"), "user file");
        File.WriteAllText(Path.Combine(target, "ComicEditor.Desktop.pdb"), "old debug symbols");
        File.WriteAllText(Path.Combine(target, "my-game.pdb"), "user debug symbols");
        var work = Path.Combine(temp.Root, "work"); var plan = UpdateInstaller.Prepare(package, release, target, work);
        WindowsSetup.Stage(plan); UpdateInstaller.Apply(plan);
        Assert.Equal(next, ReleaseClient.ReadInstallation(target));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(target, "my-project.cutscene")));
        Assert.False(File.Exists(Path.Combine(target, "ComicEditor.Desktop.pdb")));
        Assert.Equal("user debug symbols", File.ReadAllText(Path.Combine(target, "my-game.pdb")));
        Assert.True(File.Exists(Path.Combine(plan.Backup, "ComicEditor.Desktop.pdb")));
        Assert.Equal("previous executable", File.ReadAllText(Path.Combine(plan.Backup, plan.Executable)));
        UpdateInstaller.Complete(Path.Combine(work, "plan.json"), target);
        Assert.False(Directory.Exists(plan.Backup));
    }

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
            }, new JsonObject
            {
                ["name"] = name + ".sig",
                ["size"] = ReleaseSignature.Size,
                ["browser_download_url"] = $"https://github.com/{ReleaseClient.Repository}/releases/download/v1.2.3/{name}.sig"
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
    [Fact]
    public void WindowsDefaultsToInstallerEvenWhenLegacyZipIsListedFirst()
    {
        var release = Release("win-x64", [1, 2, 3]);
        var zip = (JsonObject)release["assets"]![0]!.DeepClone(); zip["name"] = "ComicEditor-win-x64.zip";
        ((JsonArray)release["assets"]!).Insert(0, zip);
        Assert.Equal("ComicEditor-win-x64-setup.exe", ReleaseClient.Select(release.ToJsonString(), new(new Version(1, 0), "win-x64"))!.AssetName);
    }

    [Fact]
    public void WindowsInstallerStagingVerifiesBytesAndUsesUnquotedFinalDestination()
    {
        using var temp = new Temporary(); var target = Path.Combine(temp.Root, "installed app"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "update.json"), Manifest("win-x64", "1.0.0"));
        File.WriteAllText(Path.Combine(target, "ComicEditor.Desktop.exe"), "old executable");
        File.WriteAllText(Path.Combine(target, "my-project.cutscene"), "user file");
        byte[] bytes = [77, 90, 1, 2, 3]; var archive = Path.Combine(temp.Root, "setup.exe"); File.WriteAllBytes(archive, bytes);
        var release = ReleaseClient.Select(Release("win-x64", bytes).ToJsonString(), new(new Version(1, 0), "win-x64"))!;
        var work = Path.Combine(temp.Root, "work"); var plan = UpdateInstaller.Prepare(archive, release, target, work);
        Assert.Equal("user file", File.ReadAllText(Path.Combine(plan.Prepared, "my-project.cutscene")));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(target, plan.Executable)));
        plan = UpdateInstaller.ReadPlan(Path.Combine(work, "plan.json")); WindowsSetup.Verify(plan);
        Assert.Equal("/S /STAGE /D=" + plan.Prepared, WindowsSetup.Command(plan, register: false).Arguments);
        Assert.Equal("/S /REGISTER /VERSION=1.2.3 /D=" + plan.Target, WindowsSetup.Command(plan, register: true).Arguments);
        Assert.False(WindowsSetup.Command(plan, false).UseShellExecute);
        File.AppendAllText(plan.InstallerPackage!, "tampered"); Assert.Throws<InvalidDataException>(() => WindowsSetup.Verify(plan));
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
    private sealed class Handler(byte[] bytes, byte[] signature, HttpStatusCode signatureStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? new HttpResponseMessage(signatureStatus) { Content = new ByteArrayContent(signature) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    public async Task DownloadsVerifyDigestAndLengthAndCleanPartialFiles(bool corrupt, long size)
    {
        using var temp = new Temporary(); byte[] bytes = [1, 2, 3];
        using var rsa = RSA.Create(3072);
        using var http = new HttpClient(new Handler(bytes, rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
        var release = new UpdateRelease(new Version(1, 2, 3), "win-x64", ReleaseClient.AssetName("win-x64")!, new Uri("https://example.test/package"), corrupt ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes)), size);
        if (corrupt || size != bytes.Length) await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseClient.DownloadSigned(release, temp.Root, rsa.ExportSubjectPublicKeyInfoPem(), client: http));
        else Assert.Equal(bytes, await File.ReadAllBytesAsync(await ReleaseClient.DownloadSigned(release, temp.Root, rsa.ExportSubjectPublicKeyInfoPem(), client: http)));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.partial"));
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("wrong-key")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("missing")]
    public async Task SignatureFailurePreservesExistingDownloadEvenWithMatchingGithubChecksum(string failure)
    {
        using var temp = new Temporary();
        using var rsa = RSA.Create(3072);
        byte[] bytes = [1, 2, 3];
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        if (failure == "tampered") bytes[0] ^= 1;
        if (failure == "wrong-key") { using var wrong = RSA.Create(3072); signature = wrong.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
        if (failure == "truncated") signature = signature[..^1];
        if (failure == "oversized") signature = [.. signature, 0];
        using var http = new HttpClient(new Handler(bytes, signature, failure == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        var release = new UpdateRelease(new Version(1, 2, 3), "win-x64", ReleaseClient.AssetName("win-x64")!, new Uri("https://example.test/package"), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var existing = Path.Combine(temp.Root, release.AssetName); File.WriteAllText(existing, "previous verified download");
        var error = await Record.ExceptionAsync(() => ReleaseClient.DownloadSigned(release, temp.Root, rsa.ExportSubjectPublicKeyInfoPem(), client: http));
        Assert.NotNull(error);
        Assert.True(error is InvalidDataException or HttpRequestException, error.ToString());
        Assert.Equal("previous verified download", File.ReadAllText(existing));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.partial"));
    }

    [Fact]
    public void RejectsReleaseWithoutSignature()
    {
        var release = Release("win-x64", [1]); ((JsonArray)release["assets"]!).RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => ReleaseClient.Select(release.ToJsonString(), new(new Version(1, 0), "win-x64")));
    }

    private static string Manifest(string runtime, string version) => new JsonObject { ["runtime"] = runtime, ["version"] = version }.ToJsonString();
    private static (UpdatePlan Plan, string Work) Prepare(Temporary temp, bool tar, bool missingExecutable = false)
    {
        var runtime = tar ? "linux-x64" : "win-x64"; var exe = tar ? "ComicEditor.Desktop" : "ComicEditor.Desktop.exe";
        var target = Path.Combine(temp.Root, "installed app"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "update.json"), Manifest(runtime, "1.0.0"));
        File.WriteAllText(Path.Combine(target, exe), "old executable");
        File.WriteAllText(Path.Combine(target, "my-project.cutscene"), "preserve this user file");
        var archive = Path.Combine(temp.Root, tar ? ReleaseClient.AssetName(runtime)! : "ComicEditor-win-x64.zip");
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
    public void LinuxManagedUpdateRepairsLauncherAndReplacesIconSymlink()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new Temporary();
        var data = Path.Combine(temp.Root, "data");
        var root = Path.Combine(data, "comic-editor");
        var app = Path.Combine(root, "releases", "build.test", "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(root, ".installer-owned"), "ComicEditor per-user installation v1\n");
        var template = Path.Combine(root, "comic-editor.desktop");
        File.WriteAllText(template, "[Desktop Entry]\nType=Application\nName=ComicEditor\nX-ComicEditor-Managed=true\n");
        File.WriteAllText(Path.Combine(app, "comic-editor.svg"), "<svg>old icon</svg>");
        File.WriteAllText(Path.Combine(app, "update.json"), Manifest("linux-x64", "1.0.0"));
        File.WriteAllText(Path.Combine(app, "ComicEditor.Desktop"), "old executable");
        Directory.CreateSymbolicLink(Path.Combine(root, "current"), app);
        var desktop = Path.Combine(data, "applications", "org.comiceditor.storyboard.desktop");
        var icon = Path.Combine(data, "icons", "hicolor", "scalable", "apps", "org.comiceditor.storyboard.svg");
        Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllText(desktop, "damaged desktop entry");
        File.CreateSymbolicLink(icon, Path.Combine(root, "current", "comic-editor.svg"));

        Assert.Equal(root, LinuxDesktopIntegration.ManagedRoot(Path.Combine(root, "current")));
        var archive = Path.Combine(temp.Root, "ComicEditor-linux-x64.tar.gz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new TarWriter(gzip))
            foreach (var (name, contents) in new Dictionary<string, string>
            {
                ["update.json"] = Manifest("linux-x64", "1.2.3"),
                ["ComicEditor.Desktop"] = "new executable",
                ["comic-editor.svg"] = "<svg>new icon</svg>"
            })
            {
                using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = dataStream, Mode = (UnixFileMode)0x1ed });
            }
        var bytes = File.ReadAllBytes(archive);
        var release = new UpdateRelease(new Version(1, 2, 3), "linux-x64", Path.GetFileName(archive),
            new Uri("https://example.test/archive"), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var work = Path.Combine(temp.Root, "work");
        var plan = UpdateInstaller.Prepare(archive, release, Path.Combine(root, "current"), work);
        UpdateInstaller.Apply(plan);
        LinuxDesktopIntegration.Repair(plan.Target);
        Assert.Equal("new executable", File.ReadAllText(Path.Combine(root, "current", "ComicEditor.Desktop")));
        Assert.Equal(File.ReadAllText(template), File.ReadAllText(desktop));
        Assert.Contains("StartupWMClass=org.comiceditor.storyboard", File.ReadAllText(desktop));
        Assert.Null(new FileInfo(icon).LinkTarget);
        Assert.Equal("<svg>new icon</svg>", File.ReadAllText(icon));
        UpdateInstaller.Complete(Path.Combine(work, "plan.json"), Path.Combine(root, "current"));
        Assert.False(Directory.Exists(plan.Backup));
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
    public void LinuxHelperRollsBackWhenUpdatedExecutableCannotStart()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new Temporary(); var (plan, work) = Prepare(temp, tar: true);
        plan.ParentProcess = int.MaxValue;
        var planFile = Path.Combine(work, "plan.json");
        File.WriteAllText(planFile, System.Text.Json.JsonSerializer.Serialize(plan, UpdateJson.Default.UpdatePlan));
        File.WriteAllText(Path.Combine(work, "apply.approved"), plan.Token);
        Assert.Equal(1, UpdateInstaller.RunHelper(planFile));
        Assert.Equal("old executable", File.ReadAllText(Path.Combine(plan.Target, plan.Executable)));
        Assert.False(Directory.Exists(plan.Backup));
        Assert.StartsWith("Update failed:", File.ReadAllText(Path.Combine(work, "install.log")));
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
