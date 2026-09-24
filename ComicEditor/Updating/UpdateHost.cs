namespace ComicEditor.Updating;

// Set by the executable hosts. Development and headless test hosts do not check the network.
public static class UpdateHost
{
    public static bool DesktopEnabled { get; set; }
    public static string? ResumePlan { get; set; }
    public static UpdateInstallation? AndroidInstallation { get; set; }
    public static string? AndroidDownloadDirectory { get; set; }
    public static Func<string, bool>? InstallAndroid { get; set; }
    public static UpdateInstallation? Installation => DesktopEnabled ? ReleaseClient.ReadInstallation(AppContext.BaseDirectory) : AndroidInstallation;
}
