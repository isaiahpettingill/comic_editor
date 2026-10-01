# Editable projects and compiled game assets

## Editable: `.cutscene`

`ComicEditor.Format/cutscene.proto` is the editing schema. Indexed projects use version 3; RGBA projects use version 4. Both retain artwork layers, visibility, ordering, text objects, translations and palette entries. Font references are stored in `TextObject.font_id`:

Each text object can also store per-language placement overrides (`placements`) and UTF-16 style spans (`styles`) for selected text ranges. A language without an override uses the object's base placement; a missing translation uses the fallback language's text and spans. The compiler resolves these settings into per-language glyph masks, so the game does not need fonts or text layout. Multiple editable projects can be open in tabs; tab order and active tab are editor recovery state, not fields in a cutscene file.

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

Each project embeds its own palette. Indexed version 3 has 2–255 RGB entries and one byte per artwork pixel; index 255 means transparent. RGBA version 4 has 2–65535 RGBA swatches (`palette_rgba` stores `0xRRGGBBAA`) and stores each layer as a lossless 8-bit RGBA PNG in `rgba_png`. Paint blends into the stored pixels, so recoloring a swatch affects future strokes and text, while existing RGBA artwork keeps its computed colors. Version 1 (16 colors) and version 2 (128 colors) projects remain readable. A GPL preset is copied into the project and is never required to render it. ComicEditor preserves GPL swatch alpha in `# ComicEditor-Alpha:` comments; readers that ignore comments still see RGB colors.

## Compiled: `.cutscene.runtime`

`ComicEditor.Format/display.proto` is the independent game schema. Generate a reader in the game's language with `protoc`; no editor library is required.

Display version 2 is used for indexed artwork and allows variable RGB palette sizes. Display version 1 used 128 colors; the byte layout and transparency sentinel are unchanged. Display version 3 is used for RGBA artwork. It stores a flattened RGBA PNG per frame in `rgba_artwork_png` and `palette_rgba` for text colors. A runtime should branch on the version and validate the relevant fields.

The file contains:

- Canvas width/height and an RGB or RGBA palette.
- An ordered list of language tags and a fallback language index.
- Ordered frames with a **single flattened indexed buffer or RGBA PNG**.
- One set of rasterized text runs per language per frame.

It contains no fonts, font references, source dialogue, localization keys, object IDs, layer names, hidden artwork, or editor state. Each text run is cropped to its nontransparent pixels and stores integer canvas position, dimensions, a palette index and an 8-bit alpha mask. The compiler applies font family, size, bold/italic, wrapping, clipping, language fallback and glyph fallback before writing the file. Chinese and other scripts need no font lookup or shaping at runtime.

### Rendering

