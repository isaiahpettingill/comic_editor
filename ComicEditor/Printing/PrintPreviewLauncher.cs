using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ComicEditor.Printing;

/// <summary>Opens a local preview in a browser, never the generic .html document handler.</summary>
internal static class PrintPreviewLauncher
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(8);

    public static async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var uri = LocalPreviewUri(path);
        if (OperatingSystem.IsLinux())
        {
            // xdg-settings selects the HTTP browser independently of text/html associations.
            // gtk-launch accepts an XDG desktop ID followed by URI arguments:
            // https://docs.gtk.org/gtk4/gtk4-launch.html
            var desktopId = (await RunHelperAsync(DefaultBrowserQuery(), captureOutput: true, cancellationToken)).Trim();
            await RunHelperAsync(LinuxBrowserCommand(desktopId, uri), captureOutput: false, cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
            await RunHelperAsync(MacBrowserCommand(uri), captureOutput: false, cancellationToken);
        else if (OperatingSystem.IsWindows())
        {
            // App Paths contains the registered executable, not a parsed shell command.
            // https://learn.microsoft.com/windows/win32/shell/app-registration
            using var process = Process.Start(WindowsBrowserCommand(FindEdgeExecutable(), uri))
                ?? throw new IOException("The browser could not be started.");
            // Do not wait for or terminate the browser: it may remain open for the user.
        }
        else throw new PlatformNotSupportedException("Browser print preview is unavailable on this platform.");
    }

    internal static Uri LocalPreviewUri(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Print preview requires an absolute local path.", nameof(path));
        var uri = new Uri(Path.GetFullPath(path));
        if (!uri.IsFile || uri.IsUnc) throw new ArgumentException("Print preview requires a local file.", nameof(path));
        return uri;
    }

    internal static ProcessStartInfo DefaultBrowserQuery() => Command("xdg-settings", "get", "default-web-browser");

    internal static ProcessStartInfo LinuxBrowserCommand(string desktopId, Uri uri)
    {
        ValidatePreviewUri(uri);
        if (desktopId.Length is < 9 or > 240 || !desktopId.EndsWith(".desktop", StringComparison.Ordinal) ||
            !char.IsAsciiLetterOrDigit(desktopId[0]) || desktopId.Contains("..", StringComparison.Ordinal) ||
            desktopId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            throw new IOException("No valid default web browser is registered. Select a browser in desktop settings or export the frame as PNG.");
        return Command("gtk-launch", desktopId, uri.AbsoluteUri);
    }

    internal static ProcessStartInfo MacBrowserCommand(Uri uri)
    {
        ValidatePreviewUri(uri);
        return Command("/usr/bin/open", "-a", "Safari", uri.AbsoluteUri);
    }

    internal static ProcessStartInfo WindowsBrowserCommand(string executable, Uri uri)
    {
        ValidatePreviewUri(uri);
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('"') || executable.Contains('\n') || executable.Contains('\r'))
            throw new IOException("The registered browser executable is invalid.");
        return Command(executable, uri.AbsoluteUri);
    }

    private static void ValidatePreviewUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.IsFile || uri.IsUnc)
            throw new ArgumentException("Print preview must be a local file URI.", nameof(uri));
    }

    private static ProcessStartInfo Command(string executable, params string[] args)
    {
        var command = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) command.ArgumentList.Add(arg);
        return command;
    }

    [SupportedOSPlatform("windows")]
    private static string FindEdgeExecutable()
    {
        const string appPath = @"\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe";
        foreach (var root in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
        {
            if (Registry.GetValue(root + appPath, "", null) is not string value) continue;
            var executable = value.Trim().Trim('"');
            if (Path.IsPathFullyQualified(executable) && File.Exists(executable)) return executable;
        }
        throw new IOException("Microsoft Edge is not registered. Export the frame as PNG and print from your preferred image viewer.");
    }

    private static async Task<string> RunHelperAsync(ProcessStartInfo command, bool captureOutput, CancellationToken cancellationToken)
    {
        command.RedirectStandardOutput = captureOutput;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HelperTimeout);
        using var process = Process.Start(command) ?? throw new IOException("The desktop browser launcher could not be started.");
        try
        {
            var output = captureOutput ? ReadBoundedAsync(process.StandardOutput, timeout.Token) : Task.FromResult("");
            await Task.WhenAll(output, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new IOException($"The desktop browser launcher failed (exit {process.ExitCode}). Export the frame as PNG instead.");
            return await output;
        }
        catch
        {
            // Only the bounded settings/launcher helper is owned by this operation,
            // never an already-launched browser or unrelated child processes.
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[1025]; var length = 0;
        while (length < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (count == 0) return new string(buffer, 0, length);
            length += count;
        }
        throw new IOException("The desktop browser setting is too long.");
    }
}
