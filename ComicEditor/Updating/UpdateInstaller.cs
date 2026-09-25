using System.Diagnostics;
using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComicEditor.Updating;

public sealed class UpdatePlan
{
    public string Token { get; set; } = "";
    public string Target { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Version { get; set; } = "";
    public int ParentProcess { get; set; }
    public string? InstallerPackage { get; set; }
    public string? InstallerSha256 { get; set; }
    public long InstallerSize { get; set; }
    public string? PreviousVersion { get; set; }
    [JsonIgnore] public string Prepared => Target + ".update-" + Token;
    [JsonIgnore] public string Backup => Target + ".previous-" + Token;
    [JsonIgnore] public string Executable => Runtime.StartsWith("win-") ? "ComicEditor.Desktop.exe" : "ComicEditor.Desktop";
}
[JsonSerializable(typeof(UpdatePlan))]
internal partial class UpdateJson : JsonSerializerContext { }

public static class UpdateInstaller
{
    public static string ResolveDirectory(string directory)
    {
        var info = new DirectoryInfo(Path.GetFullPath(directory));
        return Path.TrimEndingDirectorySeparator(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName);
    }
    public static UpdatePlan Prepare(string archive, UpdateRelease release, string target, string workDirectory, CancellationToken cancellation = default)
    {
        target = ResolveDirectory(target);
        if (ReleaseClient.ReadInstallation(target) is not { } installed || installed.Runtime != release.Runtime || release.Runtime == "android-arm64")
            throw new InvalidDataException("Automatic installation requires an installed desktop release.");
        var plan = new UpdatePlan { Token = Guid.NewGuid().ToString("N"), Target = target, Runtime = release.Runtime, Version = release.Version.ToString(), ParentProcess = Environment.ProcessId };
        if (!File.Exists(Path.Combine(target, plan.Executable))) throw new InvalidDataException("The installation's executable is missing.");
        // No writes to the running installation. The sibling directory ensures same-volume renames.
        Directory.CreateDirectory(plan.Prepared);
        try
        {
            using (var package = File.OpenRead(archive))
                if (package.Length != release.Size || !Convert.ToHexString(SHA256.HashData(package)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The staged update no longer matches its verified download.");
            CopyTree(target, plan.Prepared, cancellation);
            if (release.Runtime == "win-x64" && release.AssetName == ReleaseClient.AssetName("win-x64"))
            {
                Directory.CreateDirectory(workDirectory);
                plan.InstallerPackage = Path.GetFullPath(Path.Combine(workDirectory, "setup.exe"));
                File.Copy(archive, plan.InstallerPackage, overwrite: true);
                plan.InstallerSha256 = release.Sha256; plan.InstallerSize = release.Size;
                plan.PreviousVersion = installed.Version.ToString();
            }
            else
            {
                var payload = Path.Combine(workDirectory, "payload"); Directory.CreateDirectory(payload);
                Extract(archive, payload, cancellation);
                var next = ReleaseClient.ReadInstallation(payload);
                if (next is null || next.Runtime != release.Runtime || ReleaseClient.Normalize(next.Version) != ReleaseClient.Normalize(release.Version) || !File.Exists(Path.Combine(payload, plan.Executable)))
                    throw new InvalidDataException("The downloaded package has incorrect installation metadata or is missing its executable.");
                if (release.Runtime == "linux-x64") CheckLinuxDependencies(payload);
                CopyTree(payload, plan.Prepared, cancellation, overwrite: true);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(plan.Prepared, plan.Executable), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.WriteAllText(Path.Combine(plan.Prepared, ".update-token"), plan.Token);
            Directory.CreateDirectory(workDirectory);
            File.WriteAllText(Path.Combine(workDirectory, "plan.json"), JsonSerializer.Serialize(plan, UpdateJson.Default.UpdatePlan));
            return plan;
        }
        catch { Directory.Delete(plan.Prepared, recursive: true); throw; }
    }

    private static void CheckLinuxDependencies(string payload)
    {
        var files = new[] { Path.Combine(payload, "ComicEditor.Desktop"), Path.Combine(payload, "compiler", "comic-compile") }
            .Concat(Directory.EnumerateFiles(payload, "*.so"));
        foreach (var file in files.Where(File.Exists))
        {
            try
            {
                var start = new ProcessStartInfo("ldd") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(file);
                using var process = Process.Start(start);
                if (process is null) continue;
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(10_000)) { process.Kill(); throw new InvalidDataException("Linux dependency check timed out."); }
                if (output.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update requires Linux libraries that are not installed:\n" + output);
            }
            catch (Win32Exception) { return; } // ldd is optional, as in the standalone installer.
        }
    }
    private static void CopyTree(string source, string destination, CancellationToken cancellation, bool overwrite = false)
    {
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The application folder contains symbolic links. Update this installation manually.");
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo directory) { Directory.CreateDirectory(target); CopyTree(directory.FullName, target, cancellation, overwrite); }
            else File.Copy(entry.FullName, target, overwrite);
        }
    }
    public static void Extract(string archive, string destination, CancellationToken cancellation = default)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long total = 0; var count = 0;
        string PathFor(string name, long length)
        {
            cancellation.ThrowIfCancellationRequested();
            name = name.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p == "..")) throw new InvalidDataException("Unsafe update archive path.");
            var result = Path.GetFullPath(Path.Combine(destination, name));
            if (result != root.TrimEnd(Path.DirectorySeparatorChar) && !result.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Unsafe update archive path.");
            total += length;
            if (++count > 100_000 || length < 0 || total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Update archive exceeds its size limit.");
            return result;
        }
        using var file = File.OpenRead(archive);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var type = (entry.ExternalAttributes >> 16) & 0xf000;
                if (type is not (0 or 0x8000 or 0x4000)) throw new InvalidDataException("Update archives cannot contain links or special files.");
                var path = PathFor(entry.FullName, entry.Length);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open(); using var output = File.Create(path); input.CopyTo(output);
            }
        }
        else
        {
            using var gzip = new GZipStream(file, CompressionMode.Decompress); using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                var path = PathFor(entry.Name, entry.Length);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Update archives cannot contain links or special files.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = File.Create(path)) entry.DataStream?.CopyTo(output);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1ff);
            }
        }
    }
    public static async Task LaunchHelper(UpdatePlan plan, string workDirectory)
    {
        var helperDirectory = Path.Combine(workDirectory, "helper"); Directory.CreateDirectory(helperDirectory);
        // Native AOT starts the helper without initializing Avalonia. Native libraries are copied for loader compatibility.
        foreach (var file in Directory.EnumerateFiles(plan.Target))
            if (Path.GetFileName(file) == plan.Executable || Path.GetExtension(file) is ".dll" or ".so" or ".dylib") File.Copy(file, Path.Combine(helperDirectory, Path.GetFileName(file)), overwrite: true);
        var executable = Path.Combine(helperDirectory, plan.Executable);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, File.GetUnixFileMode(Path.Combine(plan.Target, plan.Executable)));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = helperDirectory };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(Path.Combine(workDirectory, "plan.json"));
        using var helper = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
        var ready = Path.Combine(workDirectory, "helper.ready");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(ready) && File.ReadAllText(ready) == plan.Token) return;
            if (helper.HasExited) throw new IOException("The update helper could not start. The editor will stay open.");
            await Task.Delay(100);
        }
        throw new IOException("The update helper did not respond. The editor will stay open.");
    }
    public static UpdatePlan ReadPlan(string file)
    {
        var plan = JsonSerializer.Deserialize(File.ReadAllText(file), UpdateJson.Default.UpdatePlan) ?? throw new InvalidDataException("Missing update plan.");
        if (plan.ParentProcess <= 0 || !Guid.TryParseExact(plan.Token, "N", out _) || !Path.IsPathFullyQualified(plan.Target) ||
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(plan.Target)!) == Path.TrimEndingDirectorySeparator(plan.Target) ||
            ReleaseClient.AssetName(plan.Runtime) is null || plan.Runtime == "android-arm64" || !Version.TryParse(plan.Version, out _))
            throw new InvalidDataException("Invalid update plan.");
        if (plan.InstallerPackage is not null && (plan.Runtime != "win-x64" ||
            plan.InstallerPackage != Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "setup.exe")) ||
            plan.InstallerSha256 is not { Length: 64 } || !plan.InstallerSha256.All(Uri.IsHexDigit) || plan.InstallerSize is <= 0 or > 1_073_741_824 ||
            !Version.TryParse(plan.PreviousVersion, out _))) throw new InvalidDataException("Invalid Windows installer plan.");
        return plan;
    }
    private static void MoveWithRetry(string source, string destination, Action<string, string> moveDirectory, Action<TimeSpan> pause)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { moveDirectory(source, destination); return; }
            catch (Exception error) when (attempt < 14 && IsTransientMoveFailure(error))
            {
                // NSIS, Explorer, or an antivirus scanner can briefly retain a handle
                // after staging. Keep both directory names intact until a move succeeds.
                pause(TimeSpan.FromMilliseconds(Math.Min(1000, 100 * (attempt + 1))));
            }
        }
    }

    private static bool IsTransientMoveFailure(Exception error) => error is UnauthorizedAccessException ||
        error is IOException io && (io.HResult & 0xffff) is 5 or 32 or 33;

    public static void Apply(UpdatePlan plan, Action<string, string>? moveDirectory = null, Action<TimeSpan>? pause = null)
    {
        moveDirectory ??= Directory.Move;
        pause ??= Thread.Sleep;
        if (File.ReadAllText(Path.Combine(plan.Prepared, ".update-token")) != plan.Token || ReleaseClient.ReadInstallation(plan.Target)?.Runtime != plan.Runtime)
            throw new InvalidDataException("Update staging does not match this installation.");
        MoveWithRetry(plan.Target, plan.Backup, moveDirectory, pause);
        try { MoveWithRetry(plan.Prepared, plan.Target, moveDirectory, pause); }
        catch { MoveWithRetry(plan.Backup, plan.Target, moveDirectory, pause); throw; }
    }
    public static int RunHelper(string planFile)
    {
        var log = Path.Combine(Path.GetDirectoryName(planFile)!, "install.log");
        UpdatePlan? plan = null;
        try
        {
            plan = ReadPlan(planFile);
            var workDirectory = Path.GetDirectoryName(planFile)!;
            File.WriteAllText(Path.Combine(workDirectory, "helper.ready"), plan.Token);
            try
            {
                using var parent = Process.GetProcessById(plan.ParentProcess);
                if (!parent.WaitForExit(300_000)) throw new IOException("The editor did not exit; update cancelled.");
            }
            catch (ArgumentException) { /* The editor already exited. */ }
            var approval = Path.Combine(workDirectory, "apply.approved");
            if (!File.Exists(approval) || File.ReadAllText(approval) != plan.Token) return 1;
            if (plan.InstallerPackage is not null) WindowsSetup.Stage(plan);
            Apply(plan);
            if (plan.InstallerPackage is not null) WindowsSetup.Register(plan);
            LinuxDesktopIntegration.Repair(plan.Target);
            File.WriteAllText(log, "Update installed.\n");
            Restart(plan, planFile); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(log, "Update failed: " + error.Message);
            if (plan is not null && Directory.Exists(plan.Backup))
            {
                try
                {
                    if (Directory.Exists(plan.Target)) MoveWithRetry(plan.Target, plan.Prepared, Directory.Move, Thread.Sleep);
                    MoveWithRetry(plan.Backup, plan.Target, Directory.Move, Thread.Sleep);
                    if (plan.InstallerPackage is not null && File.Exists(Path.Combine(plan.Target, "Uninstall.exe")))
                        WindowsSetup.Register(plan, previous: true);
                    else if (plan.InstallerPackage is not null) WindowsSetup.Unregister(plan);
                    LinuxDesktopIntegration.Repair(plan.Target);
                }
                catch (Exception rollback) { File.AppendAllText(log, "\nRollback: " + rollback.Message); return 1; }
            }
            if (plan is not null && !IsRunning(plan.ParentProcess))
                try { Restart(plan, planFile); } catch (Exception restart) { File.AppendAllText(log, "\nRestart: " + restart.Message); }
            return 1;
        }
    }
    private static bool IsRunning(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    private static void Restart(UpdatePlan plan, string planFile)
    {
        var start = new ProcessStartInfo(Path.Combine(plan.Target, plan.Executable)) { UseShellExecute = false, WorkingDirectory = plan.Target };
        start.ArgumentList.Add("--updated"); start.ArgumentList.Add(planFile);
        using var process = Process.Start(start) ?? throw new IOException("Could not restart the editor.");
        if (OperatingSystem.IsLinux() && process.WaitForExit(1500))
            throw new IOException($"The updated editor exited immediately (code {process.ExitCode}). The previous version will be restored.");
    }
    public static void Complete(string planFile, string currentDirectory)
    {
        var plan = ReadPlan(planFile);
        if (ResolveDirectory(currentDirectory) != plan.Target || !Directory.Exists(plan.Backup) ||
            File.ReadAllText(Path.Combine(plan.Target, ".update-token")) != plan.Token) return;
        if (new DirectoryInfo(plan.Backup).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        // Only the exact sibling backup named by this installation's token may be removed.
        Directory.Delete(plan.Backup, recursive: true);
        File.Delete(Path.Combine(plan.Target, ".update-token"));
    }
}
