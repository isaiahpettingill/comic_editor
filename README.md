# ComicEditor

A storyboard editor for hand-drawn cutscenes.

<img width="1275" height="820" alt="image" src="https://github.com/user-attachments/assets/d95765f8-0596-49a9-b9c8-dea16858e35b" />

## Install

Download desktop and Android builds from [Releases](https://github.com/isaiahpettingill/comic_editor/releases/latest).

- **Windows x64:** run `ComicEditor-win-x64-setup.exe`, or extract the portable ZIP.
- **Linux x64:** download `install-comic-editor.sh` and run `bash install-comic-editor.sh`. No sudo or .NET installation is needed. Requires a glibc-based desktop with X11 or XWayland.
- **macOS Apple Silicon:** extract `ComicEditor-osx-arm64-app.zip` and drag **ComicEditor.app** to Applications.
- **Android arm64:** install `ComicEditor-android-arm64.apk`.
- **Browser:** open [comic-editor.pages.dev](https://comic-editor.pages.dev).

## Frame timing and audio

- Edit a frame’s seconds directly in the storyboard; Enter commits, Escape restores the value. New frames start at **4 seconds**. Existing durations are preserved.
- Editing dialogs save with Enter after validation; multiline fields keep Enter for newlines and use Ctrl+Enter (or Cmd+Enter) to save. Shift+Enter never submits.
- Frame options → **Attach / replace audio…** accepts WAV and QOA without external tools. Desktop imports of MP3, Ogg, FLAC and other formats use **FFmpeg on PATH**. Audio stays local; conversion does not use a service.
- The document and compiled Protobuf/CBOR exports store real **QOA bytes**, not WAV under a different label. Attaching audio increases a too-short frame to the audio duration (rounded up to the next millisecond); replacing/removing audio never shortens longer timing. Undo restores changes.
- Audible desktop preview uses optional **FFplay on PATH** (part of FFmpeg); it stops on pause, close, restart, or frame changes and seeks when resuming. Process startup makes audible preview best-effort rather than sample-accurate. Without FFplay, or on mobile/browser, preview is silent with an explanation; audio still exports.
- Imports are limited to one hour, 256 MiB input/decoded PCM, and 1–8 channels. Cancelled or failed imports leave the frame unchanged. Compressed WAV also needs FFmpeg.

## Build

Install the .NET SDK version specified in [global.json](global.json), then clone the repository and build the desktop app:

```sh
git clone https://github.com/isaiahpettingill/comic_editor.git
cd comic_editor
dotnet build ComicEditor.Desktop
dotnet run --project ComicEditor.Desktop
```

**Browser:**

```sh
dotnet workload install wasm-tools
dotnet run --project ComicEditor.Browser
dotnet publish ComicEditor.Browser -c Release -o artifacts/browser
```

Serve `artifacts/browser/wwwroot` over HTTP(S).

**Android:** install JDK 17 and the Android SDK, then run:

```sh
dotnet workload install android
dotnet build ComicEditor.Android -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=true
dotnet build ComicEditor.Android
```

See the [release workflow](.github/workflows/release.yml) for Native AOT builds and packaging.

## Contributing

If you want to change ComicEditor, fork the repository and make your changes there.

## Credits

ComicEditor is licensed under [0BSD](LICENSE).

- QOA audio: [Dominic Szablewski’s reference codec](https://github.com/phoboslab/qoa), [MIT license](licenses/QOA-MIT.txt).
- Built with [Avalonia](https://avaloniaui.net/).
- [AvaloniaColorPicker](https://github.com/arklumpus/AvaloniaColorPicker): the LGPL-3.0 compatibility port includes [source, attribution, and rebuild instructions](third_party/AvaloniaColorPicker/README.comic-editor.md).
- Fonts: [Comic Shanns](licenses/Comic-Shanns-MIT.txt), [Anton](licenses/Anton-OFL.txt), [Permanent Marker](licenses/Permanent-Marker-Apache-2.0.txt), [Noto Sans](licenses/Noto-OFL.txt), and [Noto Color Emoji](licenses/Noto-Color-Emoji-OFL.txt).
- Icons: [IconPacks](licenses/IconPacks-MIT.txt) and [Material Design Icons](licenses/MaterialDesignIcons-LICENSE.txt). The application icon follows the [Papirus design notes](https://github.com/PapirusDevelopmentTeam/papirus-icon-theme/blob/master/tools/work/DESIGN.md).
- Theme palettes: [Catppuccin](https://github.com/catppuccin/palette), [Solarized](https://ethanschoonover.com/solarized/), and [Gruvbox](https://github.com/morhetz/gruvbox).
