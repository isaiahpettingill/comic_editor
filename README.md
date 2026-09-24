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

Android keeps a touch layout in both portrait and landscape. Draw, Frames, Layers/text, and Colors buttons switch the workspace without permanently stacking panels around the canvas. The current tool icon opens all ten tools; the layers icon beside zoom opens onion-skin settings. The top bar keeps Undo, Redo, the frame count, and the preview language accessible. Small desktop/browser windows use this compact layout too.

The vertical Material icon rail provides pixel, smooth, pressure, eraser, fill, line, rectangle, ellipse, eyedropper, and text tools. Brush size appears in the options row for brush tools. **Ctrl+mouse wheel** zooms at the pointer; the zoom selector offers Fit and fixed zoom levels. Indexed artwork uses nearest-neighbor bitmap rendering to avoid seams at fractional zoom.

**Canvas → Resize canvas…** changes every frame's dimensions. Choose Center or Top left anchoring. Enlarging adds transparent pixels; shrinking crops artwork. Text objects move with the chosen anchor. Resizing is undoable and does not resample artwork.

The 128 editable swatches flow across the palette pane. Select a color and use **Edit color…**, or right-click a swatch, to edit hex RGB or channel sliders. Pressure uses normal pen/pointer pressure, falling back to full pressure for devices without it.

Layer rows show visibility, names, and stacking order (topmost first). Double-click an artwork layer to rename it. Text has its own visibility row above artwork. Layer controls add, delete, raise, and lower artwork layers.

Choose **Text**, then **click and drag** to create its wrapping area. Clicking existing text selects it; dragging moves it, and its eight handles resize the area. **Delete** removes selected text when focus is outside an input field. Edit the selected language directly in the inspector; **Layout & style…** opens a modal for position, dimensions, font, size, style, and palette color. Apply and Cancel keep property edits atomic. Missing and overflowing translations are marked in the editor. Exported artwork never contains localization keys or selection markers.

**Languages → Manage languages…** adds/removes language tags and chooses a fallback language. Only one translation is displayed at a time, regardless of language count. Empty translations use the configured fallback; if that is also empty, game rendering and export leave the dialogue blank. Assigning an existing localization key shares its translations. Changing one object's key preserves other objects using the old key.

Bundled fonts include Comic Shanns, Anton, Permanent Marker, and Noto Sans. Noto fallback families cover Latin, Greek, Cyrillic, Arabic, Hebrew, Devanagari, Thai, and CJK characters. Paste any Google Fonts specimen/CSS link into the text properties to load its family. The editing file stores `google:<family>` rather than font bytes. Custom system fonts use `system:<family>` and must be installed on editing/compiler machines. Compiled game assets need no fonts. The editor reports unavailable custom fonts and previews with Noto instead. Font licenses and download hashes are in `licenses/` and `ComicEditor/Assets/Fonts/sources.json`.

Undo/redo covers artwork, palette edits, frame and layer operations, translation edits, language management, text properties, and canvas resizing. The desktop title displays the filename and an asterisk for unsaved changes.

## Browser / WebAssembly

The browser target shares the editor, protobuf files, and rendering code. It uses WebAssembly AOT in Release:

```sh
dotnet workload install wasm-tools
dotnet run --project ComicEditor.Browser
dotnet publish ComicEditor.Browser -c Release -o artifacts/browser
```

Serve `artifacts/browser/wwwroot` over HTTP(S), including all `_framework` files; it cannot run from a `file://` URL. The release workflow packages this directory as `ComicEditor-browser-wasm.zip`. All-frame PNG export downloads a ZIP in browsers. Open/save use the browser's file picker. Project data remains in memory until you save it; closing or reloading the tab discards unsaved changes. Browser access to installed system fonts depends on the runtime, so prefer bundled fonts for portable previews.

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

The [release workflow](.github/workflows/release.yml) runs format and rendering tests and builds Native AOT artifacts for Windows x64, Linux x64, macOS x64/arm64, and WebAssembly. Pushes to `main`, version tags, and manual runs also build the signed Android arm64 APK. Pull requests skip Android because signing secrets are unavailable to external contributors. Android Native AOT on .NET 11 is experimental; the workflow builds it explicitly with `PublishAot=true`.

To publish a release, push a version tag such as `git tag v0.1.0` followed by `git push origin v0.1.0`. Once every platform build succeeds, the workflow creates the GitHub Release and attaches all builds, the Linux installer, and its checksum. Ordinary pushes to `main` produce Actions artifacts; the GitHub Release publishing step runs only for version tags.

Android releases require four repository secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, and `ANDROID_KEY_PASSWORD`. Keep the same signing key for future releases so installed copies can update. The APK build requires these secrets and will fail clearly if they are absent.
