#!/usr/bin/env bash
# SPDX-License-Identifier: 0BSD
set -euo pipefail

# Filled by the Linux release job. Source checkouts support --archive.
RELEASE_REPOSITORY='@REPOSITORY@'
RELEASE_TAG='@TAG@'
RELEASE_SHA256='@SHA256@'

die() { printf 'ComicEditor: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'EOF'
Usage: bash install-comic-editor.sh [--archive FILE] [--sha256 HASH]
       bash install-comic-editor.sh --uninstall

Installs ComicEditor and comic-compile for the current Linux user, without sudo.
The release script downloads and verifies its matching Linux x64 archive.
--archive uses an already downloaded archive (also works from a source checkout).
--sha256 supplies an expected checksum for a local archive.
Re-run to update. Uninstall with comic-editor-uninstall; projects are retained.
Locations: ${XDG_DATA_HOME:-$HOME/.local/share}/comic-editor and ~/.local/bin.
COMIC_EDITOR_BIN_DIR can override the command directory.
EOF
}

archive=''; expected="$RELEASE_SHA256"; uninstall=false
while (($#)); do
    case "$1" in
        --archive|--sha256)
            (($# >= 2)) || die "Missing value for $1"
            if [[ "$1" == --archive ]]; then archive="$2"; else expected="$2"; fi
            shift 2 ;;
        --uninstall) uninstall=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) die "Unknown option: $1 (use --help)" ;;
    esac
done
[[ $(uname -s) == Linux ]] || die 'This installer requires Linux.'
[[ -n ${HOME:-} && "$HOME" == /* ]] || die 'HOME must be an absolute path.'
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
[[ "$data_home" == /* ]] || data_home="$HOME/.local/share"
bin_home="${COMIC_EDITOR_BIN_DIR:-$HOME/.local/bin}"
[[ "$bin_home" == /* ]] || die 'COMIC_EDITOR_BIN_DIR must be absolute.'
# Desktop Exec paths cannot contain '='; exclude control characters as well.
for path in "$data_home" "$bin_home"; do
    [[ "$path" != *$'\n'* && "$path" != *$'\r'* && "$path" != *$'\t'* && "$path" != *=* ]] || die 'Unsupported character in installation path.'
done
data_home=$(realpath -m -- "$data_home")
bin_home=$(realpath -m -- "$bin_home")
root="$data_home/comic-editor"
desktop="$data_home/applications/org.comiceditor.storyboard.desktop"
icon="$data_home/icons/hicolor/scalable/apps/org.comiceditor.storyboard.svg"
marker='ComicEditor per-user installation v1'
[[ ! -L "$root" ]] || die "Installation directory is a symlink: $root"
if [[ -e "$root" ]]; then
    [[ -f "$root/.installer-owned" && $(cat "$root/.installer-owned") == "$marker" ]] || die "Refusing to replace an unmanaged directory: $root"
fi

destinations=("$bin_home/comic-editor" "$bin_home/comic-compile" "$bin_home/comic-editor-uninstall" "$desktop" "$icon")
targets=("$root/launch" "$root/compile" "$root/uninstall" "$root/comic-editor.desktop" "$root/current/comic-editor.svg")
refresh_desktop() {
    if command -v update-desktop-database >/dev/null; then update-desktop-database "$data_home/applications" >/dev/null 2>&1 || true; fi
    if command -v gtk-update-icon-cache >/dev/null; then gtk-update-icon-cache -f -t "$data_home/icons/hicolor" >/dev/null 2>&1 || true; fi
}
if "$uninstall"; then
    [[ -d "$root" ]] || { printf 'ComicEditor is not installed here.\n'; exit 0; }
    for i in "${!destinations[@]}"; do
        if [[ -L "${destinations[i]}" && $(readlink -- "${destinations[i]}") == "${targets[i]}" ]]; then
            rm -- "${destinations[i]}"
        fi
    done
    rm -rf -- "$root"
    refresh_desktop
    printf 'ComicEditor uninstalled. Projects and downloaded font caches were retained.\n'
    exit 0
fi
[[ $(uname -m) == x86_64 ]] || die 'This release provides Linux x64 only.'
for tool in tar sha256sum mktemp install readlink; do command -v "$tool" >/dev/null || die "Required command not found: $tool"; done
for i in "${!destinations[@]}"; do
    destination="${destinations[i]}"
    if [[ -e "$destination" || -L "$destination" ]]; then
        [[ -L "$destination" && $(readlink -- "$destination") == "${targets[i]}" ]] || die "Refusing to replace an unrelated file: $destination"
    fi
done

mkdir -p -- "$data_home"
stage=$(mktemp -d "$data_home/.comic-editor-install.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
if [[ -z "$archive" ]]; then
    [[ "$RELEASE_REPOSITORY" != @* && "$RELEASE_TAG" != @* && "$expected" =~ ^[0-9a-fA-F]{64}$ ]] || die 'Use the installer from a GitHub release, or supply --archive FILE.'
    url="https://github.com/$RELEASE_REPOSITORY/releases/download/$RELEASE_TAG/ComicEditor-linux-x64.tar.gz"
    archive="$stage/release.tar.gz"
    printf 'Downloading ComicEditor %s…\n' "$RELEASE_TAG"
    if command -v curl >/dev/null; then
        curl --fail --location --retry 3 --proto '=https' --tlsv1.2 --output "$archive" "$url"
    elif command -v wget >/dev/null; then
        wget --https-only -O "$archive" "$url"
    else die 'Install curl or wget, or use --archive FILE.'; fi
fi
[[ -f "$archive" ]] || die "Archive not found: $archive"
actual=$(sha256sum < "$archive"); actual=${actual%% *}
if [[ "$expected" != @* || "$expected" != "$RELEASE_SHA256" ]]; then
    [[ "$expected" =~ ^[0-9a-fA-F]{64}$ ]] || die 'Invalid SHA-256 checksum.'
    [[ "${actual,,}" == "${expected,,}" ]] || die 'Archive checksum mismatch; installation was not changed.'
fi
# Published archives contain only regular files and directories, never links.
tar -tzf "$archive" > "$stage/entries"
while IFS= read -r entry; do
    [[ "$entry" != /* && "/$entry/" != */../* ]] || die 'Unsafe archive path.'
done < "$stage/entries"
tar -tvzf "$archive" > "$stage/details"
if grep -q '^[^-d]' "$stage/details"; then die 'Archive contains unsupported links or special files.'; fi
mkdir "$stage/app"
tar -xzf "$archive" --no-same-owner --no-same-permissions -C "$stage/app"
for file in ComicEditor.Desktop compiler/comic-compile comic-editor.svg; do
    [[ -f "$stage/app/$file" ]] || die "Archive is missing $file"
done
chmod +x "$stage/app/ComicEditor.Desktop" "$stage/app/compiler/comic-compile"
if command -v ldd >/dev/null; then
    for binary in "$stage/app/ComicEditor.Desktop" "$stage/app/compiler/comic-compile" "$stage/app/"*.so; do
        [[ -f "$binary" ]] || continue
        dependencies=$(ldd "$binary" 2>&1 || true)
        if [[ "$dependencies" == *'not found'* ]]; then
            printf '%s\n' "$dependencies" >&2
            die 'Missing Linux libraries. See the Linux installation section in README.md; the existing installation was not changed.'
        fi
    done
fi

install -m 755 -- "${BASH_SOURCE[0]}" "$stage/installer.sh"
mkdir -p -- "$root/releases" "$bin_home" "$(dirname "$desktop")" "$(dirname "$icon")"
printf '%s\n' "$marker" > "$root/.installer-owned"
version=$(mktemp -d "$root/releases/build.XXXXXXXX")
mv -- "$stage/app" "$version/app"
old=$(readlink "$root/current" 2>/dev/null || true)
ln -s "$version/app" "$stage/current"
mv -Tf -- "$stage/current" "$root/current"
# Wrappers retain their actual paths, including spaces, without editing shell profiles.
printf '#!/usr/bin/env bash\nexec %q "$@"\n' "$root/current/ComicEditor.Desktop" > "$root/launch"
printf '#!/usr/bin/env bash\nexec %q "$@"\n' "$root/current/compiler/comic-compile" > "$root/compile"
install -m 755 -- "$stage/installer.sh" "$root/installer.sh"
printf '#!/usr/bin/env bash\nexport XDG_DATA_HOME=%q\nexport COMIC_EDITOR_BIN_DIR=%q\nexec bash %q --uninstall\n' "$data_home" "$bin_home" "$root/installer.sh" > "$root/uninstall"
chmod 755 "$root/launch" "$root/compile" "$root/uninstall"
# Exec uses Desktop Entry quoting, which is distinct from shell quoting.
exec_path="$root/launch"
exec_path=${exec_path//\\/\\\\}; exec_path=${exec_path//\"/\\\"}
exec_path=${exec_path//\$/\\\$}; exec_path=${exec_path//\`/\\\`}
exec_path=${exec_path//\\/\\\\}; exec_path=${exec_path//%/%%}
cat > "$root/comic-editor.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=ComicEditor
Comment=Draw and localize game cutscenes
Exec="$exec_path"
Icon=org.comiceditor.storyboard
Terminal=false
Categories=Graphics;2DGraphics;
Keywords=cutscene;storyboard;drawing;localization;
StartupNotify=true
EOF
chmod 644 "$root/comic-editor.desktop"
for i in "${!destinations[@]}"; do
    ln -s -- "${targets[i]}" "$stage/link"
    mv -Tf -- "$stage/link" "${destinations[i]}"
done
refresh_desktop
# Only remove the previous managed payload; keep documents and font caches elsewhere.
if [[ "$old" == "$root/releases/"*/app && "$old" != "$version/app" ]]; then
    previous=${old%/app}
    [[ $(dirname "$previous") == "$root/releases" && ! -L "$previous" ]] && rm -rf -- "$previous"
fi
printf 'Installed ComicEditor. Open it from your application menu or run:\n  %s\nCLI: %s\nUninstall: %s\n' "$bin_home/comic-editor" "$bin_home/comic-compile" "$bin_home/comic-editor-uninstall"
case ":$PATH:" in *":$bin_home:"*) ;; *) printf 'For terminal commands, add %s to PATH. The desktop menu works immediately.\n' "$bin_home" ;; esac
