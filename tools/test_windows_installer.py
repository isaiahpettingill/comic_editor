"""Exercise real NSIS install, staging, and uninstall in a disposable directory.

The --registered check is for disposable CI runners: it also tests HKCU and the
Start menu. Local checks use /STAGE and leave the user's shell integration alone.
"""

import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def run(executable: Path, arguments: str) -> None:
    # /D and _?= must be last and UNQUOTED, including paths with spaces.
    command = f'"{executable}" {arguments}'
    completed = subprocess.run(command, timeout=180, check=False)
    if completed.returncode != 0:
        raise AssertionError(f"NSIS exited {completed.returncode}: {command}")


def check(installer: Path, payload: Path, registered: bool) -> None:
    if os.name != "nt":
        raise RuntimeError("The NSIS integration check requires Windows")
    import winreg

    registry = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\ComicEditor"

    def installed_path():
        try:
            with winreg.OpenKey(
                winreg.HKEY_CURRENT_USER,
                registry,
                0,
                winreg.KEY_READ | winreg.KEY_WOW64_32KEY,
            ) as key:
                return Path(winreg.QueryValueEx(key, "InstallLocation")[0])
        except FileNotFoundError:
            return None

    if registered and installed_path() is not None:
        raise RuntimeError(
            "Refusing to overwrite an existing user installation during this test"
        )
    prior = installed_path()

    def association(extension):
        try:
            with winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, rf"Software\Classes\{extension}"
            ) as key:
                return winreg.QueryValueEx(key, "")[0]
        except FileNotFoundError:
            return None

    prior_associations = {ext: association(ext) for ext in [".ctsc", ".cutscene"]}
    expected = json.loads((payload / "update.json").read_text())
    with tempfile.TemporaryDirectory(prefix="comic NSIS 'spaces' $ ") as temporary:
        target = Path(temporary) / "application"
        mode = "" if registered else "/STAGE "
        run(installer, f"/S {mode}/D={target}")
        assert (target / "ComicEditor.Desktop.exe").is_file()
        assert (target / "Uninstall.exe").is_file()
        assert json.loads((target / "update.json").read_text()) == expected
        assert installed_path() == (target if registered else prior)
        shortcut = (
            Path(os.environ["APPDATA"])
            / "Microsoft/Windows/Start Menu/Programs/ComicEditor.lnk"
        )
        if registered:
            assert shortcut.is_file()
            for ext in prior_associations:
                assert association(ext) == (
                    prior_associations[ext] or "ComicEditor.Cutscene"
                )
                with winreg.OpenKey(
                    winreg.HKEY_CURRENT_USER, rf"Software\Classes\{ext}\OpenWithProgids"
                ) as key:
                    assert winreg.QueryValueEx(key, "ComicEditor.Cutscene")[0] == ""
            with winreg.OpenKey(
                winreg.HKEY_CURRENT_USER,
                r"Software\Classes\ComicEditor.Cutscene\shell\open\command",
            ) as key:
                assert (
                    winreg.QueryValueEx(key, "")[0]
                    == f'"{target / "ComicEditor.Desktop.exe"}" "%1"'
                )
        sentinel = target / "keep-my-project.cutscene"
        sentinel.write_text("user project")
        user_symbols = target / "my-game.pdb"
        user_symbols.write_text("user debug file")
        old_symbols = [
            target / "ComicEditor.Desktop.pdb",
            target / "libSkiaSharp.pdb",
            target / "compiler/comic-compile.pdb",
            target / "compiler/libHarfBuzzSharp.pdb",
        ]
        for symbols in old_symbols:
            symbols.parent.mkdir(exist_ok=True)
            symbols.write_text("obsolete package symbols")
        (target / "update.json").write_text(
            json.dumps({"version": "0.0.0", "runtime": "win-x64"})
        )
        run(installer, f"/S {mode}/D={target}")
        assert json.loads((target / "update.json").read_text()) == expected
        assert sentinel.read_text() == "user project"
        assert all(not symbols.exists() for symbols in old_symbols)
        assert user_symbols.read_text() == "user debug file"
        # Registration-only must not recopy files or replace user changes.
        if registered:
            (target / "README.md").write_text("registration sentinel")
            run(installer, f"/S /REGISTER /VERSION={expected['version']} /D={target}")
            assert (target / "README.md").read_text() == "registration sentinel"
        # A running/locked app must not be partially removed or unregistered.
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [
            ctypes.c_wchar_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_void_p,
        ]
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.CreateFileW(
            str(target / "ComicEditor.Desktop.exe"), 0x80000000, 0, None, 3, 0, None
        )
        assert handle != ctypes.c_void_p(-1).value
        try:
            blocked = subprocess.run(
                f'"{target / "Uninstall.exe"}" /S _?={target}', timeout=30
            )
            assert blocked.returncode != 0
            assert (target / "ComicEditor.Desktop.exe").is_file()
            assert installed_path() == (target if registered else prior)
        finally:
            kernel.CloseHandle(handle)
        run(target / "Uninstall.exe", f"/S _?={target}")
        assert not (target / "ComicEditor.Desktop.exe").exists()
        assert not (target / "update.json").exists()
        assert sentinel.read_text() == "user project"
        assert installed_path() == prior
        if registered:
            assert not shortcut.exists()
        assert {
            ext: association(ext) for ext in prior_associations
        } == prior_associations
    print("NSIS install, update, and uninstall passed; user files preserved.")


if __name__ == "__main__":
    check(
        Path(sys.argv[1]).resolve(),
        Path(sys.argv[2]).resolve(),
        "--registered" in sys.argv,
    )
