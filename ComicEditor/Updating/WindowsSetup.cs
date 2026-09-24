using System.Diagnostics;
using System.Security.Cryptography;

namespace ComicEditor.Updating;

public static class WindowsSetup
{
    public static void Verify(UpdatePlan plan)
    {
        using var package = File.OpenRead(plan.InstallerPackage ?? throw new InvalidDataException("Missing setup package."));
        if (package.Length != plan.InstallerSize || !Convert.ToHexString(SHA256.HashData(package)).Equals(plan.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Windows installer no longer matches its verified download.");
    }

    public static ProcessStartInfo Command(UpdatePlan plan, bool register, bool previous = false)
    {
        var target = register ? plan.Target : plan.Prepared;
        if (!Path.IsPathFullyQualified(target) || target.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new InvalidDataException("Invalid installer destination.");
        var mode = register ? "/REGISTER /VERSION=" + Version.Parse((previous ? plan.PreviousVersion : plan.Version)!) : "/STAGE";
        // NSIS requires /D last and unquoted, even with spaces. This is passed
        // directly to CreateProcess, never through cmd.exe or PowerShell.
        return new ProcessStartInfo(plan.InstallerPackage!)
        {
            Arguments = "/S " + mode + " /D=" + target,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(plan.InstallerPackage)!
        };
    }

    private static void Run(UpdatePlan plan, bool register, bool previous = false, bool unregister = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("NSIS updates require Windows.");
        Verify(plan);
        var start = Command(plan, register, previous);
        if (unregister) start.Arguments = "/S /UNREGISTER /D=" + plan.Target;
        using var setup = Process.Start(start) ?? throw new IOException("Could not start Windows setup.");
        if (!setup.WaitForExit(600_000))
        {
            setup.Kill(entireProcessTree: true); setup.WaitForExit();
            throw new IOException("Windows setup timed out.");
        }
        if (setup.ExitCode != 0) throw new IOException($"Windows setup failed (exit code {setup.ExitCode}).");
    }

    public static void Stage(UpdatePlan plan)
    {
        Run(plan, register: false);
        var installed = ReleaseClient.ReadInstallation(plan.Prepared);
        if (installed?.Runtime != plan.Runtime || ReleaseClient.Normalize(installed.Version) != ReleaseClient.Normalize(Version.Parse(plan.Version)) ||
            !File.Exists(Path.Combine(plan.Prepared, plan.Executable)) || !File.Exists(Path.Combine(plan.Prepared, "Uninstall.exe")))
            throw new InvalidDataException("Windows setup produced an incomplete or incorrect application package.");
    }

    public static void Register(UpdatePlan plan, bool previous = false) => Run(plan, register: true, previous);
    public static void Unregister(UpdatePlan plan) => Run(plan, register: true, unregister: true);
}