1. Clear to white (the editor's canvas background), or choose the game's desired background.
2. For version 2, read `indexed_artwork` in row-major order. Its length is exactly `canvas_width * canvas_height`; indices `0..254` select palette colors, and `255` means transparent. For version 3, decode `rgba_artwork_png` as RGBA and composite it with source-over alpha.
3. Find the language's index in `languages`. Use `fallback_language_index` for an unknown language.
4. Select `frame.text[language_index]`. For each run, draw a rectangle at `(x,y)` with `width * height` row-major alpha values. In version 2 the source RGB is `palette_rgb[palette_index]` and source alpha is `alpha / 255`. In version 3 use the RGB channels of `palette_rgba[palette_index]` and multiply its alpha channel by the coverage value. Composite runs in stored order with ordinary source-over alpha blending.

A runtime can upload indexed artwork as an R8 texture plus a palette, or decode the RGBA PNG once and upload it as an RGBA texture. Each text mask can be an R8 texture tinted with its palette color. No layer composition or text layout is needed at runtime.

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

## Alternative compiled format: `.cbor`

Use **File → Build CBOR game cutscene…**, or choose a `.cbor` output path:

```sh
comic-compile story.cutscene story.cbor
dotnet run --project ComicEditor.Cli -c Release -- story.cutscene story.cbor
```

The `.cbor` extension is detected case-insensitively; other output extensions retain
protobuf export. Font resolution, visible-layer composition, text effects,
per-language placements and missing-translation fallback match protobuf export.
Both indexed and RGBA sources produce the same CBOR representation.

Version 2 is one CBOR map with text keys and these eight fields:

| Key | Value |
| --- | --- |
| `version` | Unsigned integer `2` (independent of protobuf versions) |
| `width`, `height` | Canvas dimensions in pixels |
| `languages` | Array of language tags, sorted ordinally |
| `fallback_language` | One tag from `languages`; `und` for a project without languages |
| `vars` | Sorted array of distinct case-sensitive variable names used by frame conditions |
| `frames` | Ordered array of maps `{png, duration_ms, req}`; each `png` is a flattened base image with no text |
| `text` | Flat array of maps: `{frame, language, x, y, png}` |

`png` in a frame map is a PNG **byte string**. `duration_ms` is an unsigned
integer from 1 to 3600000. `req` is a text string defined below.

`frame` in a text entry is a zero-based index in the original `frames` array; `language` is a tag from `languages`;
`x` and `y` are integer canvas coordinates of the cropped text image's top-left
corner. `png` is a PNG byte string with dimensions encoded in its PNG header.
All images are lossless 8-bit RGBA PNGs with straight (unpremultiplied) alpha.
Text pixels already contain the ink color and its alpha multiplied by glyph
coverage. No palette, fonts, source text, object IDs or protobuf schema are needed.
Maps and arrays have definite lengths; the document has no CBOR tags or trailing data.

Render the base PNG over white (or your chosen background), then draw matching
text PNGs at `(x,y)` using source-over alpha, in stored order. Text entries are
ordered by frame, language and source text-object order. Empty/hidden text emits
no entry. Visible artwork layers are composited in source order; hidden layers
are excluded. Unknown language tags select `fallback_language`; missing
translations are already resolved at compile time.

[`examples/render_cbor.py`](../examples/render_cbor.py) parses the file with
`cbor2`, chooses a language, composites the artwork and text with Pillow, and
writes a displayed frame as a PNG:

```sh
uv run examples/render_cbor.py story.cbor es 0 frame-es.png
```

The example requires no ComicEditor code or protobuf libraries. A comic viewer can
use the same `render(data, language, frame_index)` function for each page. Readers
should reject unsupported versions and validate dimensions, frame references,
language tags and image bounds before uploading textures. Apply file-size and
allocation limits appropriate to your application when reading untrusted assets.


### Frame timing and variants

On each thumbnail, **… → Timing & visibility…** sets duration in milliseconds
and one of four show modes. The clock badge shows its duration; the visibility
badge shows ✓ (always), ⊘ (never), a variable name (set), or !NAME (not set).
Tooltips show the full values. Defaults are **1000 ms** and **always**.
Settings are saved in the editable project and preserved by undo, duplication
and frame copy/paste.

The storyboard's play button (or **Frame → Play cutscene…**) opens a timed preview
in the current language. Check the variables to set, then Play. Pause preserves
the current frame's remaining time; Play resumes; Restart returns to the first
matching frame. Changing variables resets playback. Preview plays once, retains
the last frame at completion, and stops when closed. It does not change the
editing selection. Very short durations are best-effort at the display's refresh
rate; delayed timer ticks advance across all elapsed frames without adding drift.

| `req` | Show when |
| --- | --- |
| `"always"` | Always |
| `"never"` | Never |
| `"PLAYER_A"` | `PLAYER_A` is in the runtime's set of defined variables |
| `"not PLAYER_A"` | `PLAYER_A` is absent from that set |

Names match `[A-Za-z_][A-Za-z0-9_]{0,63}`, are case-sensitive, and cannot be
`always`, `never` or `not`. A condition tests one variable; there are no boolean
expressions or mutually exclusive groups. Multiple variables can be set. `vars`
is automatically derived from conditions, sorted ordinally, and includes names
used in negated conditions. The export contains every frame so the game can
choose a variant at runtime. Excluded frames take **zero time**.

Conceptually, a CBOR v2 file decodes to this structure (PNG bytes abbreviated):

```json
{
  "version": 2,
  "width": 320,
  "height": 180,
  "languages": ["en", "es"],
  "fallback_language": "en",
  "vars": ["PLAYER_A", "PLAYER_B"],
  "frames": [
    {"png": "<PNG bytes>", "duration_ms": 1000, "req": "always"},
    {"png": "<PNG bytes>", "duration_ms": 80, "req": "PLAYER_A"},
    {"png": "<PNG bytes>", "duration_ms": 80, "req": "not PLAYER_A"},
    {"png": "<PNG bytes>", "duration_ms": 1500, "req": "PLAYER_B"},
    {"png": "<PNG bytes>", "duration_ms": 1000, "req": "never"}
  ],
  "text": [{"frame": 1, "language": "es", "x": 12, "y": 30, "png": "<PNG bytes>"}]
}
```

For `PLAYER_A` set and `PLAYER_B` unset, play original frame indices 0, 1 for
1000 ms and 80 ms. Keep original indices when looking up localized text;
filtering must not renumber its `frame` references. Draw each selected frame's
base and localized text together, then wait for its duration before advancing.

The Python example reads both v1 and v2, and includes `selected_frames` and a
Tk playback viewer (requires Python's Tk support):

```sh
uv run examples/render_cbor.py story.cbor es --play --var PLAYER_A
```

V1 CBOR stored raw PNG byte strings in `frames` and had no timing or conditions;
the example treats these as 1000 ms / always with no variables. New exports are
v2, so v1-only readers must update. Editable protobuf adds `Frame.duration_ms`
and `Frame.req`; compiled protobuf adds `DisplayCutscene.vars` and the same
per-frame fields. These are additive fields; artwork versions are unchanged.
Legacy missing/zero duration means 1000 ms; missing/empty `req` means always.
Older readers can still render artwork but need to implement these fields to
honor timing and variants. PNG/book exports remain static documents.
