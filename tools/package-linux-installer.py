"""Package a reusable installer with the pinned desktop release signing key."""

import hashlib
import os
from pathlib import Path
import re


def package(archive: Path, output: Path, repository: str, public_key: str) -> None:
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repository):
        raise ValueError("A GitHub owner/repository is required")
    if not public_key.startswith("-----BEGIN PUBLIC KEY-----\n") or not public_key.rstrip().endswith("-----END PUBLIC KEY-----"):
        raise ValueError("A PEM release signing key is required")
    with archive.open("rb") as source:
        checksum = hashlib.file_digest(source, "sha256").hexdigest()
    template = Path(__file__).with_name("install-linux.sh").read_text()
    for key, value in {
        "@REPOSITORY@": repository,
        "@PUBLIC_KEY@": public_key.rstrip(),
    }.items():
        template = template.replace(key, value)
    output.write_text(template, newline="\n")
    output.chmod(0o755)
    archive.with_name(archive.name + ".sha256").write_text(
        f"{checksum}  {archive.name}\n", newline="\n"
    )


if __name__ == "__main__":
    package(
        Path("ComicEditor-linux-x64.tar.gz"),
        Path("install-comic-editor.sh"),
        os.environ["GITHUB_REPOSITORY"],
        Path(__file__).resolve().parent.parent.joinpath("signing/desktop-public.pem").read_text(),
    )
