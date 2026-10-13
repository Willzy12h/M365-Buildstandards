"""Offline tests for the release publisher's pin and package checks. No network or GitHub calls."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("publisher", HERE / "Publish-ValidatedRelease.py")
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)

SOURCE = "a" * 40
VERSION = "1.2.3-preview.4"


def write_pin(root, **overrides):
    (root / "build/releases").mkdir(parents=True, exist_ok=True)
    (root / "build/releases/notes.md").write_text("Synthetic notes.\n", encoding="utf-8")
    pin = {
        "schemaVersion": 1, "version": VERSION, "sourceCommit": SOURCE, "sourceBranch": "integration",
        "validatedRun": "123", "standard": "2026.09.30", "packageSha256": "b" * 64, "extractedFiles": 3,
        "notes": "build/releases/notes.md", "approval": "Synthetic approval.",
        "distribution": "Unsigned internal prerelease.", "pending": ["Live acceptance"],
    }
    pin.update(overrides)
    pin = {key: value for key, value in pin.items() if value is not None}
    (root / f"build/releases/{pin.get('version', VERSION)}.json").write_text(json.dumps(pin), encoding="utf-8")
    return pin


class PinTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def refused(self, message, **overrides):
        write_pin(self.root, **overrides)
        with self.assertRaisesRegex(ValueError, message):
            publisher.load_pin(VERSION, self.root)

    def test_valid_pin_loads(self):
        write_pin(self.root)
        self.assertEqual(publisher.load_pin(VERSION, self.root)["validatedRun"], "123")

    def test_repository_pins_are_valid(self):
        self.assertIn("1.1.0-preview.18", publisher.validate_all_pins(HERE.parent))

    def test_malformed_version_refused(self):
        for version in ("1.2", "v1.2.3", "1.2.3-beta.1", "../1.2.3", "1.2.3\n"):
            with self.assertRaisesRegex(ValueError, "Version must look like"):
                publisher.load_pin(version, self.root)

    def test_missing_pin_refused(self):
        with self.assertRaisesRegex(ValueError, "No reviewed release pin"):
            publisher.load_pin(VERSION, self.root)

    def test_unknown_member_refused(self):
        self.refused("unknown members", extra="x")

    def test_missing_member_refused(self):
        self.refused("missing approval", approval=None)

    def test_wrong_types_refused(self):
        self.refused("extractedFiles has the wrong type", extractedFiles="3")
        self.refused("extractedFiles has the wrong type", extractedFiles=True)
        self.refused("validatedRun has the wrong type", validatedRun=123)

    def test_version_must_match_file_name(self):
        write_pin(self.root)
        path = self.root / f"build/releases/{VERSION}.json"
        pin = json.loads(path.read_text(encoding="utf-8"))
        pin["version"] = "1.2.3-preview.5"
        path.write_text(json.dumps(pin), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "does not match its file name"):
            publisher.load_pin(VERSION, self.root)

    def test_identities_must_be_exact(self):
        self.refused("full commit SHA", sourceCommit="abc123")
        self.refused("SHA-256", packageSha256="B" * 64)
        self.refused("run ID", validatedRun="12a")
        self.refused("release ID", standard="latest")
        self.refused("branch name", sourceBranch="main; rm")

    def test_notes_path_must_stay_in_repository(self):
        self.refused("repository-relative", notes="../outside.md")
        self.refused("repository-relative", notes="/etc/passwd.md")
        self.refused("does not exist", notes="build/releases/missing.md")

    def test_blank_approval_refused(self):
        self.refused("approval must be recorded", approval="  ")


class ContextTests(unittest.TestCase):
    def test_feature_branch_dispatch_refused(self):
        os.environ["GITHUB_REF"] = "refs/heads/claude/anything"
        try:
            with self.assertRaisesRegex(ValueError, "main or integration"):
                publisher.check_context()
        finally:
            del os.environ["GITHUB_REF"]

    def test_integration_dispatch_allowed(self):
        os.environ["GITHUB_REF"] = "refs/heads/integration"
        try:
            publisher.check_context()
        finally:
            del os.environ["GITHUB_REF"]


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.portable = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def build(self, version=VERSION, dirty=False, neutral=False):
        name = f"BuildStandard-{VERSION}-win-x64.zip" if neutral else publisher.package_name(VERSION)
        package = self.portable / name
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr("VERSION.json", json.dumps({
                "sourceCommit": SOURCE, "version": version, "sourceTreeDirty": dirty,
                "defaultStandard": "2026.09.30", "runtime": "win-x64", "selfContained": True,
                "dotnetSdk": "10.0.401", "dotnetRuntime": "10.0.1"}))
            archive.writestr("standards/2026.09.30.json", "{}")
        sha = hashlib.sha256(package.read_bytes()).hexdigest()
        (self.portable / (package.name + ".sha256")).write_text(f"{sha}  {package.name}\n", encoding="utf-8")
        result = {"sourceCommit": SOURCE, "version": VERSION, "standard": "2026.09.30", "zipSha256": sha,
                  "extractedFiles": 3, "tenantOperationsPerformed": False}
        for flag in ("stageBytesMatch", "checksumsVerified", "blankConnectionSettings", "emptyEvidenceFolders",
                     "actualPackagedFirstLaunch", "gracefulShutdown", "accessibilityAssemblyVerified",
                     "textBoxContextMenuOpened", "contextMenuCopyVerified"):
            result[flag] = True
        (self.portable / "portable-verification.json").write_text(json.dumps(result), encoding="utf-8")
        return {"version": VERSION, "sourceCommit": SOURCE, "packageSha256": sha,
                "standard": "2026.09.30", "extractedFiles": 3}

    def test_tested_package_accepted(self):
        pin = self.build()
        metadata, catalogue = publisher.validate_package(pin, self.portable)
        self.assertEqual(metadata["version"], VERSION)
        self.assertEqual(catalogue, hashlib.sha256(b"{}").hexdigest())

    def test_neutral_named_tested_package_accepted_without_changing_old_pins(self):
        pin = self.build(neutral=True)
        metadata, catalogue = publisher.validate_package(pin, self.portable)
        self.assertEqual(metadata["version"], VERSION)
        self.assertEqual(catalogue, hashlib.sha256(b"{}").hexdigest())

    def test_two_recognised_package_names_are_refused_as_ambiguous(self):
        pin = self.build()
        (self.portable / f"BuildStandard-{VERSION}-win-x64.zip").write_bytes(b"ambiguous duplicate")
        with self.assertRaisesRegex(ValueError, "exactly one recognised portable ZIP"):
            publisher.validate_package(pin, self.portable)

    def test_neutral_name_does_not_weaken_pinned_bytes_or_sidecar(self):
        pin = self.build(neutral=True)
        pin["packageSha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "independently recorded"):
            publisher.validate_package(pin, self.portable)
        pin = self.build(neutral=True)
        (self.portable / f"BuildStandard-{VERSION}-win-x64.zip.sha256").write_text("wrong")
        with self.assertRaisesRegex(ValueError, "sidecar"):
            publisher.validate_package(pin, self.portable)

    def test_unrecognised_neutral_filename_is_not_discovered(self):
        pin = self.build(neutral=True)
        package = self.portable / f"BuildStandard-{VERSION}-win-x64.zip"
        package.rename(self.portable / "BuildStandard-arbitrary.zip")
        with self.assertRaisesRegex(ValueError, "exactly one recognised portable ZIP"):
            publisher.validate_package(pin, self.portable)

    def test_prepare_keeps_the_actual_neutral_filename_and_its_verified_bytes(self):
        import shutil
        pin = self.build(neutral=True)
        with tempfile.TemporaryDirectory() as job:
            root = Path(job)
            full_pin = write_pin(root)
            full_pin.update(pin)
            pin = full_pin
            pin.update({"notes": "build/releases/notes.md", "validatedRun": "123", "pending": ["Synthetic live acceptance unrun"]})
            shutil.copytree(self.portable, root / "release-input/portable")
            for folder in ("ui", "standards"):
                target = root / "release-input" / folder
                target.mkdir()
                (target / "synthetic.txt").write_text("Synthetic fixture only.")
            previous = os.getcwd()
            try:
                os.chdir(root)
                output = publisher.prepare(pin)
                name = f"BuildStandard-{VERSION}-win-x64.zip"
                self.assertEqual(publisher.digest(output / name), pin["packageSha256"])
                record = json.loads((output / "RELEASE-RECORD.json").read_text())
                self.assertEqual(record["package"], name)
                self.assertEqual(record["product"], "BuildStandard")
                self.assertIn(name, (output / "SHA256SUMS.txt").read_text())
            finally:
                os.chdir(previous)

    def test_prepare_refuses_package_corrupted_during_staging(self):
        import shutil
        from unittest.mock import patch
        pin = self.build(neutral=True)
        with tempfile.TemporaryDirectory() as job:
            root = Path(job); full_pin = write_pin(root); full_pin.update(pin); pin = full_pin
            shutil.copytree(self.portable, root / "release-input/portable")
            for folder in ("ui", "standards"):
                target = root / "release-input" / folder; target.mkdir()
                (target / "synthetic.txt").write_text("Synthetic fixture only.")
            original_copy = shutil.copyfile
            def corrupt_copy(source, destination, *args, **kwargs):
                result = original_copy(source, destination, *args, **kwargs)
                if Path(destination).name == f"BuildStandard-{VERSION}-win-x64.zip":
                    with Path(destination).open("ab") as stream: stream.write(b"changed during staging")
                return result
            previous = os.getcwd()
            try:
                os.chdir(root)
                with patch("shutil.copyfile", side_effect=corrupt_copy):
                    with self.assertRaisesRegex(ValueError, "independently recorded tested SHA-256"):
                        publisher.prepare(pin)
                self.assertFalse((root / "release-output/RELEASE-RECORD.json").exists())
            finally:
                os.chdir(previous)

    def test_publish_refuses_changed_staged_package_before_any_github_operation(self):
        from unittest.mock import patch
        pin = self.build(neutral=True)
        package = self.portable / f"BuildStandard-{VERSION}-win-x64.zip"
        with package.open("ab") as stream: stream.write(b"changed after preparation")
        with patch.object(publisher, "api", side_effect=AssertionError("GitHub must not be called")), \
                patch.object(publisher, "gh", side_effect=AssertionError("GitHub must not be called")):
            with self.assertRaisesRegex(ValueError, "independently recorded tested SHA-256"):
                publisher.publish(pin, self.portable)

    def test_changed_bytes_refused(self):
        pin = self.build()
        pin["packageSha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "does not match the independently recorded"):
            publisher.validate_package(pin, self.portable)

    def test_dirty_build_refused(self):
        pin = self.build(dirty=True)
        with self.assertRaisesRegex(ValueError, "clean self-contained"):
            publisher.validate_package(pin, self.portable)

    def test_different_file_count_refused(self):
        pin = self.build()
        pin["extractedFiles"] = 4
        with self.assertRaisesRegex(ValueError, "different source/artifact"):
            publisher.validate_package(pin, self.portable)


class NotesTests(unittest.TestCase):
    def test_notes_carry_fingerprint_and_allowlisting_route(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            pin = write_pin(root)
            cwd = os.getcwd()
            os.chdir(root)
            try:
                notes = publisher.release_notes(pin)
            finally:
                os.chdir(cwd)
        self.assertIn("b" * 64, notes)
        self.assertIn("unsigned internal prerelease", notes)
        self.assertIn("Do not bypass application control", notes)

    def test_ga_is_not_a_prerelease(self):
        self.assertFalse(publisher.prerelease({"version": "1.1.0"}))
        self.assertTrue(publisher.prerelease({"version": "1.1.0-preview.19"}))


if __name__ == "__main__":
    result = unittest.main(exit=False, verbosity=2).result
    sys.exit(0 if result.wasSuccessful() else 1)
