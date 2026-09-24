import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location(
    "ci_version", Path(__file__).with_name("ci-version.py")
)
VERSION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERSION)


class ReleaseVersionTests(unittest.TestCase):
    def choose(
        self, releases=(), tags=None, event="push", ref="refs/heads/main", base="0.1.11"
    ):
        return VERSION.choose_version(base, "current", ref, event, releases, tags or {})

    def test_first_main_push_uses_configured_version(self):
        self.assertEqual(("0.1.11", True), self.choose(tags={"v0.1.10": "old"}))

    def test_next_push_bumps_patch_without_replacing_existing_release(self):
        self.assertEqual(("0.1.14", True), self.choose(tags={"v0.1.13": "old"}))

    def test_rerun_reuses_matching_draft(self):
        release = {"tag_name": "v0.1.12", "target_commitish": "current", "draft": True}
        self.assertEqual(("0.1.12", True), self.choose([release]))

    def test_rerun_does_not_replace_published_assets(self):
        release = {"tag_name": "v0.1.12", "target_commitish": "current", "draft": False}
        self.assertEqual(("0.1.12", False), self.choose([release]))
        release["target_commitish"] = "main"
        self.assertEqual(
            ("0.1.12", False), self.choose([release], {"v0.1.12": "current"})
        )

    def test_previous_failed_draft_reserves_its_number(self):
        release = {"tag_name": "v0.1.11", "target_commitish": "old", "draft": True}
        self.assertEqual(("0.1.12", True), self.choose([release]))

    def test_pr_and_other_branch_never_publish(self):
        self.assertEqual(("0.1.11", False), self.choose(event="pull_request"))
        self.assertEqual(("0.1.11", False), self.choose(ref="refs/heads/feature"))

    def test_explicit_tag_retains_its_version(self):
        self.assertEqual(("0.1.20", True), self.choose(ref="refs/tags/v0.1.20"))

    def test_refuse_old_release_series(self):
        with self.assertRaises(ValueError):
            self.choose(tags={"v1.0.0": "old"})

    def test_all_platforms_receive_matching_version(self):
        with patch.dict(
            "os.environ",
            COMIC_RELEASE_VERSION="0.1.15",
            COMIC_ANDROID_VERSION_CODE="1050",
        ):
            desktop = ET.fromstring(
                VERSION.stamped(b"<Project><Version>0.1.11</Version></Project>")
            )
            android = ET.fromstring(
                VERSION.stamped(
                    b"<Project><ApplicationVersion>12</ApplicationVersion><ApplicationDisplayVersion>0.1.11</ApplicationDisplayVersion></Project>",
                    True,
                )
            )
            self.assertEqual("0.1.15", desktop.findtext("Version"))
            self.assertEqual("0.1.15", android.findtext("ApplicationDisplayVersion"))
            self.assertEqual("1050", android.findtext("ApplicationVersion"))


if __name__ == "__main__":
    unittest.main()
