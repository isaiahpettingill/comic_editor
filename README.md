# ComicEditor

A small Avalonia 12 storyboard editor for hand-drawn, indexed-color cutscenes. It targets .NET 11. Editable `.ctsc` / `.cutscene` projects retain artwork, text, translations, and font references; compiled `.cutscene.runtime` game assets flatten artwork and prerender localized text. PNG export is optional. The software is [0BSD](LICENSE); the bundled Comic Shanns font has its own [MIT license](licenses/Comic-Shanns-MIT.txt). The [IconPacks package](licenses/IconPacks-MIT.txt) and [Material icons](licenses/MaterialDesignIcons-LICENSE.txt) retain their own licenses.

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

The Linux binary targets x86-64, glibc-based desktop distributions compatible with the Ubuntu 24.04 build environment. ARM Linux and Alpine/musl are not included. Normal desktop libraries are required: X11 (or XWayland in a Wayland session), fontconfig, ICU, and the usual .NET native dependencies. The installer reports missing linked libraries before replacing an existing installation. Distribution package names vary; see [Avalonia's Linux dependency guidance](https://docs.avaloniaui.net/docs/deployment/linux). It registers `.ctsc` and `.cutscene` with the desktop MIME database without changing system packages.

For local builds, the unstamped source installer accepts `bash tools/install-linux.sh --archive /path/to/ComicEditor-linux-x64.tar.gz --sha256 HASH`; the checksum option is optional only for this source-checkout mode.

## File associations and project extensions

New projects default to **`.ctsc`**. Both `.ctsc` and `.cutscene` contain the same editable protobuf format, work in Open/Save As, and are accepted by the CLI. Existing `.cutscene` filenames remain unchanged when saved.

- **Windows:** the NSIS installer registers both extensions, an icon, Open with, and Default Apps capabilities for the current user. Existing default app choices are preserved; uninstall removes only ComicEditor's registration. Portable ZIPs do not register themselves.
- **Linux:** the installer registers a shared MIME type and desktop handler for KDE, GNOME, XFCE, and other XDG desktops.
- **macOS:** download the appropriate `-app.zip`, extract it, and drag **ComicEditor.app** to Applications. Finder discovers its declared file types. The tar archive remains available for portable/CLI installations.
- **Android:** registers an Open with handler for the cutscene MIME type and file/content URLs whose paths end in these extensions. Providers using opaque content URLs and generic MIME types may require opening the project from inside the editor.
- **Browser:** use the file picker; the website does not register OS file associations.

Opening an associated project prompts before discarding unsaved edits. Compiled `.cutscene.runtime` files are display assets, not editable projects.

The palette editor and individual color dialog use [AvaloniaColorPicker](https://github.com/arklumpus/AvaloniaColorPicker) with a local Avalonia 12 compatibility port. Spectrum/hue selection, RGB, and hex entry edit opaque palette entries; transparency remains the reserved index. Its LGPL license, complete source, and rebuild instructions are in [third_party/AvaloniaColorPicker](third_party/AvaloniaColorPicker/README.comic-editor.md), the release source archive, and **Help > About & licenses**. ComicEditor's own software remains 0BSD.

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

**Edit → Drawing input…** enables mouse/touchpad smoothing for freehand tools. Pen input retains its pressure response; the Smooth brush also smooths pen strokes. **Scroll over the canvas** to zoom at the pointer (no modifier needed), or **pinch with two fingers**. **Middle-button drag** or **three-finger drag** pans the zoomed canvas. Single-finger touch still draws; adding a second finger cancels the provisional mark without changing undo/redo history. Lift all fingers before drawing again. The zoom selector offers Fit and fixed zoom levels. The Zoom tool uses click to enlarge and right-click or Shift-click to reduce. Indexed artwork uses nearest-neighbor bitmap rendering to avoid seams at fractional zoom.

New text reuses the last applied font, size, bold, and italic style. New projects reuse the latest canvas dimensions and palette. The active color/tool, each tool's size and brush shape, spray density, shape fill, smoothing, preview language, onion skin, comparison, and zoom are remembered across sessions. These preferences are separate from cutscene files, stored in the application's local data directory (`ComicEditor/preferences.json`) or browser local storage.

**Canvas → Resize canvas…** changes every frame's dimensions. Choose Center or Top left anchoring. Enlarging adds transparent pixels; shrinking crops artwork. Text objects move with the chosen anchor. Resizing is undoable and does not resample artwork.

Each cutscene has its own 2–255 color palette (128 by default); index 255 is reserved for transparency. Swatches flow across the palette pane. Open **Palette → Palette editor…** or **Edit palette…** to change its size and colors, load a preset, or import/export a GIMP `.gpl` file. Right-click a swatch to edit just that color. Pressure uses normal pen/pointer pressure, falling back to full pressure for devices without it.

Layer rows show visibility, names, and stacking order (topmost first). Double-click an artwork layer to rename it. Text has its own visibility row above artwork. Layer controls add, delete, raise, and lower artwork layers.

Choose **Text**, then **click and drag** to create its wrapping area. Clicking existing text selects it; dragging moves it, and its eight handles resize the area. **Delete** removes selected text when focus is outside an input field. Edit the selected language directly in the inspector; **Layout & style…** opens a modal for position, dimensions, font, size, style, and palette color. Apply and Cancel keep property edits atomic. Missing and overflowing translations are marked in the editor. Exported artwork never contains localization keys or selection markers.

**Languages → Manage languages…** adds/removes language tags and chooses a fallback language. Only one translation is displayed at a time, regardless of language count. Empty translations use the configured fallback; if that is also empty, game rendering and export leave the dialogue blank. Assigning an existing localization key shares its translations. Changing one object's key preserves other objects using the old key.

Comic Shanns remains the default cutscene font, with Anton and Permanent Marker available. Only **Noto Sans and Noto Color Emoji** are bundled as fallback families. When text needs additional coverage, the editor offers to install an appropriate Google font if online. Chinese (Simplified/Traditional), Japanese, Korean, Arabic, Hebrew, Indic scripts, Thai, and other supported scripts download on demand after approval. The preview language selects the regional CJK family. Declining keeps the text and its missing-font warning; **Install missing fonts…** retries later. PNG/game export refuses missing glyphs rather than silently producing boxes.

Downloaded fonts and their licenses are cached in the app-data `ComicEditor/fonts` directory (IndexedDB in the browser) and work offline afterward. Projects store `google:<family>` references in `fallback_font_ids`, not downloaded font bytes. A project opened on another machine can offer the same installation. Paste any Google Fonts specimen/CSS link into text properties to choose an additional typeface. Custom system fonts use `system:<family>` and must be installed on editing/compiler machines. Compiled game assets need no fonts. Font licenses and hashes are in `licenses/` and `ComicEditor/Assets/Fonts/sources.json`. Noto Color Emoji retains all COLR glyphs; its redundant SVG table is removed by `tools/prepare-emoji.py` to reduce installation size.

The CLI uses cached language fonts offline. To approve missing language-font downloads during compilation, run `comic-compile --download-fonts INPUT.cutscene OUTPUT.cutscene.runtime`. Without that flag, missing language fonts produce an actionable error. `COMIC_EDITOR_FONT_CACHE` optionally selects a different cache directory for build machines.

Undo/redo covers artwork, palette edits, frame and layer operations, translation edits, language management, text properties, and canvas resizing. The desktop title displays the filename and an asterisk for unsaved changes.

**File → Export frame PNG… / Export all PNGs…** lets you choose any project language without changing the preview. Each PNG is indexed (color type 3) with exactly the distinct RGB colors visible in that image, merging duplicate colors and dropping unused palette entries. Exports use crisp text edges and a white background; no antialias shades or selection markers are added. Each frame gets its own minimal palette and the smallest supported PNG bit depth. The editable project palette stays unchanged.

## Palette presets

The palette editor works on a draft. **Apply** updates only the current cutscene; **Cancel** discards the draft. Artwork and text keep their slot numbers when the palette is recolored. Shrinking remaps removed slots to the nearest remaining RGB color across every frame/layer/text object; Undo restores the original indices and palette. Imported palettes must have 2–255 colors.

**Save preset** writes a `.gpl` file to `ComicEditor/palettes` under the user's local application-data directory, using the palette name as its filename. Saving that name again updates the preset. The desktop **Open folder** button opens this directory, and `.gpl` files placed there appear in the preset selector when reopening the editor. **Import .gpl…** loads a file into the draft without changing the library; **Export .gpl…** saves a copy wherever you choose. Browser presets persist in IndexedDB and can be exported as `.gpl` files.

A cutscene embeds its complete palette: modifying it never rewrites a saved preset, and it renders without access to the palette folder. The [.gpl specification](https://developer.gimp.org/core/standards/gpl/) defines the portable RGB text format. New editable files use format version 3 and compiled assets use version 2; see the [runtime format guide](docs/runtime-format.md).

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

### Cloudflare Pages deployment

The production address is **https://comic-editor.pages.dev**. [The Pages workflow](.github/workflows/pages.yml) deploys the existing WASM artifact after successful builds on `main`; pull requests never deploy. Superseded builds are skipped. You can also run this workflow manually with a successful **Build and release** run ID to deploy a release artifact or roll back.

Configure these repository Actions secrets once:

- `CLOUDFLARE_ACCOUNT_ID`: the account that will own the site.
- `CLOUDFLARE_API_TOKEN`: a token with **Account → Cloudflare Pages → Edit**, scoped to that account.

The workflow creates the `comic-editor` Pages project if necessary, with `main` as its production branch. The requested address must be available. It keeps the portable release ZIP unchanged and prepares a separate Pages directory. Oversized WASM files use explicit gzip downloads, browser-native decompression, and SHA-256 integrity verification before loading. Packaging verifies decompression against the original bytes and enforces Pages' 25 MiB asset limit.

For local deployment after publishing the browser target:

```sh
pnpm install --frozen-lockfile
node tools/prepare-pages.mjs artifacts/browser/wwwroot artifacts/pages
pnpm exec wrangler login
pnpm exec wrangler pages project create comic-editor --production-branch main --force
pnpm deploy --branch main
```

The project creation command is only needed once; `--force` keeps the project on Pages instead of Wrangler's Workers migration path. Use a fresh output directory for preparation. The browser must support `DecompressionStream`, as current Chrome, Edge, Firefox, and Safari do.

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

### Windows installation

Download **ComicEditor-win-x64-setup.exe** from the release. The NSIS installer installs for the current user under `%LOCALAPPDATA%\Programs\ComicEditor` by default, adds a Start menu shortcut, and registers an uninstall entry in Windows Settings. You can choose another writable folder. Administrator access is not required. Uninstall removes packaged application files and its shortcut; projects, palette presets, preferences, and recovery data are kept.

The portable ZIP remains available. Versions before 0.1.7 update through that ZIP once; from 0.1.7 onward, Windows updates select the NSIS installer by default. Updating a portable copy keeps its existing location and registers that copy as an installed application.

### In-app updates

Desktop release builds check GitHub 15 seconds after startup and every four hours. **Help → Check for updates…** checks immediately and lets you disable automatic checks. New versions are announced in the Help menu (the compact menu highlights blue). Downloads and installation start when you choose them.

On Windows, Linux, and macOS, choose **Download update**, then **Restart and install**. Downloads must match GitHub's published SHA-256 and size. The updater stages the matching platform package beside the application, waits for the editor to exit, replaces the installation, and reopens your cutscene with unsaved edits and the selected frame intact. Undo history resets. Files stored alongside the application are preserved; the previous installation is retained until the updated editor restores the workspace. Directory replacement failures roll back to the previous installation. System-owned/read-only installations need to be updated by their owner. Development builds have no `update.json` and never update themselves.

On Windows the verified NSIS installer runs silently in the staging folder, then updates Windows registration and the Start menu after the folder swap. It does not install over the running executable. Failed staging leaves the existing installation in place.

Android has no in-app updater or package-install permission. Track `isaiahpettingill/comic_editor` in Obtainium to update the signed APK; autosave and crash recovery remain available. The browser version updates when its web host deploys a newer build; save before reloading.

Update recovery and logs live in the application's local data folder under `ComicEditor/updates`. `resume.cutscene` is a normal editable project copy that can be opened manually if a restart fails. Updating keeps project files and editor preferences. Version 0.1.3 is the first release with the updater, so older versions require one manual installation.

### Release builds

Desktop binaries and release packages ship with repository-controlled `.sig` signatures. The private signing key is stored in GitHub Actions secrets and the public key is committed under [signing](signing/README.md). The updater verifies signatures before installing. Android retains its existing keystore; CI checks the final APK's signature and certificate identity. See the signing guide for manual verification and the distinction from OS-trusted publisher signing.

The [release workflow](.github/workflows/release.yml) runs format and rendering tests and builds Native AOT artifacts for Windows x64, Linux x64, macOS x64/arm64, and WebAssembly. Pushes to `main`, version tags, and manual runs also build the signed Android arm64 APK. Pull requests skip Android because signing secrets are unavailable to external contributors. Android Native AOT on .NET 11 is experimental; the workflow builds it explicitly with `PublishAot=true`.

Desktop and CLI Release publishes default to Native AOT with full trimming and `OptimizationPreference=Speed`. Release builds omit debugging symbols, including symbols supplied by native graphics packages. Packaging rejects debug files and checks a 160 MiB installed-size budget for the combined editor, CLI, native libraries, and bundled multilingual fonts. Version 0.1.8 Windows setup also removes the known package symbols accidentally shipped by earlier versions, including when invoked by the updater. Portable ZIP users should extract into a fresh folder to avoid keeping obsolete files.

To publish a release, push a version tag such as `git tag v0.1.0` followed by `git push origin v0.1.0`. Once every platform build succeeds, the workflow creates the GitHub Release and attaches all builds, the Windows NSIS installer, the Linux installer, and its checksum. The Windows job compiles setup with NSIS 3.12 and tests silent installation, staged updates, shell registration, and uninstall. Ordinary pushes to `main` produce Actions artifacts; the GitHub Release publishing step runs only for version tags.

Android releases require four repository secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, and `ANDROID_KEY_PASSWORD`. Keep the same signing key for future releases so installed copies can update. The APK build requires these secrets and will fail clearly if they are absent.
