using System.ComponentModel;
using System.Diagnostics;

namespace ComicEditor.Updating;

internal static class LinuxDesktopIntegration
{
    private const string Marker = "ComicEditor per-user installation v1";
    private const string Id = "org.comiceditor.storyboard";

    internal static string? ManagedRoot(string directory)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var target = UpdateInstaller.ResolveDirectory(directory);
        var build = Directory.GetParent(target);
        var releases = build?.Parent;
        var root = releases?.Parent;
        if (Path.GetFileName(target) != "app" || releases?.Name != "releases" || root is null) return null;
        try
        {
            if (File.ReadAllText(Path.Combine(root.FullName, ".installer-owned")).Trim() != Marker ||
                UpdateInstaller.ResolveDirectory(Path.Combine(root.FullName, "current")) != target) return null;
            return root.FullName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void Repair(string directory)
    {
        var root = ManagedRoot(directory); if (root is null) return;
        var target = UpdateInstaller.ResolveDirectory(directory);
        var dataHome = Directory.GetParent(root)!.FullName;
        var template = Path.Combine(root, "comic-editor.desktop");
        var sourceIcon = Path.Combine(target, "comic-editor.svg");
        if (!File.Exists(template) || !File.Exists(sourceIcon) ||
            !File.ReadAllText(template).Contains("X-ComicEditor-Managed=true", StringComparison.Ordinal))
            throw new InvalidDataException("The managed Linux desktop files are incomplete. Re-run the Linux installer.");

        // Existing installations predate the window-class declaration. Keep the
        // stable launcher ID so pinned KDE/GNOME launchers survive future updates.
        var entry = File.ReadAllText(template);
        if (!entry.Contains("StartupWMClass=", StringComparison.Ordinal))
        {
            entry = entry.Replace("X-ComicEditor-Managed=true",
                "StartupWMClass=" + Id + "\nX-ComicEditor-Managed=true", StringComparison.Ordinal);
            WriteAtomic(template, entry);
        }

        var applications = Path.Combine(dataHome, "applications");
        var iconTheme = Path.Combine(dataHome, "icons", "hicolor");
        var desktop = Path.Combine(applications, Id + ".desktop");
        var icon = Path.Combine(iconTheme, "scalable", "apps", Id + ".svg");
        CopyAtomic(template, desktop);
        CopyAtomic(sourceIcon, icon);
        TryRun("update-mime-database", Path.Combine(dataHome, "mime"));
        TryRun("update-desktop-database", applications);
        TryRun("gtk-update-icon-cache", "-f", "-t", iconTheme);
        if (!TryRun("kbuildsycoca6", "--noincremental")) TryRun("kbuildsycoca5", "--noincremental");
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temporary); File.Move(temporary, destination, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteAtomic(string destination, string contents)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, contents); File.Move(temporary, destination, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool TryRun(string executable, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            if (process.WaitForExit(10_000)) return process.ExitCode == 0;
            process.Kill(); return false;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException) { return false; }
    }
}
