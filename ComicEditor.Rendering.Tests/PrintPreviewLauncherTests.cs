using ComicEditor.Printing;

namespace ComicEditor.Rendering.Tests;

public sealed class PrintPreviewLauncherTests
{
    [Fact]
    public void LinuxUsesRegisteredBrowserAndSeparateUriArgumentNotHtmlHandler()
    {
        var uri = new Uri("file:///tmp/a%20folder/print%23frame.html");
        var query = PrintPreviewLauncher.DefaultBrowserQuery();
        Assert.Equal("xdg-settings", query.FileName); Assert.Equal(new[] { "get", "default-web-browser" }, query.ArgumentList);
        var command = PrintPreviewLauncher.LinuxBrowserCommand("org.chromium.Chromium.desktop", uri);
        Assert.Equal("gtk-launch", command.FileName); Assert.False(command.UseShellExecute);
        Assert.Equal(new[] { "org.chromium.Chromium.desktop", uri.AbsoluteUri }, command.ArgumentList);
        Assert.Empty(command.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--help.desktop")]
    [InlineData("../../browser.desktop")]
    [InlineData("browser.desktop\nother.desktop")]
    [InlineData("browser.desktop; touch /tmp/marker")]
    [InlineData("browser")]
    public void LinuxRejectsPathsOptionsShellSyntaxAndInvalidDesktopIds(string desktopId)
        => Assert.Throws<IOException>(() => PrintPreviewLauncher.LinuxBrowserCommand(desktopId, new Uri("file:///tmp/frame.html")));

    [Fact]
    public void OtherDesktopCommandsExplicitlyTargetBrowsersWithoutShellParsing()
    {
        var uri = new Uri("file:///tmp/print%20frame.html");
        var mac = PrintPreviewLauncher.MacBrowserCommand(uri);
        Assert.Equal("/usr/bin/open", mac.FileName); Assert.False(mac.UseShellExecute);
        Assert.Equal(new[] { "-a", "Safari", uri.AbsoluteUri }, mac.ArgumentList);
        var win = PrintPreviewLauncher.WindowsBrowserCommand(@"C:\Program Files\Microsoft\Edge\Application\msedge.exe", uri);
        Assert.Equal(@"C:\Program Files\Microsoft\Edge\Application\msedge.exe", win.FileName);
        Assert.Equal(new[] { uri.AbsoluteUri }, win.ArgumentList); Assert.False(win.UseShellExecute); Assert.Empty(win.Arguments);
    }

    [Theory]
    [InlineData("https://example.com/print")]
    [InlineData("file://server/share/frame.html")]
    public void NetworkLocationsAreNotAcceptedAsLocalPrintPreviews(string location)
        => Assert.Throws<ArgumentException>(() => PrintPreviewLauncher.LinuxBrowserCommand("browser.desktop", new Uri(location)));

    [Fact]
    public void RelativePathsAreRejectedAndLocalPathSpacesRemainOneUri()
    {
        Assert.Throws<ArgumentException>(() => PrintPreviewLauncher.LocalPreviewUri("frame.html"));
        var path = Path.Combine(Path.GetTempPath(), "comic print #1", "frame.html");
        var uri = PrintPreviewLauncher.LocalPreviewUri(path);
        Assert.True(uri.IsFile); Assert.Contains("%20", uri.AbsoluteUri); Assert.Contains("%23", uri.AbsoluteUri);
        Assert.Equal(Path.GetFullPath(path), uri.LocalPath);
    }
}
