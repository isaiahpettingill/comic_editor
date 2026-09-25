# AvaloniaColorPicker compatibility port

Upstream: https://github.com/arklumpus/AvaloniaColorPicker

Source revision: `f4c54a219b0c9c0d490784307bb16924ff73cad5` (1.4.0).
Copyright Giorgio Bianchini. This library remains **LGPL-3.0-only**;
see LICENSE and LICENSE.GPL. ComicEditor's own source remains 0BSD.

Modifications made September 24, 2026:

- Build against the application's .NET 11 / Avalonia 12 versions.
- Replace removed Checked events with guarded IsCheckedChanged handlers.
- Replace IStyleable with StyleKeyOverride, update focus event type and Popup.Placement.
- Update the alpha canvas visibility directly for Avalonia 12.

ComicEditor composes the library's CustomColorPicker and ColorCanvasControls,
keeping project palettes and GPL palette files in its existing palette editor.
It does not use the library's separate global palette storage.

## Modify and rebuild

The complete library and application source, including build scripts, are in
each release's source archive and this repository. To replace or modify this
statically linked library, extract that source, edit this directory, install
the SDK pinned in `global.json`, and run from the repository root:

```
dotnet publish ComicEditor.Desktop -c Release -r win-x64 -o rebuilt
```

Use `linux-x64` or `osx-arm64` on the corresponding host instead.
Linux requires clang and zlib development headers. Windows requires Visual
Studio's C++ build tools. Android and browser build commands and dependencies
are recorded in `.github/workflows/release.yml`. NuGet restores dependencies.
No signing secret is needed to build or run a modified application. Official
release signatures identify official binaries; they are not an execution check.
