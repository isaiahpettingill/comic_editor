# ComicEditor

A small Avalonia 12 storyboard editor for hand-drawn, indexed-color cutscenes. It targets .NET 11. Editable `.cutscene` projects retain artwork, text, translations, and font references; compiled `.cutscene.runtime` game assets flatten artwork and prerender localized text. PNG export is optional. The software is [0BSD](LICENSE); the bundled Comic Shanns font has its own [MIT license](licenses/Comic-Shanns-MIT.txt). The [IconPacks package](licenses/IconPacks-MIT.txt) and [Material icons](licenses/MaterialDesignIcons-LICENSE.txt) retain their own licenses.

## Build and run

Install the .NET 11 SDK. Avalonia packages and the Protobuf compiler used for C# generation restore through NuGet.

```sh
dotnet run --project ComicEditor.Desktop
dotnet test ComicEditor.Format.Tests
```

## Install on Linux

Download **install-comic-editor.sh** from a GitHub Release and run:

```sh
bash install-comic-editor.sh
```

The release automatically includes this installer and a SHA-256 checksum file. The script downloads that release's Linux x64 archive, verifies its embedded checksum, installs the editor and CLI, and adds **ComicEditor** to your desktop application menu with its icon. No .NET installation or sudo is needed. Re-run the installer from a newer release to update; close the editor first.

Files live under `~/.local/share/comic-editor`, with `comic-editor`, `comic-compile`, and `comic-editor-uninstall` commands in `~/.local/bin`. `XDG_DATA_HOME` and `COMIC_EDITOR_BIN_DIR` can override these locations. If your shell does not include `~/.local/bin` in `PATH`, the installer prints the full command paths; desktop menu launch works independently of `PATH`.

The launcher uses the standard XDG application and icon directories for KDE Plasma, GNOME, XFCE, and other compatible desktops. Run the installer as your desktop user. Updates replace the launcher with a regular `.desktop` file and refresh Plasma's menu cache when `kbuildsycoca6` or `kbuildsycoca5` is available. Re-running the newest installer repairs the older launcher. If installing outside your desktop session, log in again or run `kbuildsycoca6 --noincremental` (Plasma 6) / `kbuildsycoca5 --noincremental` (Plasma 5).

For an offline installation, download both assets from the same release:

```sh
bash install-comic-editor.sh --archive ComicEditor-linux-x64.tar.gz
# Remove the application, launchers and icon; retain projects and font caches:
~/.local/bin/comic-editor-uninstall
```

