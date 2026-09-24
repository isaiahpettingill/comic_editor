using Avalonia.Controls;
using ComicEditor.Fonts;
using ComicEditor.Rendering;

namespace ComicEditor.Views;

public partial class MainView
{
    private CancellationTokenSource? fontCheck;
    private CancellationTokenSource? fontInstall;
    private readonly HashSet<string> offeredFonts = new(StringComparer.Ordinal);
    private DateTimeOffset fontNetworkRetry;
    private Action? refreshFontWarning;
    private Func<CancellationToken, Task<bool>> fontOnline = GoogleFontDownload.IsReachableAsync;
    private Func<string, CancellationToken, Task<string>> downloadLanguageFont = CutsceneFonts.LoadGoogleAsync;
    private Func<ComicEditor.Format.Cutscene, Task> loadCachedLanguageFonts = CutsceneFonts.LoadCachedFallbacksAsync;

    private void QueueFontCheck()
    {
        fontCheck?.Cancel(); fontCheck?.Dispose();
        fontCheck = new CancellationTokenSource();
        _ = CheckFontsAsync(fontCheck.Token);
    }

    private async Task CheckFontsAsync(CancellationToken token, bool explicitlyRequested = false)
    {
        var scene = editor.Scene;
        try
        {
            if (!explicitlyRequested) await Task.Delay(700, token);
            if (TopLevel.GetTopLevel(this) is null || modal is not null) return;
            await loadCachedLanguageFonts(scene);
            token.ThrowIfCancellationRequested();
            if (scene != editor.Scene) return;
            var references = CutsceneFonts.UsedFallbackIds(scene).Except(scene.FallbackFontIds).ToArray();
            if (references.Length > 0) { editor.BeforeChange(); scene.FallbackFontIds.AddRange(references); RefreshTitle(); }
            RefreshCanvas();
            refreshFontWarning?.Invoke();
            var allMissing = CutsceneFonts.Missing(scene);
            var missing = allMissing.Where(f => f.Family.Length > 0 &&
                (explicitlyRequested || !offeredFonts.Contains(f.Id))).ToArray();
            if (missing.Length == 0)
            {
                if (explicitlyRequested && allMissing.Count > 0)
                    await ShowError("Choose a font that covers these characters in Layout & style using a Google Fonts link or an installed system font: " + string.Join(" ", allMissing.Select(f => f.Sample)));
                return;
            }
            if (!explicitlyRequested && DateTimeOffset.UtcNow < fontNetworkRetry) return;
            if (!await fontOnline(token))
            {
                fontNetworkRetry = DateTimeOffset.UtcNow.AddSeconds(30);
                if (explicitlyRequested) await ShowError("Google Fonts is unreachable. Your text is kept; connect to the internet and try Install missing fonts again.");
                return;
            }
            token.ThrowIfCancellationRequested();
            if (scene != editor.Scene || modal is not null) return;
            foreach (var font in missing) offeredFonts.Add(font.Id);
            var status = Label("Downloads are cached for offline use. The project stores font references; game assets contain the rendered text.");
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(Label("Some characters need additional fonts:"));
            foreach (var font in missing) body.Children.Add(Label($"{font.Sample}  {font.Description} — {font.Family}"));
            body.Children.Add(status);
            var downloading = false;
            ShowModal("Install language fonts?", body, async () =>
            {
                if (downloading) return;
                downloading = true;
                using var install = new CancellationTokenSource(); fontInstall = install;
                var dialog = modal;
                try
                {
                    foreach (var font in missing)
                    {
                        status.Text = "Downloading " + font.Family + "…";
                        await downloadLanguageFont(font.Link, install.Token);
                        install.Token.ThrowIfCancellationRequested();
                        if (scene == editor.Scene && !scene.FallbackFontIds.Contains(font.Id))
                        { editor.BeforeChange(); scene.FallbackFontIds.Add(font.Id); }
                    }
                    if (scene == editor.Scene && modal == dialog) { fontInstall = null; CloseModal(); RefreshAll(); }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { status.Text = "Font installation failed: " + ex.Message + " You can retry or cancel."; }
                finally { if (fontInstall == install) fontInstall = null; downloading = false; }
            }, "Install fonts");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetSaveMessage("Font check failed: " + ex.Message, true); }
    }
}
