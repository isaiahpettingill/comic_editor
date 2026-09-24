# Editable projects and compiled game assets

## Editable: `.cutscene`

`ComicEditor.Format/cutscene.proto` remains the editing schema (version 3). It stores all artwork layers, visibility, ordering, text objects, translations and palette entries. Font references are stored in `TextObject.font_id`:

| Reference | Meaning |
| --- | --- |
| `comic-shanns` | Bundled default Comic Shanns |
| `google:Anton` | Google Fonts family named Anton |
| `google:Permanent Marker` | Google Fonts family named Permanent Marker |
| `google:Noto Sans SC` | Google Fonts family named Noto Sans SC |
| `system:Example Family` | Font installed on the editing/compiler machine |

The Google Fonts family name is the reference; CDN URLs and machine-specific cache paths are not stored. Legacy `anton`, `permanent-marker`, and `noto-sans` IDs are still understood. Font bytes are **not** embedded in editable files. Fonts downloaded for editing are cached under the user's local application-data directory (`ComicEditor/fonts`); the browser holds its cache for the session. Reopening an uncached Google font requires network access. A family reference follows future catalog updates; keep a compiled asset if exact historical output must be preserved.

The default and bundled font presets work offline. Noto Sans and Noto Color Emoji are the bundled fallback families. Other scripts use optional Google Fonts downloads, offered by the editor when missing glyphs are detected. Installed fonts work offline from the app-data cache (IndexedDB in browsers). The optional `fallback_font_ids` field in the editable document records `google:<family>` references; existing version 3 projects without this field still load. Preview language chooses regional CJK families.

The editor's text properties also accept Google Fonts specimen links or CSS family links. Downloads use the complete regular/variable font from the public Google Fonts repository, retaining its license in the local cache. Bold and italic use the available face or synthesis. The CLI reports missing language fonts; `--download-fonts` explicitly permits fetching them before compilation. Export requires the referenced glyphs to be available. Game assets remain self-contained and contain no font references to resolve at runtime.

Each project embeds its own RGB palette. It has 2–255 entries; every artwork pixel is still one byte, with 255 reserved for transparency. Version 1 (16 colors) and version 2 (128 colors) projects remain readable; saving writes version 3. A GPL preset is copied into the project and is never required to render it.

## Compiled: `.cutscene.runtime`

`ComicEditor.Format/display.proto` is the independent game schema (version 2). Generate a reader in the game's language with `protoc`; no editor library is required.

Display version 2 allows variable palette sizes. Display version 1 used 128 colors; the byte layout and transparency sentinel are unchanged. Runtime readers should accept version 2 and use the palette array length rather than a hardcoded count.

The file contains:

- Canvas width/height and 2–255 palette colors.
- An ordered list of language tags and a fallback language index.
- Ordered frames with a **single flattened indexed artwork buffer**.
- One set of rasterized text runs per language per frame.

It contains no fonts, font references, source dialogue, localization keys, object IDs, layer names, hidden artwork, or editor state. Each text run is cropped to its nontransparent pixels and stores integer canvas position, dimensions, a palette index and an 8-bit alpha mask. The compiler applies font family, size, bold/italic, wrapping, clipping, language fallback and glyph fallback before writing the file. Chinese and other scripts need no font lookup or shaping at runtime.

### Rendering

1. Clear to white (the editor's canvas background), or choose the game's desired background.
2. Read `indexed_artwork` in row-major order. Its length is exactly `canvas_width * canvas_height`; indices `0..254` select palette colors, and `255` means transparent.
3. Find the language's index in `languages`. Use `fallback_language_index` for an unknown language.
4. Select `frame.text[language_index]`. For each run, draw a rectangle at `(x,y)` with `width * height` row-major alpha values. The source RGB is `palette_rgb[palette_index]`; source alpha is `alpha / 255`. Composite runs in stored order with ordinary source-over alpha blending.

A runtime can upload indexed artwork as an R8 texture plus a palette, and each text mask as an R8 texture tinted with its palette color. Upload once and reuse textures while the frame is displayed. No decompression, layer composition or text layout is needed.

Readers should validate the format version, palette count, buffer lengths, frame text/language counts, palette indices and bounds before allocating GPU resources. Protobuf binary files are inspectable with `protoc --decode` using their respective schemas.

## Compile

In the editor use **File → Build game cutscene…**. From the command line:

```sh
comic-compile story.cutscene story.cutscene.runtime
# From source:
dotnet run --project ComicEditor.Cli -c Release -- story.cutscene story.cutscene.runtime
```

The CLI compiles every frame and every project language. It returns 0 on success, 1 for conversion failure and 2 for invalid arguments. Missing custom system fonts and unresolved Google fonts fail conversion rather than silently substituting a different primary font. The output is replaced only after successful compilation; input and output must be different paths. The desktop release archives include the Native AOT CLI in `compiler/`.

Keep editable projects as source assets and compile them during the game's build. PNG export remains an optional debugging/sharing feature.
