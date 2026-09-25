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
                "comic-editor.svg": f"<svg xmlns='http://www.w3.org/2000/svg'><title>{version}</title></svg>",
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
        self.assertTrue((self.bin / "comic-editor-update").is_symlink())
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        self.assertTrue(desktop.is_file())
        self.assertFalse(desktop.is_symlink())
        self.assertIn('Exec="', desktop.read_text())
        self.assertIn('" %f', desktop.read_text())
        self.assertIn("StartupWMClass=org.comiceditor.storyboard", desktop.read_text())
        self.assertIn(
            "MimeType=application/vnd.comiceditor.cutscene;", desktop.read_text()
        )
        mime = self.data / "mime/packages/org.comiceditor.storyboard.xml"
        self.assertIn('pattern="*.ctsc"', mime.read_text())
        self.assertIn('pattern="*.cutscene"', mime.read_text())
        self.assertIn(r"\\$", desktop.read_text())
        icon = self.data / "icons/hicolor/scalable/apps/org.comiceditor.storyboard.svg"
        self.assertTrue(icon.is_file())
        self.assertFalse(icon.is_symlink())
        self.assertIn("one", icon.read_text())
        document = self.data / "example.cutscene"
        document.write_text("keep this project")
        self.make_archive("two")
        self.run_installer("--archive", self.archive)
        self.assertEqual(self.command("comic-editor"), "two:\n")
        self.assertFalse(icon.is_symlink())
        self.assertIn("two", icon.read_text())
        self.assertEqual(len(list((self.root / "releases").iterdir())), 1)
        # Rerunning an installation is also supported.
        self.run_installer("--archive", self.archive)
        self.command("comic-editor-uninstall")
        self.assertFalse(self.root.exists())
        self.assertFalse(mime.exists())
        self.assertFalse(desktop.is_symlink())
        self.assertFalse((self.bin / "comic-editor").is_symlink())
        self.assertFalse((self.bin / "comic-editor-update").is_symlink())
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

    def test_plasma5_refresh_and_launcher_repair(self):
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
        self.run_installer("--archive", self.archive)
        self.assertIn("StartupWMClass=org.comiceditor.storyboard", desktop.read_text())
        self.command("comic-editor-uninstall")
        self.assertFalse(desktop.exists())

    def test_unrelated_command_is_not_overwritten(self):
        self.bin.mkdir()
        command = self.bin / "comic-editor"
        command.write_text("unrelated")
        self.run_installer("--archive", self.archive, success=False)
        self.assertEqual(command.read_text(), "unrelated")
        self.assertFalse(self.root.exists())

    def test_repair_recovers_damaged_desktop_and_icon_without_replacing_commands(self):
        self.run_installer("--archive", self.archive)
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        icon = self.data / "icons/hicolor/scalable/apps/org.comiceditor.storyboard.svg"
        desktop.write_text("damaged desktop entry")
        icon.write_text("damaged icon")
        self.run_installer("--archive", self.archive)
        self.assertIn("X-ComicEditor-Managed=true", desktop.read_text())
        self.assertIn("<title>one</title>", icon.read_text())
        self.assertEqual(self.command("comic-editor"), "one:\n")
        command = self.bin / "comic-compile"
        command.unlink()
        command.write_text("user replacement")
        self.run_installer("--archive", self.archive, "--repair", success=False)
        self.assertEqual(command.read_text(), "user replacement")

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

    def test_saved_installer_fetches_latest_and_repairs_on_every_run(self):
        private = self.base / "private.pem"
        signature = self.base / "archive.sig"
        subprocess.run(
            ["openssl", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(private)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        public = subprocess.check_output(
            ["openssl", "pkey", "-in", str(private), "-pubout"], text=True
        )

        def sign():
            subprocess.run(
                ["openssl", "dgst", "-sha256", "-sign", str(private),
                 "-sigopt", "rsa_padding_mode:pss", "-sigopt", "rsa_pss_saltlen:32",
                 "-out", str(signature), str(self.archive)],
                check=True,
            )

        sign()
        installer = self.base / "install-comic-editor.sh"
        PACKAGING.package(self.archive, installer, "example/comic_editor", public)
        self.assertNotIn("@PUBLIC_KEY@", installer.read_text())
        expected = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.assertIn(expected, self.archive.with_name(self.archive.name + ".sha256").read_text())
        # Replace the network with the current archive and its real signature.
        mock_bin = self.base / "mock"
        mock_bin.mkdir()
        curl = mock_bin / "curl"
        curl.write_text(
            "#!/bin/bash\nwhile (($#)); do\n"
            ' if [[ "$1" == --output ]]; then output="$2"; shift 2;\n'
            ' else url="$1"; shift; fi\ndone\n'
            'printf "%s\\n" "$url" >> "$REQUEST_LOG"\n'
            'if [[ "$url" == *.sig ]]; then cp "$TEST_SIGNATURE" "$output"; '
            'else cp "$TEST_ARCHIVE" "$output"; fi\n'
        )
        curl.chmod(0o755)
        self.env.update(
            PATH=str(mock_bin) + ":" + os.environ["PATH"],
            TEST_ARCHIVE=str(self.archive),
            TEST_SIGNATURE=str(signature),
            REQUEST_LOG=str(self.base / "url"),
        )
        self.run_installer(script=installer)
        self.assertEqual(
            (self.base / "url").read_text().splitlines(),
            ["https://github.com/example/comic_editor/releases/latest/download/ComicEditor-linux-x64.tar.gz",
             "https://github.com/example/comic_editor/releases/latest/download/ComicEditor-linux-x64.tar.gz.sig"],
        )
        desktop = self.data / "applications/org.comiceditor.storyboard.desktop"
        icon = self.data / "icons/hicolor/scalable/apps/org.comiceditor.storyboard.svg"
        desktop.write_text("damaged desktop")
        icon.write_text("damaged icon")
        self.make_archive("two")
        sign()
        self.run_installer(script=self.bin / "comic-editor-update")
        self.assertEqual(self.command("comic-editor"), "two:\n")
        self.assertIn("StartupWMClass=org.comiceditor.storyboard", desktop.read_text())
        self.assertIn("<title>two</title>", icon.read_text())
        self.make_archive("tampered")
        self.run_installer(script=installer, success=False)
        self.assertEqual(self.command("comic-editor"), "two:\n")

    def test_main_branch_packages_reusable_installer(self):
        env = dict(
            self.env,
            GITHUB_REPOSITORY="example/comic_editor",
            GITHUB_REF_NAME="main",
            COMIC_RELEASE_VERSION="1.2.3",
        )
        subprocess.run(
            ["python3", str(TOOLS / "package-linux-installer.py")],
            cwd=self.base,
            env=env,
            check=True,
        )
        installer = (self.base / "install-comic-editor.sh").read_text()
        self.assertIn("/releases/latest/download/", installer)
        self.assertNotIn("RELEASE_TAG=", installer)
        self.assertIn("-----BEGIN PUBLIC KEY-----", installer)


if __name__ == "__main__":
    unittest.main()
