#!/usr/bin/env bash
# SPDX-License-Identifier: 0BSD
set -euo pipefail

# Filled by the Linux release job. Source checkouts support --archive.
RELEASE_REPOSITORY='@REPOSITORY@'

die() { printf 'ComicEditor: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'EOF'
Usage: bash install-comic-editor.sh [--archive FILE] [--sha256 HASH]
       bash install-comic-editor.sh --uninstall

Installs ComicEditor and comic-compile for the current Linux user, without sudo.
The release script downloads and verifies the latest Linux x64 release each time.
--archive uses an already downloaded archive (also works from a source checkout).
--sha256 supplies an expected checksum for a local archive.
Re-run to update or repair the launcher and icon. Projects are retained.
Uninstall with comic-editor-uninstall.
Locations: ${XDG_DATA_HOME:-$HOME/.local/share}/comic-editor and ~/.local/bin.
COMIC_EDITOR_BIN_DIR can override the command directory.
EOF
}

archive=''; expected=''; uninstall=false
while (($#)); do
    case "$1" in
        --archive|--sha256)
            (($# >= 2)) || die "Missing value for $1"
            if [[ "$1" == --archive ]]; then archive="$2"; else expected="$2"; fi
            shift 2 ;;
        --uninstall) uninstall=true; shift ;;
        --repair) shift ;; # Accepted for older instructions; repair is automatic.
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
mime="$data_home/mime/packages/org.comiceditor.storyboard.xml"
marker='ComicEditor per-user installation v1'
[[ ! -L "$root" ]] || die "Installation directory is a symlink: $root"
if [[ -e "$root" ]]; then
    [[ -f "$root/.installer-owned" && $(cat "$root/.installer-owned") == "$marker" ]] || die "Refusing to replace an unmanaged directory: $root"
fi

destinations=("$bin_home/comic-editor" "$bin_home/comic-compile" "$bin_home/comic-editor-update" "$bin_home/comic-editor-uninstall" "$desktop" "$icon" "$mime")
targets=("$root/launch" "$root/compile" "$root/installer.sh" "$root/uninstall" "$root/comic-editor.desktop" "$root/current/comic-editor.svg" "$root/cutscene-mime.xml")
owned_destination() {
    local destination="$1" target="$2"
    if [[ -L "$destination" && $(readlink -- "$destination") == "$target" ]]; then return 0; fi
    if ! "$uninstall" && [[ -d "$root" && ( "$destination" == "$desktop" || "$destination" == "$icon" ) ]]; then return 0; fi
    [[ ( "$destination" == "$desktop" || "$destination" == "$icon" || "$destination" == "$mime" ) && ! -L "$destination" && -f "$destination" && -f "$target" ]] && cmp -s -- "$destination" "$target"
}
refresh_desktop() {
    if command -v update-mime-database >/dev/null && [[ -d "$data_home/mime" ]]; then update-mime-database "$data_home/mime" >/dev/null 2>&1 || true; fi
    if command -v update-desktop-database >/dev/null; then update-desktop-database "$data_home/applications" >/dev/null 2>&1 || true; fi
    if command -v gtk-update-icon-cache >/dev/null; then gtk-update-icon-cache -f -t "$data_home/icons/hicolor" >/dev/null 2>&1 || true; fi
    # update-desktop-database updates MIME associations, not Plasma's application menu.
    local cache_tool
    for cache_tool in kbuildsycoca6 kbuildsycoca5; do
        if command -v "$cache_tool" >/dev/null; then
            if ! "$cache_tool" --noincremental > /dev/null 2>&1; then
                printf 'Plasma menu refresh was unavailable. Run %s --noincremental in your desktop session.\n' "$cache_tool" >&2
            fi
            break
        fi
    done
}
if "$uninstall"; then
    [[ -d "$root" ]] || { printf 'ComicEditor is not installed here.\n'; exit 0; }
    for i in "${!destinations[@]}"; do
        if owned_destination "${destinations[i]}" "${targets[i]}"; then
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
        owned_destination "$destination" "${targets[i]}" || die "Refusing to replace an unrelated file: $destination"
    fi
done

mkdir -p -- "$data_home"
stage=$(mktemp -d "$data_home/.comic-editor-install.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
if [[ -z "$archive" ]]; then
    [[ "$RELEASE_REPOSITORY" != @* && "$RELEASE_REPOSITORY" == */* ]] || die 'Use the installer from a GitHub release, or supply --archive FILE.'
    command -v openssl >/dev/null || die 'Install openssl to verify the downloaded release, or use --archive FILE.'
    url="https://github.com/$RELEASE_REPOSITORY/releases/latest/download/ComicEditor-linux-x64.tar.gz"
    archive="$stage/ComicEditor-linux-x64.tar.gz"
    printf 'Downloading the latest ComicEditor release…\n'
    download() {
        if command -v curl >/dev/null; then
            curl --fail --location --retry 3 --proto '=https' --proto-redir '=https' --tlsv1.2 --output "$2" "$1"
        elif command -v wget >/dev/null; then
            wget --https-only -O "$2" "$1"
        else die 'Install curl or wget, or use --archive FILE.'; fi
    }
    download "$url" "$archive"
    download "$url.sig" "$stage/release.sig"
    cat > "$stage/public.pem" <<'PUBLIC_KEY'
@PUBLIC_KEY@
PUBLIC_KEY
    openssl dgst -sha256 -verify "$stage/public.pem" -signature "$stage/release.sig" \
        -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:32 "$archive" >/dev/null \
        || die 'Release signature verification failed; installation was not changed.'
fi
[[ -f "$archive" ]] || die "Archive not found: $archive"
actual=$(sha256sum < "$archive"); actual=${actual%% *}
if [[ -n "$expected" ]]; then
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
mkdir -p -- "$root/releases" "$bin_home" "$(dirname "$desktop")" "$(dirname "$icon")" "$(dirname "$mime")"
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
GenericName=Cutscene Editor
Comment=Draw and localize game cutscenes
Exec="$exec_path" %f
MimeType=application/vnd.comiceditor.cutscene;
Icon=org.comiceditor.storyboard
Terminal=false
Categories=Graphics;2DGraphics;
Keywords=cutscene;storyboard;drawing;localization;
StartupNotify=true
StartupWMClass=org.comiceditor.storyboard
X-ComicEditor-Managed=true
EOF
chmod 644 "$root/comic-editor.desktop"
cat > "$root/cutscene-mime.xml" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/vnd.comiceditor.cutscene">
    <comment>ComicEditor Cutscene</comment>
    <sub-class-of type="application/octet-stream"/>
    <glob pattern="*.ctsc"/>
    <glob pattern="*.cutscene"/>
    <icon name="org.comiceditor.storyboard"/>
  </mime-type>
</mime-info>
EOF
for i in "${!destinations[@]}"; do
    if [[ "${destinations[i]}" == "$desktop" || "${destinations[i]}" == "$icon" || "${destinations[i]}" == "$mime" ]]; then
        # Stable regular files let desktop environments refresh the launcher and icon.
        install -m 644 -- "${targets[i]}" "$stage/link"
    else
        ln -s -- "${targets[i]}" "$stage/link"
    fi
    mv -Tf -- "$stage/link" "${destinations[i]}"
done
refresh_desktop
# Only remove the previous managed payload; keep documents and font caches elsewhere.
if [[ "$old" == "$root/releases/"*/app && "$old" != "$version/app" ]]; then
    previous=${old%/app}
    [[ $(dirname "$previous") == "$root/releases" && ! -L "$previous" ]] && rm -rf -- "$previous"
fi
printf 'Installed ComicEditor. Open it from your application menu or run:\n  %s\nUpdate or repair: %s\nCLI: %s\nUninstall: %s\n' "$bin_home/comic-editor" "$bin_home/comic-editor-update" "$bin_home/comic-compile" "$bin_home/comic-editor-uninstall"
case ":$PATH:" in *":$bin_home:"*) ;; *) printf 'For terminal commands, add %s to PATH.\n' "$bin_home" ;; esac
