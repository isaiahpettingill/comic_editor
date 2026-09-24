"""Stamp the standalone installer with this release's URL and archive checksum."""

import hashlib
import os
from pathlib import Path
import re
from urllib.parse import quote


def package(archive: Path, output: Path, repository: str, tag: str) -> None:
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repository) or not tag:
        raise ValueError("A GitHub owner/repository and release tag are required")
    with archive.open("rb") as source:
        checksum = hashlib.file_digest(source, "sha256").hexdigest()
    template = Path(__file__).with_name("install-linux.sh").read_text()
    for key, value in {
        "@REPOSITORY@": repository,
        "@TAG@": quote(tag, safe=""),
        "@SHA256@": checksum,
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
        os.environ["GITHUB_REF_NAME"],
    )
