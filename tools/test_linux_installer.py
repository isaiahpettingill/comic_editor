"""Exercise installer changes using disposable XDG paths and harmless binaries."""

import hashlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "packaging", TOOLS / "package-linux-installer.py"
)
PACKAGING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGING)


class LinuxInstallerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="comic installer ")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.data = self.base / "data $with 'quotes'"
        self.bin = self.base / "bin with spaces"
        self.env = dict(
            os.environ,
            XDG_DATA_HOME=str(self.data),
            COMIC_EDITOR_BIN_DIR=str(self.bin),
        )
        self.root = self.data / "comic-editor"
        self.archive = self.base / "ComicEditor-linux-x64.tar.gz"
        self.make_archive("one")

    def make_archive(self, version, extra=None):
        with tarfile.open(self.archive, "w:gz") as tar:
            files = {
                "ComicEditor.Desktop": f"#!/bin/sh\nprintf '{version}:%s\\n' \"$*\"\n",
                "compiler/comic-compile": "#!/bin/sh\nprintf 'compiler:%s\\n' \"$*\"\n",
                "comic-editor.svg": "<svg xmlns='http://www.w3.org/2000/svg'/>",
                "LICENSE": "0BSD",
            }
            if extra:
                files.update(extra)
            for name, contents in files.items():
                data = contents.encode()
                entry = tarfile.TarInfo(name)
                entry.size = len(data)
                entry.mode = 0o644
                tar.addfile(entry, io.BytesIO(data))

    def run_installer(self, *args, success=True, script=None):
        result = subprocess.run(
            ["bash", str(script or TOOLS / "install-linux.sh"), *map(str, args)],
            env=self.env,
            capture_output=True,
            text=True,
        )
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout)
        return result

    def command(self, name, *args):
        return subprocess.check_output(
            [str(self.bin / name), *args], env=self.env, text=True
        )

    def test_install_update_uninstall_and_preserve_documents(self):
        self.run_installer("--archive", self.archive)
        self.assertEqual(self.command("comic-editor", "two words"), "one:two words\n")
        self.assertEqual(self.command("comic-compile", "a b"), "compiler:a b\n")
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        self.assertTrue(desktop.is_file())
        self.assertFalse(desktop.is_symlink())
        self.assertIn('Exec="', desktop.read_text())
        self.assertIn('" %f', desktop.read_text())
        self.assertIn(
            "MimeType=application/vnd.comiceditor.cutscene;", desktop.read_text()
        )
        mime = self.data / "mime/packages/org.comiceditor.storyboard.xml"
        self.assertIn('pattern="*.ctsc"', mime.read_text())
        self.assertIn('pattern="*.cutscene"', mime.read_text())
        self.assertIn(r"\\$", desktop.read_text())
        self.assertTrue(
            (
                self.data / "icons/hicolor/scalable/apps/org.comiceditor.storyboard.svg"
            ).is_file()
        )
        document = self.data / "example.cutscene"
        document.write_text("keep this project")
        self.make_archive("two")
        self.run_installer("--archive", self.archive)
        self.assertEqual(self.command("comic-editor"), "two:\n")
        self.assertEqual(len(list((self.root / "releases").iterdir())), 1)
        # Rerunning an installation is also supported.
        self.run_installer("--archive", self.archive)
        self.command("comic-editor-uninstall")
        self.assertFalse(self.root.exists())
        self.assertFalse(mime.exists())
        self.assertFalse(desktop.is_symlink())
        self.assertFalse((self.bin / "comic-editor").is_symlink())
        self.assertEqual(document.read_text(), "keep this project")
        self.run_installer("--uninstall")

    def test_bad_checksum_preserves_previous_installation(self):
        self.run_installer("--archive", self.archive)
        self.make_archive("two")
        result = self.run_installer(
            "--archive", self.archive, "--sha256", "0" * 64, success=False
        )
        self.assertIn("checksum mismatch", result.stderr)
        self.assertEqual(self.command("comic-editor"), "one:\n")

    def test_migrates_old_desktop_symlink_and_refreshes_plasma(self):
        mock_bin = self.base / "mock"
        mock_bin.mkdir()
        log = self.base / "menu-refresh"
        for name in [
            "kbuildsycoca6",
            "kbuildsycoca5",
            "update-desktop-database",
            "gtk-update-icon-cache",
        ]:
            tool = mock_bin / name
            tool.write_text(
                '#!/bin/sh\nprintf "%s:%s\\n" "${0##*/}" "$*" >> "$REFRESH_LOG"\n'
            )
            tool.chmod(0o755)
        self.env.update(
            PATH=str(mock_bin) + ":" + os.environ["PATH"], REFRESH_LOG=str(log)
        )
        self.run_installer("--archive", self.archive)
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        desktop.unlink()
        desktop.symlink_to(self.root / "comic-editor.desktop")
        self.run_installer("--archive", self.archive)
        self.assertFalse(desktop.is_symlink())
        self.assertIn("Categories=Graphics;2DGraphics;", desktop.read_text())
        self.assertNotIn("OnlyShowIn", desktop.read_text())
        self.assertNotIn("NoDisplay", desktop.read_text())
        self.assertIn("kbuildsycoca6:--noincremental", log.read_text())
        self.assertNotIn("kbuildsycoca5:", log.read_text())
        self.assertIn("update-desktop-database:", log.read_text())
        self.command("comic-editor-uninstall")
        self.assertFalse(desktop.exists())
        self.assertEqual(log.read_text().count("kbuildsycoca6:"), 3)

    def test_plasma5_refresh_fallback_and_user_edited_desktop_is_preserved(self):
        mock_bin = self.base / "mock"
        mock_bin.mkdir()
        log = self.base / "menu-refresh"
        tool = mock_bin / "kbuildsycoca5"
        tool.write_text('#!/bin/sh\nprintf "%s" "$*" > "$REFRESH_LOG"\n')
        tool.chmod(0o755)
        self.env.update(
            PATH=str(mock_bin) + ":" + os.environ["PATH"], REFRESH_LOG=str(log)
        )
        self.run_installer("--archive", self.archive)
        self.assertEqual(log.read_text(), "--noincremental")
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        desktop.write_text("user replacement")
        self.run_installer("--archive", self.archive, success=False)
        self.command("comic-editor-uninstall")
        self.assertEqual(desktop.read_text(), "user replacement")

    def test_unrelated_command_is_not_overwritten(self):
        self.bin.mkdir()
        command = self.bin / "comic-editor"
        command.write_text("unrelated")
        self.run_installer("--archive", self.archive, success=False)
        self.assertEqual(command.read_text(), "unrelated")
        self.assertFalse(self.root.exists())

    def test_unsafe_and_incomplete_archives_are_rejected(self):
        self.make_archive("one", {"../escaped": "bad"})
        self.run_installer("--archive", self.archive, success=False)
        self.assertFalse((self.data / "escaped").exists())
        with tarfile.open(self.archive, "w:gz"):
            pass
        self.run_installer("--archive", self.archive, success=False)
        self.assertFalse(self.root.exists())

    def test_uninstall_preserves_replaced_launcher(self):
        self.run_installer("--archive", self.archive)
        command = self.bin / "comic-compile"
        command.unlink()
        command.write_text("user replacement")
        self.command("comic-editor-uninstall")
        self.assertEqual(command.read_text(), "user replacement")

    def test_release_download_url_and_embedded_checksum(self):
        installer = self.base / "install-comic-editor.sh"
        PACKAGING.package(self.archive, installer, "example/comic_editor", "v1.2.3")
        self.assertNotIn("@SHA256@", installer.read_text())
        expected = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.assertIn(expected, installer.read_text())
        # Replace network access with a downloader that records the requested URL.
        mock_bin = self.base / "mock"
        mock_bin.mkdir()
        curl = mock_bin / "curl"
        curl.write_text(
            "#!/bin/bash\nwhile (($#)); do\n"
            ' if [[ "$1" == --output ]]; then output="$2"; shift 2;\n'
            ' else url="$1"; shift; fi\ndone\n'
            'printf "%s" "$url" > "$REQUEST_LOG"\n'
            'cp "$TEST_ARCHIVE" "$output"\n'
        )
        curl.chmod(0o755)
        self.env.update(
            PATH=str(mock_bin) + ":" + os.environ["PATH"],
            TEST_ARCHIVE=str(self.archive),
            REQUEST_LOG=str(self.base / "url"),
        )
        self.run_installer(script=installer)
        self.assertEqual(
            (self.base / "url").read_text(),
            "https://github.com/example/comic_editor/releases/download/"
            "v1.2.3/ComicEditor-linux-x64.tar.gz",
        )
        self.make_archive("tampered")
        self.run_installer(script=installer, success=False)
        self.assertEqual(self.command("comic-editor"), "one:\n")


if __name__ == "__main__":
    unittest.main()