The Linux binary targets x86-64, glibc-based desktop distributions compatible with the Ubuntu 24.04 build environment. ARM Linux and Alpine/musl are not included. Normal desktop libraries are required: X11 (or XWayland in a Wayland session), fontconfig, ICU, and the usual .NET native dependencies. The installer reports missing linked libraries before replacing an existing installation. Distribution package names vary; see [Avalonia's Linux dependency guidance](https://docs.avaloniaui.net/docs/deployment/linux). It does not change system packages or file associations.

For local builds, the unstamped source installer accepts `bash tools/install-linux.sh --archive /path/to/ComicEditor-linux-x64.tar.gz --sha256 HASH`; the checksum option is optional only for this source-checkout mode.

## Development

The application icon is drawn in [`comic-editor.svg`](ComicEditor/Assets/comic-editor.svg). Its flat colors, highlights, and shadows follow the [Papirus design notes](https://github.com/PapirusDevelopmentTeam/papirus-icon-theme/blob/master/tools/work/DESIGN.md). To regenerate the desktop PNG, multi-size ICO, and Android PNG, install CairoSVG and run `python tools/render_icon.py`.

The Android target also needs the .NET Android workload, Android SDK, and JDK. With those installed:

```sh
dotnet workload install android
dotnet build ComicEditor.Android -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=true
dotnet build ComicEditor.Android
```

## Editing

Use the storyboard's duplicate icon to create a new frame from the current one. **Alt+Left/Right** selects adjacent frames. **Compare** fits previous and current frames side by side on desktop. **Onion skin** overlays previous artwork, with an adjacent opacity control. Drag pane headers to swap desktop panes and drag dividers to resize them. The conventional menu bar includes File, Edit, Frame, View, Canvas, Palette, and Languages. Undo and Redo buttons remain visible in the toolbar.

Android keeps a touch layout in both portrait and landscape. Draw, Frames, Layers/text, and Colors buttons switch the workspace without permanently stacking panels around the canvas. The current tool icon opens the tool picker; Options opens the selected tool's settings. The layers icon beside zoom opens onion-skin settings. The top bar keeps Undo, Redo, the frame count, and the preview language accessible. Small desktop/browser windows use this compact layout too.

The vertical Material icon rail provides pixel, smooth, pressure, eraser, fill, line, rectangle, ellipse, eyedropper, text, spray, rectangular selection, freehand selection, curve, polygon, rounded rectangle, and zoom tools. **Tool options** (the sliders icon on desktop) sets the brush size, tip shape, shape fill, or spray density. Brush tips include round, square, two diagonal tips, horizontal, and vertical. Hold the spray can still to build up paint. Curves start with a dragged line followed by two dragged bends. Polygons accept clicked corners; double-click, Enter, or Finish closes them. Escape cancels an unfinished path.

Drag a selection on the current artwork layer, then drag inside it to move pixels. **Edit** offers copy/cut/paste/delete/select all/deselect; desktop shortcuts are Ctrl+C/X/V/A and Delete. Transparent selected pixels paste transparently. These selections affect artwork, not separate text objects. Selection movement is undoable.

**Edit → Drawing input…** enables mouse/touchpad smoothing for freehand tools. Pen input retains its pressure response; the Smooth brush also smooths pen strokes. **Ctrl+mouse wheel** zooms at the pointer; the zoom selector offers Fit and fixed zoom levels. The Zoom tool uses click to enlarge and right-click or Shift-click to reduce. Indexed artwork uses nearest-neighbor bitmap rendering to avoid seams at fractional zoom.

New text reuses the last applied font, size, bold, and italic style. New projects reuse the latest canvas dimensions and palette. The active color/tool, each tool's size and brush shape, spray density, shape fill, smoothing, preview language, onion skin, comparison, and zoom are remembered across sessions. These preferences are separate from cutscene files, stored in the application's local data directory (`ComicEditor/preferences.json`) or browser local storage.

**Canvas → Resize canvas…** changes every frame's dimensions. Choose Center or Top left anchoring. Enlarging adds transparent pixels; shrinking crops artwork. Text objects move with the chosen anchor. Resizing is undoable and does not resample artwork.

The 128 editable swatches flow across the palette pane. Select a color and use **Edit color…**, or right-click a swatch, to edit hex RGB or channel sliders. Pressure uses normal pen/pointer pressure, falling back to full pressure for devices without it.

Layer rows show visibility, names, and stacking order (topmost first). Double-click an artwork layer to rename it. Text has its own visibility row above artwork. Layer controls add, delete, raise, and lower artwork layers.

Choose **Text**, then **click and drag** to create its wrapping area. Clicking existing text selects it; dragging moves it, and its eight handles resize the area. **Delete** removes selected text when focus is outside an input field. Edit the selected language directly in the inspector; **Layout & style…** opens a modal for position, dimensions, font, size, style, and palette color. Apply and Cancel keep property edits atomic. Missing and overflowing translations are marked in the editor. Exported artwork never contains localization keys or selection markers.

**Languages → Manage languages…** adds/removes language tags and chooses a fallback language. Only one translation is displayed at a time, regardless of language count. Empty translations use the configured fallback; if that is also empty, game rendering and export leave the dialogue blank. Assigning an existing localization key shares its translations. Changing one object's key preserves other objects using the old key.

Bundled fonts include Comic Shanns, Anton, Permanent Marker, and Noto Sans. Noto fallback families cover Latin, Greek, Cyrillic, Arabic, Hebrew, Devanagari, Thai, and CJK characters. Paste any Google Fonts specimen/CSS link into the text properties to load its family. The editing file stores `google:<family>` rather than font bytes. Custom system fonts use `system:<family>` and must be installed on editing/compiler machines. Compiled game assets need no fonts. The editor reports unavailable custom fonts and previews with Noto instead. Font licenses and download hashes are in `licenses/` and `ComicEditor/Assets/Fonts/sources.json`.

Undo/redo covers artwork, palette edits, frame and layer operations, translation edits, language management, text properties, and canvas resizing. The desktop title displays the filename and an asterisk for unsaved changes.

**File → Export frame PNG… / Export all PNGs…** lets you choose any project language without changing the preview. Each PNG is indexed (color type 3) with exactly the distinct RGB colors visible in that image, merging duplicate colors and dropping unused palette entries. Exports use crisp text edges and a white background; no antialias shades or selection markers are added. Each frame gets its own minimal palette and the smallest supported PNG bit depth. The editable project palette stays unchanged.

## Autosave and crash recovery

**File → Autosave & recovery…** enables autosave and sets its interval (1–60 minutes; default 2). Save a new cutscene once to choose its file. **Save / Ctrl+S** then updates that file; **Save as…** chooses another. Autosave is off by default and remembers your choice. It pauses if the file changes outside the editor or becomes inaccessible. Use Open to load external changes or Save as to preserve your edits separately.

Independently of autosave, the editor writes a recovery snapshot every five seconds between drawing gestures, when opening/saving a project, and on desktop close or mobile backgrounding. Startup reopens the last cutscene, including untitled work and unsaved edits. A clean session reloads the latest original file; a dirty session restores the recovery copy. Missing files or revoked access restore a copy that you can save elsewhere. Undo history resets after reopening. A crash can lose changes since the last completed snapshot.

Desktop/Android recovery lives in the local application data folder at `ComicEditor/last-session.json`, outside the application installation. Browser recovery uses IndexedDB for the current site; clearing site data removes it. Browser downloads remain manual, with automatic recovery available even though file autosave is disabled. Keep explicit `.cutscene` saves as your portable project files.

## Browser / WebAssembly

The browser target shares the editor, protobuf files, and rendering code. It uses WebAssembly AOT in Release:

```sh
dotnet workload install wasm-tools
dotnet run --project ComicEditor.Browser
dotnet publish ComicEditor.Browser -c Release -o artifacts/browser
```

Serve `artifacts/browser/wwwroot` over HTTP(S), including all `_framework` files; it cannot run from a `file://` URL. The release workflow packages this directory as `ComicEditor-browser-wasm.zip`. All-frame PNG export downloads a ZIP in browsers. Open/save use the browser's file picker. The last project and unsaved edits are recovered from IndexedDB after reload; file downloads still require Save. Browser access to installed system fonts depends on the runtime, so prefer bundled fonts for portable previews.

## File formats and CLI

[The format guide](docs/runtime-format.md) documents both protobuf schemas and runtime rendering. The game can generate its own reader with `protoc`, without referencing the editor's .NET library.

```sh
# Build the compact display asset for all frames and languages:
dotnet run --project ComicEditor.Cli -c Release -- story.cutscene story.cutscene.runtime
# Or use the Native AOT CLI shipped in desktop release archives:
comic-compile story.cutscene story.cutscene.runtime
```

The editor also exposes **File → Build game cutscene…**. The runtime format removes layer metadata and stores rasterized text masks; Chinese and other translations render without client fonts. Keep the `.cutscene` file for further editing.

## Releases

### In-app updates

Release builds check GitHub 15 seconds after startup and every four hours. **Help → Check for updates…** checks immediately and lets you disable automatic checks. New versions are announced in the Help menu (the compact menu highlights blue). Downloads and installation start when you choose them.

On Windows, Linux, and macOS, choose **Download update**, then **Restart and install**. Downloads must match GitHub's published SHA-256 and size. The updater stages the matching platform package beside the application, waits for the editor to exit, replaces the installation, and reopens your cutscene with unsaved edits and the selected frame intact. Undo history resets. Files stored alongside the application are preserved; the previous installation is retained until the updated editor restores the workspace. Directory replacement failures roll back to the previous installation. System-owned/read-only installations need to be updated by their owner. Development builds have no `update.json` and never update themselves.

Android release builds download the signed APK and hand it to Android's package installer. Android may first ask you to allow ComicEditor to install updates; then return and tap **Install update** again. Installation always uses Android's confirmation and signing checks. Reopen ComicEditor after updating to restore the active cutscene. The browser version updates when its web host deploys a newer build; save before reloading.

Update recovery and logs live in the application's local data folder under `ComicEditor/updates`. `resume.cutscene` is a normal editable project copy that can be opened manually if a restart fails. Updating keeps project files and editor preferences. Version 0.1.3 is the first release with the updater, so older versions require one manual installation.

### Release builds

The [release workflow](.github/workflows/release.yml) runs format and rendering tests and builds Native AOT artifacts for Windows x64, Linux x64, macOS x64/arm64, and WebAssembly. Pushes to `main`, version tags, and manual runs also build the signed Android arm64 APK. Pull requests skip Android because signing secrets are unavailable to external contributors. Android Native AOT on .NET 11 is experimental; the workflow builds it explicitly with `PublishAot=true`.

To publish a release, push a version tag such as `git tag v0.1.0` followed by `git push origin v0.1.0`. Once every platform build succeeds, the workflow creates the GitHub Release and attaches all builds, the Linux installer, and its checksum. Ordinary pushes to `main` produce Actions artifacts; the GitHub Release publishing step runs only for version tags.

Android releases require four repository secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, and `ANDROID_KEY_PASSWORD`. Keep the same signing key for future releases so installed copies can update. The APK build requires these secrets and will fail clearly if they are absent.
