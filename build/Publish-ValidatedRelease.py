"""Promote an explicitly approved, already tested CI artifact as a GitHub release, without rebuilding it.

Each release is pinned by a reviewed file, build/releases/<version>.json, that names the exact source commit,
the successful build run, the tested ZIP's SHA-256 and the expected package identity. A new release therefore
still needs a reviewed source change; this script only removes the need to copy the publisher for each version.
It runs through the manual publish-release.yml workflow, gated by the release environment. No tenant
credentials, branch merges, main/integration writes or live operations occur.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

REPOSITORY = "Willzy12h/M365-Buildstandards"
PINS = Path("build/releases")
WORKFLOW = ".github/workflows/build.yml"
# Only reviewed long-lived branches may dispatch a publication; a feature branch could carry an unreviewed pin.
PUBLISHING_REFS = ("refs/heads/main", "refs/heads/integration")
VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+(-preview\.\d+)?$")
PIN_FIELDS = {
    "schemaVersion": int, "version": str, "sourceCommit": str, "sourceBranch": str, "validatedRun": str,
    "standard": str, "packageSha256": str, "extractedFiles": int, "notes": str, "approval": str,
    "distribution": str, "pending": list,
}
OPTIONAL_PIN_FIELDS = {"reviewPrompt": str}
REVIEW_ARTIFACTS = ("portable-windows-review", "synthetic-ui-review", "standard-definition-exports")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load_pin(version, root=Path(".")):
    """Read and strictly validate one reviewed release pin. Unknown or missing members are refused."""
    require(isinstance(version, str) and VERSION_PATTERN.fullmatch(version) is not None,
            "Version must look like 1.2.3 or 1.2.3-preview.4.")
    path = root / PINS / f"{version}.json"
    require(path.is_file(), f"No reviewed release pin exists at {path.as_posix()}.")
    pin = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(pin, dict), "A release pin must be a JSON object.")
    unknown = set(pin) - set(PIN_FIELDS) - set(OPTIONAL_PIN_FIELDS)
    require(not unknown, f"Release pin has unknown members: {', '.join(sorted(unknown))}.")
    for name, kind in PIN_FIELDS.items():
        require(name in pin, f"Release pin is missing {name}.")
    for name, kind in {**PIN_FIELDS, **OPTIONAL_PIN_FIELDS}.items():
        if name in pin:
            # bool is an int subclass; a true/false count or schema version is still wrong.
            require(isinstance(pin[name], kind) and not isinstance(pin[name], bool),
                    f"Release pin member {name} has the wrong type.")
    require(pin["schemaVersion"] == 1, "Unsupported release pin schema version.")
    require(pin["version"] == version, "Release pin version does not match its file name.")
    require(re.fullmatch(r"[0-9a-f]{40}", pin["sourceCommit"]) is not None, "sourceCommit must be a full commit SHA.")
    require(re.fullmatch(r"[0-9]{1,20}", pin["validatedRun"]) is not None, "validatedRun must be a run ID.")
    require(re.fullmatch(r"[0-9a-f]{64}", pin["packageSha256"]) is not None, "packageSha256 must be a SHA-256.")
    require(re.fullmatch(r"\d{4}\.\d{2}\.\d{1,2}", pin["standard"]) is not None, "standard must be a release ID.")
    require(re.fullmatch(r"[A-Za-z0-9._/-]{1,200}", pin["sourceBranch"]) is not None, "sourceBranch is not a branch name.")
    require(pin["extractedFiles"] > 0, "extractedFiles must be positive.")
    require(all(isinstance(item, str) and item for item in pin["pending"]), "pending must list text items.")
    for name in ("approval", "distribution"):
        require(pin[name].strip() != "", f"{name} must be recorded.")
    for name in ("notes", "reviewPrompt"):
        if name in pin:
            relative = Path(pin[name])
            require(not relative.is_absolute() and ".." not in relative.parts and relative.suffix == ".md",
                    f"{name} must be a repository-relative Markdown path.")
            require((root / relative).is_file(), f"{name} file {pin[name]} does not exist.")
    return pin


def validate_all_pins(root=Path(".")):
    pins = sorted((root / PINS).glob("*.json"))
    require(pins, "No release pins found.")
    for path in pins:
        load_pin(path.stem, root)
    return [path.stem for path in pins]


def package_name(version):
    return f"M365-BuildStandard-Tool-{version}-win-x64.zip"


def gh(*arguments, missing_ok=False):
    result = subprocess.run(["gh", *arguments, "--repo", REPOSITORY]
                            if arguments[0] == "release" else ["gh", *arguments],
                            capture_output=True, text=True)
    if result.returncode:
        if missing_ok and "HTTP 404" in result.stderr:
            return None
        # Do not echo headers, signed artifact locations or token-bearing errors.
        raise RuntimeError(f"GitHub operation {arguments[0]} failed (exit {result.returncode}).")
    return result.stdout


def api(path, missing_ok=False):
    value = gh("api", f"repos/{REPOSITORY}/{path}", missing_ok=missing_ok)
    return None if value is None else json.loads(value)


def check_context():
    require(os.environ.get("GITHUB_REPOSITORY", REPOSITORY) == REPOSITORY,
            "Publication is restricted to the original repository.")
    ref = os.environ.get("GITHUB_REF")
    require(ref is None or ref in PUBLISHING_REFS,
            "Publication must be dispatched from main or integration, where release pins are reviewed.")


def check_run(pin):
    run_id = pin["validatedRun"]
    run = api(f"actions/runs/{run_id}")
    require(run["head_sha"] == pin["sourceCommit"] and run["head_branch"] == pin["sourceBranch"]
            and run["head_repository"]["full_name"] == REPOSITORY
            and run["event"] == "push" and run["path"] == WORKFLOW
            and run["status"] == "completed" and run["conclusion"] == "success",
            "The pinned source run is not a successful original-repository build of the pinned commit.")
    jobs = api(f"actions/runs/{run_id}/jobs?per_page=100")["jobs"]
    for name in ("build", "standard", "secrets"):
        matches = [job for job in jobs if job["name"] == name]
        require(len(matches) == 1 and matches[0]["conclusion"] == "success",
                f"The source run lacks successful {name} validation.")
    artifacts = api(f"actions/runs/{run_id}/artifacts?per_page=100")["artifacts"]
    for name in REVIEW_ARTIFACTS:
        matches = [artifact for artifact in artifacts if artifact["name"] == name]
        require(len(matches) == 1 and not matches[0]["expired"],
                f"The pinned {name} artifact is missing or expired.")
    print(f"Verified successful source run {run_id} at {pin['sourceCommit']}.")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_package(pin, portable):
    version, source, expected = pin["version"], pin["sourceCommit"], pin["packageSha256"]
    package = portable / package_name(version)
    require(package.is_file() and digest(package) == expected,
            "Portable ZIP does not match the independently recorded tested SHA-256.")
    sidecar = portable / (package.name + ".sha256")
    require(sidecar.read_text(encoding="utf-8").strip() == f"{expected}  {package.name}",
            "Portable ZIP sidecar does not identify the tested bytes.")
    result = json.loads((portable / "portable-verification.json").read_text(encoding="utf-8"))
    require(all(result.get(key) == value for key, value in {
        "sourceCommit": source, "version": version, "standard": pin["standard"],
        "zipSha256": expected, "extractedFiles": pin["extractedFiles"],
    }.items()), "Extracted-package evidence identifies a different source/artifact.")
    flags = ("stageBytesMatch", "checksumsVerified", "blankConnectionSettings",
             "emptyEvidenceFolders", "actualPackagedFirstLaunch", "gracefulShutdown",
             "accessibilityAssemblyVerified", "textBoxContextMenuOpened", "contextMenuCopyVerified")
    require(all(result.get(flag) is True for flag in flags)
            and result.get("tenantOperationsPerformed") is False,
            "Required offline portable checks did not all pass.")
    with zipfile.ZipFile(package) as archive:
        def read(name, maximum):
            entries = [entry for entry in archive.infolist() if entry.filename.replace("\\", "/") == name]
            require(len(entries) == 1 and entries[0].file_size <= maximum,
                    f"Missing, duplicate or oversized {name} in the tested ZIP.")
            return archive.read(entries[0])
        metadata = json.loads(read("VERSION.json", 65536))
        require(metadata.get("sourceCommit") == source and metadata.get("version") == version
                and metadata.get("sourceTreeDirty") is False
                and metadata.get("defaultStandard") == pin["standard"]
                and metadata.get("runtime") == "win-x64" and metadata.get("selfContained") is True
                and metadata.get("dotnetSdk") == "10.0.401"
                and str(metadata.get("dotnetRuntime", "")).startswith("10."),
                "Package metadata does not identify a clean self-contained .NET 10 source build.")
        catalogue = read(f"standards/{pin['standard']}.json", 4 * 1024 * 1024)
    return metadata, hashlib.sha256(catalogue).hexdigest()


def prerelease(pin):
    return "-preview." in pin["version"]


def release_notes(pin):
    """The reviewed notes, followed by provenance generated from the pin so the two cannot disagree."""
    notes = Path(pin["notes"]).read_text(encoding="utf-8").rstrip()
    kind = "unsigned internal prerelease" if prerelease(pin) else "unsigned internal release"
    return f"""{notes}

