"""Allocate immutable releases for main, and stamp every platform consistently."""

import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def version_tuple(value):
    if not re.fullmatch(r"\d+\.\d+\.\d+", value):
        raise ValueError(f"Expected a stable three-part version: {value}")
    return tuple(map(int, value.split(".")))


def choose_version(base, sha, ref, event, releases, tags):
    version_tuple(base)
    publish = event != "pull_request" and (
        ref == "refs/heads/main" or ref.startswith("refs/tags/v")
    )
    if not publish:
        return base, False
    stable = [r for r in releases if re.fullmatch(r"v\d+\.\d+\.\d+", r["tag_name"])]
    if ref.startswith("refs/tags/v"):
        requested = ref.removeprefix("refs/tags/v")
        version_tuple(requested)
        existing = next((r for r in stable if r["tag_name"] == "v" + requested), None)
        return requested, existing is None or existing["draft"]
    same = [
        r
        for r in stable
        if r["target_commitish"] == sha or tags.get(r["tag_name"]) == sha
    ]
    if same:
        latest = max(same, key=lambda r: version_tuple(r["tag_name"][1:]))
        return latest["tag_name"][1:], latest["draft"]
    used = set(tags) | {r["tag_name"] for r in stable}
    major, minor, patch = version_tuple(base)
    for tag in used:
        if re.fullmatch(r"v\d+\.\d+\.\d+", tag):
            a, b, c = version_tuple(tag[1:])
            if (a, b) > (major, minor):
                raise ValueError(
                    "The configured release series is older than a published version"
                )
            if (a, b) == (major, minor):
                patch = max(patch, c + 1)
    return f"{major}.{minor}.{patch}", True


def plan():
    base = ET.parse(ROOT / "Directory.Build.props").findtext(".//Version")
    ref = os.environ["GITHUB_REF"]
    event = os.environ["GITHUB_EVENT_NAME"]
    releases, tags = [], {}
    if event != "pull_request":
        pages = json.loads(
            subprocess.check_output(
                [
                    "gh",
                    "api",
                    f"repos/{os.environ['GITHUB_REPOSITORY']}/releases?per_page=100",
                    "--paginate",
                    "--slurp",
                ]
            )
        )
        releases = [release for page in pages for release in page]
        lines = subprocess.check_output(
            [
                "git",
                "for-each-ref",
                "--format=%(refname:short) %(objectname) %(*objectname)",
                "refs/tags/v*",
            ],
            cwd=ROOT,
            text=True,
        ).splitlines()
        for line in lines:
            parts = line.split()
            tags[parts[0]] = parts[-1]
    version, publish = choose_version(
        base, os.environ["GITHUB_SHA"], ref, event, releases, tags
    )
    base_code = int(
        ET.parse(ROOT / "ComicEditor.Android/ComicEditor.Android.csproj").findtext(
            ".//ApplicationVersion"
        )
    )
    code = max(base_code, 1000 + int(os.environ["GITHUB_RUN_NUMBER"]))
    if code > 2_100_000_000:
        raise ValueError("Android version code exceeds the platform limit")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(
            f"version={version}\nandroid_code={code}\npublish={str(publish).lower()}\n"
        )
    print(f"Version {version}, Android code {code}, publish={publish}")


def stamped(data, android=False):
    version = os.environ["COMIC_RELEASE_VERSION"]
    version_tuple(version)
    root = ET.fromstring(data)
    if android:
        code = int(os.environ["COMIC_ANDROID_VERSION_CODE"])
        if not 1 <= code <= 2_100_000_000:
            raise ValueError("Invalid Android version code")
        root.find(".//ApplicationVersion").text = str(code)
        root.find(".//ApplicationDisplayVersion").text = version
    else:
        root.find(".//Version").text = version
    return ET.tostring(root, encoding="utf-8") + b"\n"


def stamp():
    for relative, android in [
        ("Directory.Build.props", False),
        ("ComicEditor.Android/ComicEditor.Android.csproj", True),
    ]:
        path = ROOT / relative
        path.write_bytes(stamped(path.read_bytes(), android))


def source(destination):
    # Archive the exact checked-out commit, including the matching version stamps.
    original = subprocess.check_output(
        ["git", "archive", "--format=zip", "HEAD"], cwd=ROOT
    )
    with (
        zipfile.ZipFile(io.BytesIO(original)) as incoming,
        zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as output,
    ):
        for entry in incoming.infolist():
            data = incoming.read(entry)
            if entry.filename == "Directory.Build.props":
                data = stamped(data)
            if entry.filename == "ComicEditor.Android/ComicEditor.Android.csproj":
                data = stamped(data, True)
            output.writestr(entry, data)


if __name__ == "__main__":
    if sys.argv[1] == "plan":
        plan()
    elif sys.argv[1] == "stamp":
        stamp()
    elif sys.argv[1] == "source":
        source(sys.argv[2])
    else:
        raise ValueError("Expected plan, stamp, or source")
