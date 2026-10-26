# Workspace conventions

- Click a frame's seconds to edit. Enter saves, Escape cancels; leaving the field commits valid values and cancels invalid drafts. Audio remains the minimum duration.
- Frame, artwork-layer, and text-item ellipses act on that item, or its selected set. Delete works on the focused set; ordinary text fields keep text-editing keys. At least one frame and artwork layer remain.
- Ctrl/Cmd-click toggles items; Shift-click selects a range; Ctrl/Cmd+Shift-click adds a range. Shift+arrow keys extend the focused list selection. Copy/paste and duplication preserve selected-frame order.
- Drag a pane handle to a highlighted left/center/right/bottom target. The preview shows its final slot; release swaps panes. Escape or dropping outside cancels. Right-click a handle offers the same destinations without dragging. Layout is remembered; View → Reset pane layout restores defaults.
- See **View → Keyboard shortcuts (F1)** or [the shortcut reference](keyboard-shortcuts.md).

## Bringing in media

Drop one PNG/JPEG/WebP/BMP/GIF onto the canvas or a storyboard frame. GIF imports its first image. It becomes a new, undoable artwork layer; oversized images are cropped to the canvas. Clipboard bitmap paste (Ctrl/Cmd+V) uses the platform's image clipboard formats. Existing internal frame/artwork copy remains available; typing in a text box never triggers image import. In indexed documents partial alpha is approximated against white and colors map into the palette; RGBA documents preserve alpha.

Drop one WAV, QOA, MP3, Ogg, FLAC, AAC/M4A, Opus, AIFF, or WebM audio file onto its storyboard frame. PCM WAV and QOA work directly. Other formats need **FFmpeg on the desktop PATH**, using the same bounded conversion path as the frame audio picker. The original input is never changed. Stored frame bytes are QOA; attaching audio cannot shorten the frame below the sound duration. Browser/mobile imports support PCM WAV and QOA, not a desktop FFmpeg installation.

**Ctrl/Cmd+P** renders the current frame into a PNG embedded in a temporary local print page. Desktop launches a browser explicitly rather than the system's `.html` file handler (which may be a text editor):

- **Linux:** queries the registered default web browser with `xdg-settings`, then launches its validated desktop ID through `gtk-launch`. Both utilities must be installed; helper operations time out after eight seconds.
- **Windows:** launches Microsoft Edge from its registered App Paths executable. Edge must be registered.
- **macOS:** opens the local page in Safari with `/usr/bin/open -a Safari`.

The page requests the browser's print dialog and also provides a **Print frame** button if automatic dialogs are blocked. Launching the browser does not confirm that a dialog appeared. Nothing is sent to a printer until you confirm it. Missing helpers/browser registration or a failed launch show an error directing you to **File → Export frame PNG**; print that PNG from your preferred image viewer. Browser/mobile use PNG export directly. The print page stays local and is not uploaded; no file-association or default-browser settings are changed.

Clipboard support depends on the OS/browser and permission settings. An image that the platform doesn't expose as a bitmap can still be imported by dropping its file. File input is bounded (images 64 MiB/16 megapixels; audio 256 MiB with existing decoder duration limits).

## Hands-on audit and remaining suggestions

A disposable project in the released browser editor was used to create/move/edit multiline text and try keyboard/toolbar actions. Double-clicking near the start incorrectly placed the caret at the end; the new hit-testing fixes that extra click. Moving text in one drag and entering multiline text already behaved as expected.

Conventions follow familiar drawing tools (B pencil, M selection, Shift+tool key for variants) and developer-tool layouts (visible drag destinations, cancellation, retained positions). A tool-variant mouse selection takes two clicks; the shortcut takes none. Per-item menus deliberately prioritize correct targets, not fewer clicks; Delete remains the zero-click path for a focused selection.

Suggestions not silently implemented: keep the tool/zoom strip stable when text formatting appears; collapse advanced localization explanation rather than removing meaningful labels; remove redundant inline-edit undo checkpoints only with a history-safe design. The current Escape behavior ends inline text editing while keeping edits; undo reverses them.