## Verify before running

This is an **{kind}**. Windows will not show a verified publisher. Before extracting, compare the ZIP's SHA-256 with the value below and with the fingerprint published through your organisation's trusted channel:

**Application ZIP SHA-256: `{pin['packageSha256']}`**

If application control blocks the tool, ask your security owner to allow it under the organisation's policy (for example a file-hash rule for this exact build). Do not bypass application control.

Source: `{pin['sourceCommit']}`. Standard: **{pin['standard']}**. [Validated Windows run](https://github.com/{REPOSITORY}/actions/runs/{pin['validatedRun']}).
"""


def prepare(pin):
    inputs = Path("release-input")
    output = Path("release-output")
    require(not output.exists(), "Release staging already exists; use a fresh job.")
    metadata, catalogue_digest = validate_package(pin, inputs / "portable")
    package = package_name(pin["version"])
    output.mkdir()
    for name in (package, package + ".sha256", "portable-verification.json"):
        shutil.copyfile(inputs / "portable" / name, output / name)
    for folder, name in (("ui", "native-ui-review.zip"), ("standards", "standard-definition-exports.zip")):
        root = inputs / folder
        files = sorted(path for path in root.rglob("*") if path.is_file())
        require(files and not any(path.is_symlink() for path in root.rglob("*")),
                f"Missing or linked {folder} review assets.")
        with zipfile.ZipFile(output / name, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in files:
                archive.write(path, path.relative_to(root).as_posix())
    if "reviewPrompt" in pin:
        shutil.copyfile(pin["reviewPrompt"], output / "CLAUDE-REVIEW-PROMPT.md")
    record = {
        "product": "M365 BuildStandard Tool", "version": pin["version"], "prerelease": prerelease(pin),
        "sourceCommit": pin["sourceCommit"], "validatedRun": pin["validatedRun"], "package": package,
        "packageBytes": (output / package).stat().st_size, "packageSha256": pin["packageSha256"],
        "standard": pin["standard"], "catalogueSha256": catalogue_digest,
        "dotnetSdk": metadata["dotnetSdk"], "dotnetRuntime": metadata["dotnetRuntime"],
        "publisherCommit": os.environ.get("GITHUB_SHA", "local-prepare-only"),
        "publisherRun": os.environ.get("GITHUB_RUN_ID", "local-prepare-only"),
        "approval": pin["approval"], "distribution": pin["distribution"], "pending": pin["pending"],
        "tenantOperationsPerformed": False,
    }
    (output / "RELEASE-RECORD.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    sums = "".join(f"{digest(path)}  {path.name}\n" for path in sorted(output.iterdir()))
    (output / "SHA256SUMS.txt").write_text(sums, encoding="utf-8")
    Path("release-notes.md").write_text(release_notes(pin), encoding="utf-8")
    return output


def find_release(tag):
    # Draft releases are not reliably discoverable through the public tag route.
    matches = [release for release in api("releases?per_page=100") if release["tag_name"] == tag]
    require(len(matches) <= 1, "Duplicate release identity.")
    return matches[0] if matches else None


def publish(pin, output):
    tag, source = "v" + pin["version"], pin["sourceCommit"]
    release = find_release(tag)
    if release:
        require(release["draft"] and release["prerelease"] == prerelease(pin)
                and release["target_commitish"] == source,
                "An existing release cannot be replaced or repointed.")
    else:
        existing = api(f"git/ref/tags/{tag}", missing_ok=True)
        if existing is None:
            gh("api", f"repos/{REPOSITORY}/git/refs", "--method", "POST",
               "-f", f"ref=refs/tags/{tag}", "-f", f"sha={source}")
        else:
            require(existing["object"]["type"] == "commit" and existing["object"]["sha"] == source,
                    "An existing tag cannot be repointed by this publisher.")
        kind = ["--prerelease"] if prerelease(pin) else []
        gh("release", "create", tag, "--draft", *kind, "--verify-tag", "--target", source,
           "--title", f"M365 BuildStandard Tool {pin['version']}", "--notes-file", "release-notes.md")
    for path in sorted(output.iterdir()):
        release = find_release(tag)
        matches = [asset for asset in release["assets"] if asset["name"] == path.name]
        if matches:
            require(len(matches) == 1 and matches[0].get("digest") == "sha256:" + digest(path),
                    f"Existing draft asset {path.name} has different bytes; refusing overwrite.")
        else:
            gh("release", "upload", tag, str(path))
    release = find_release(tag)
    assets = {asset["name"]: asset for asset in release["assets"]}
    require(set(assets) == {path.name for path in output.iterdir()}, "Draft asset coverage mismatch.")
    for path in output.iterdir():
        require(assets[path.name].get("digest") == "sha256:" + digest(path),
                f"Uploaded asset {path.name} failed server digest verification.")
    require(api(f"git/ref/tags/{tag}")["object"]["sha"] == source, "Release tag points at a different source.")
    if prerelease(pin):
        gh("release", "edit", tag, "--draft=false", "--prerelease", "--latest=false")
    else:
        gh("release", "edit", tag, "--draft=false", "--latest")
    published = find_release(tag)
    require(not published["draft"] and published["prerelease"] == prerelease(pin),
            "Publication was not confirmed.")
    print(published["html_url"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", help="Release to publish; build/releases/<version>.json must exist.")
    parser.add_argument("--validate-pins", action="store_true",
                        help="Offline: strictly validate every release pin and exit.")
    parser.add_argument("--check-run", action="store_true")
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--github-output", help="With --check-run, append the pinned run ID for later steps.")
    args = parser.parse_args()
    if args.validate_pins:
        print("Valid release pins: " + ", ".join(validate_all_pins()))
    else:
        require(args.version is not None, "--version is required.")
        reviewed = load_pin(args.version)
        check_context()
        check_run(reviewed)
        if args.check_run:
            if args.github_output:
                with open(args.github_output, "a", encoding="utf-8") as stream:
                    stream.write(f"run={reviewed['validatedRun']}\n")
        else:
            prepared = prepare(reviewed)
            if not args.prepare_only:
                publish(reviewed, prepared)
