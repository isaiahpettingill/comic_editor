using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using ComicEditor.Rendering;
using ComicEditor.Updating;

namespace ComicEditor.Views;

public partial class MainView
{
    private CancellationTokenSource? updateLifetime, updateOperation;
    private UpdateRelease? availableUpdate;
    private string? updatePackage;
    private bool updateBusy, updateInstalling, restoredUpdate;
    private string updateMessage = "Checks for stable releases on GitHub.";
    private MenuItem? updateMenu, helpMenu, compactDrawer;
    private TextBlock? updateStatus;
    private ProgressBar? updateProgress;
    private Button? updateAction, updateCancel;

    private void AddUpdateMenu(Menu menu)
    {
        helpMenu = new MenuItem { Header = "_Help" };
        var about = new MenuItem { Header = "About & licenses…" };
        about.Click += (_, _) => ShowLicenses(); helpMenu.Items.Add(about); menu.Items.Add(helpMenu);
        if (OperatingSystem.IsAndroid()) return;
        updateMenu = new MenuItem { Header = "Check for updates…", Name = "CheckForUpdates" };
        updateMenu.Click += (_, _) => ShowUpdates(); helpMenu.Items.Add(updateMenu);
        RefreshUpdateControls();
    }
    private void RefreshUpdateControls()
    {
        if (updateMenu is not null) updateMenu.Header = availableUpdate is null ? "Check for updates…" : $"Update {availableUpdate.Version} available…";
        if (helpMenu is not null) helpMenu.Header = availableUpdate is null ? "_Help" : "_Help • Update";
        if (compactDrawer is not null) compactDrawer.Foreground = availableUpdate is null ? Brush(UiTheme.Text) : Brush(UiTheme.Accent);
        if (updateStatus is not null) updateStatus.Text = updateMessage;
        if (updateAction is not null)
        {
            updateAction.IsEnabled = !updateBusy && UpdateHost.Installation is not null;
            updateAction.Content = updateBusy ? "Please wait…" : updatePackage is not null ? "Restart and install" : availableUpdate is not null ? "Download update" : "Check now";
        }
        if (updateCancel is not null) updateCancel.IsVisible = updateBusy && !updateInstalling;
        if (updateProgress is not null) updateProgress.IsVisible = updateBusy;
    }
    private void ShowUpdates()
    {
        var installation = UpdateHost.Installation;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Label($"ComicEditor {ReleaseClient.AppVersion.ToString(3)}", true));
        updateStatus = Label(updateMessage); updateStatus.Name = "UpdateStatus"; body.Children.Add(updateStatus);
        var auto = new CheckBox { Name = "AutoCheckUpdates", Content = "Automatically check for updates", IsChecked = editor.Preferences.CheckForUpdates, IsEnabled = installation is not null };
        auto.IsCheckedChanged += (_, _) => { editor.Preferences.CheckForUpdates = auto.IsChecked == true; editor.Preferences.Save(); };
        body.Children.Add(auto);
        if (OperatingSystem.IsBrowser())
            body.Children.Add(Label("This version updates when the web host deploys a new build. Save your project before reloading the page."));
        else if (installation is null)
            body.Children.Add(Label("Automatic updates are enabled in release packages. Development builds stay under your control."));
        else
            body.Children.Add(Label("Downloads are verified before installation. Restart when ready; your open cutscene and unsaved edits will return. Undo history resets after restarting."));
        updateProgress = new ProgressBar { Name = "UpdateProgress", Minimum = 0, Maximum = 100, Height = 6, IsIndeterminate = true };
        body.Children.Add(updateProgress);
        updateAction = Button("Check now", () => _ = RunUpdateAction()); updateAction.Name = "UpdateAction";
        updateCancel = Button("Cancel download", () => updateOperation?.Cancel()); updateCancel.Name = "CancelUpdate";
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { updateAction, updateCancel } }; body.Children.Add(actions);
        body.Children.Add(Button("Release notes & downloads", async () =>
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null) await launcher.LaunchUriAsync(new Uri(ReleaseClient.ReleasesUrl));
        }));
        RefreshUpdateControls(); ShowModal("Software updates", body);
    }
    private async Task RunUpdateAction()
    {
        if (updateBusy) return;
        if (availableUpdate is null) { await CheckUpdates(manual: true); return; }
        if (updatePackage is not null) { await InstallUpdate(); return; }
        updateBusy = true; updateOperation = new CancellationTokenSource();
        updateMessage = $"Downloading {availableUpdate.Version}…"; RefreshUpdateControls();
        if (updateProgress is not null) updateProgress.IsIndeterminate = false;
        try
        {
            var root = ReleaseClient.DataDirectory;
            var folder = Path.Combine(root, "downloads", availableUpdate.Version.ToString());
            updatePackage = await ReleaseClient.Download(availableUpdate, folder, new Progress<double>(value =>
            {
                if (updateProgress is not null) updateProgress.Value = value * 100;
                updateMessage = $"Downloading {availableUpdate.Version}: {value:P0}";
                if (updateStatus is not null) updateStatus.Text = updateMessage;
            }), updateOperation.Token);
            updateMessage = $"Update {availableUpdate.Version} is downloaded and verified. Install when ready.";
        }
        catch (OperationCanceledException) { updateMessage = "Download cancelled or timed out. You can retry."; }
        catch (Exception ex) { updateMessage = "Download failed: " + ex.Message; }
        finally { updateBusy = false; updateOperation.Dispose(); updateOperation = null; RefreshUpdateControls(); }
    }
    private async Task CheckUpdates(bool manual)
    {
        var installation = UpdateHost.Installation;
        if (installation is null || updateBusy || updatePackage is not null) return;
        updateBusy = true; updateOperation = new CancellationTokenSource();
        if (manual) updateMessage = "Checking GitHub for updates…";
        if (updateProgress is not null) updateProgress.IsIndeterminate = true;
        RefreshUpdateControls();
        try
        {
            availableUpdate = await ReleaseClient.Check(installation, updateOperation.Token);
            updateMessage = availableUpdate is null ? "You’re up to date." : $"Version {availableUpdate.Version} is available ({availableUpdate.Size / 1048576.0:F1} MB).";
        }
        catch (OperationCanceledException) { if (manual) updateMessage = "The update check was cancelled or timed out. Try again."; }
        catch (Exception ex) { if (manual) updateMessage = "Could not check for updates: " + ex.Message; }
        finally { updateBusy = false; updateOperation.Dispose(); updateOperation = null; RefreshUpdateControls(); }
    }
    private async Task InstallUpdate()
    {
        if (availableUpdate is null || updatePackage is null) return;
        if (fileBusy) { updateMessage = "Wait for the current file operation to finish, then install again."; RefreshUpdateControls(); return; }
        FinishPath(); StopSpray(); updateBusy = updateInstalling = true;
        updateMessage = "Preparing the update and saving your workspace…";
        if (updateProgress is not null) updateProgress.IsIndeterminate = true;
        RefreshUpdateControls();
        try
        {
            await SaveSession();
            UpdateRecovery.Save(editor, availableUpdate.Version, ReleaseClient.DataDirectory);
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                throw new InvalidOperationException("Desktop restart is not available in this host.");
            var directory = Path.Combine(ReleaseClient.DataDirectory, "install-" + Guid.NewGuid().ToString("N"));
            var plan = await Task.Run(() => UpdateInstaller.Prepare(updatePackage, availableUpdate, AppContext.BaseDirectory, directory));
            await UpdateInstaller.LaunchHelper(plan, directory);
            File.WriteAllText(Path.Combine(directory, "apply.approved"), plan.Token);
            desktop.Shutdown();
        }
        catch (Exception ex)
        {
            if (ex is InvalidDataException or FileNotFoundException) updatePackage = null;
            updateMessage = "The update could not start: " + ex.Message;
        }
        finally { updateBusy = updateInstalling = false; RefreshUpdateControls(); }
    }
    private async void StartUpdates()
    {
        if (OperatingSystem.IsAndroid() || updateLifetime is not null) return;
        updateLifetime = new CancellationTokenSource(); var cancellation = updateLifetime.Token;
        if (!restoredUpdate && UpdateHost.ResumePlan is not null)
        {
            restoredUpdate = true;
            try
            {
                if (UpdateRecovery.Restore(editor, ReleaseClient.DataDirectory, UpdateHost.ResumePlan is not null, restoreWorkspace: !sessionRestored))
                { RefreshAll(); RefreshTools(); await CutsceneFonts.EnsureAsync(editor.Scene); RefreshCanvas(); }
                if (UpdateHost.ResumePlan is { } plan)
                {
                    if (OperatingSystem.IsLinux()) await Task.Run(() => LinuxDesktopIntegration.Repair(AppContext.BaseDirectory));
                    var log = Path.Combine(Path.GetDirectoryName(plan)!, "install.log");
                    if (File.Exists(log) && File.ReadAllText(log).StartsWith("Update failed:")) await ShowError(File.ReadAllText(log));
                    else
                    {
                        if (OperatingSystem.IsLinux()) await Task.Delay(2000);
                        await Task.Run(() => UpdateInstaller.Complete(plan, AppContext.BaseDirectory));
                    }
                }
            }
            catch (Exception ex) { await ShowError("Update recovery: " + ex.Message + "\nRecovery files remain in " + ReleaseClient.DataDirectory); }
        }
        if (UpdateHost.Installation is null) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellation);
            while (!cancellation.IsCancellationRequested)
            {
                if (editor.Preferences.CheckForUpdates) await CheckUpdates(manual: false);
                await Task.Delay(TimeSpan.FromHours(4), cancellation);
            }
        }
        catch (OperationCanceledException) { }
    }
    private void StopUpdates()
    {
        updateLifetime?.Cancel(); updateLifetime?.Dispose(); updateLifetime = null;
        updateOperation?.Cancel();
    }
}
