"""Promote the explicitly approved Preview.18 CI artifact, without rebuilding it.

Pins deliberately require a reviewed source change for a different release. This
script runs only through the manual publish-preview18.yml workflow, gated by the release environment. No tenant
credentials, branch merges, main/integration writes or live operations occur.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

REPOSITORY = "Willzy12h/M365-Buildstandards"
SOURCE = "10808de054a195c73ae742a0ae2f6240a318e421"
RUN = "37515288607"
VERSION = "1.1.0-preview.18"
STANDARD = "2026.09.30"
ZIP_SHA256 = "9155fad428fe6c93ae8b72a6695a8ee5622f533348165d00167bcd577f3495cc"
TAG = "v" + VERSION
PACKAGE = f"M365-BuildStandard-Tool-{VERSION}-win-x64.zip"


def require(condition, message):
    if not condition:
        raise ValueError(message)


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


def check_run():
    require(os.environ.get("GITHUB_REPOSITORY", REPOSITORY) == REPOSITORY,
            "Publication is restricted to the original repository.")
    run = api(f"actions/runs/{RUN}")
    require(run["head_sha"] == SOURCE and run["head_branch"] == "astra/release-2026-09-12"
            and run["head_repository"]["full_name"] == REPOSITORY
            and run["event"] == "push" and run["path"] == ".github/workflows/build.yml"
            and run["status"] == "completed" and run["conclusion"] == "success",
            "The pinned source run is not a successful original-repository build.")
    jobs = api(f"actions/runs/{RUN}/jobs?per_page=100")["jobs"]
    for name in ("build", "standard", "secrets"):
        matches = [job for job in jobs if job["name"] == name]
        require(len(matches) == 1 and matches[0]["conclusion"] == "success",
                f"The source run lacks successful {name} validation.")
    artifacts = api(f"actions/runs/{RUN}/artifacts?per_page=100")["artifacts"]
    for name in ("portable-windows-review", "synthetic-ui-review", "standard-definition-exports"):
        matches = [artifact for artifact in artifacts if artifact["name"] == name]
        require(len(matches) == 1 and not matches[0]["expired"],
                f"The pinned {name} artifact is missing or expired.")
    print(f"Verified successful source run {RUN} at {SOURCE}.")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_package(portable):
    package = portable / PACKAGE
    require(package.is_file() and digest(package) == ZIP_SHA256,
            "Portable ZIP does not match the independently recorded tested SHA-256.")
    sidecar = portable / (PACKAGE + ".sha256")
    require(sidecar.read_text(encoding="utf-8").strip() == f"{ZIP_SHA256}  {PACKAGE}",
            "Portable ZIP sidecar does not identify the tested bytes.")
    result = json.loads((portable / "portable-verification.json").read_text(encoding="utf-8"))
    require(all(result.get(key) == value for key, value in {
        "sourceCommit": SOURCE, "version": VERSION, "standard": STANDARD,
        "zipSha256": ZIP_SHA256, "extractedFiles": 297,
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
        require(metadata.get("sourceCommit") == SOURCE and metadata.get("version") == VERSION
                and metadata.get("sourceTreeDirty") is False
                and metadata.get("defaultStandard") == STANDARD
                and metadata.get("runtime") == "win-x64" and metadata.get("selfContained") is True
                and metadata.get("dotnetSdk") == "10.0.401"
                and str(metadata.get("dotnetRuntime", "")).startswith("10."),
                "Package metadata does not identify a clean self-contained .NET 10 source build.")
        catalogue = read(f"standards/{STANDARD}.json", 4 * 1024 * 1024)
    return metadata, hashlib.sha256(catalogue).hexdigest()


def prepare():
    inputs = Path("release-input")
    output = Path("release-output")
    require(not output.exists(), "Release staging already exists; use a fresh job.")
    metadata, catalogue_digest = validate_package(inputs / "portable")
    output.mkdir()
    for name in (PACKAGE, PACKAGE + ".sha256", "portable-verification.json"):
        shutil.copyfile(inputs / "portable" / name, output / name)
    for folder, name in (("ui", "native-ui-review.zip"), ("standards", "standard-definition-exports.zip")):
        root = inputs / folder
        files = sorted(path for path in root.rglob("*") if path.is_file())
        require(files and not any(path.is_symlink() for path in root.rglob("*")),
                f"Missing or linked {folder} review assets.")
        with zipfile.ZipFile(output / name, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in files:
                archive.write(path, path.relative_to(root).as_posix())
    shutil.copyfile("docs/integration/CLAUDE-PREVIEW18-REVIEW-PROMPT.md", output / "CLAUDE-REVIEW-PROMPT.md")
    record = {
        "product": "M365 BuildStandard Tool", "version": VERSION, "prerelease": True,
        "sourceCommit": SOURCE, "validatedRun": RUN, "package": PACKAGE,
        "packageBytes": (output / PACKAGE).stat().st_size, "packageSha256": ZIP_SHA256,
        "standard": STANDARD, "catalogueSha256": catalogue_digest,
        "dotnetSdk": metadata["dotnetSdk"], "dotnetRuntime": metadata["dotnetRuntime"],
        "publisherCommit": os.environ.get("GITHUB_SHA", "local-prepare-only"),
        "publisherRun": os.environ.get("GITHUB_RUN_ID", "local-prepare-only"),
        "approval": "User explicitly requested publication on 6 October 2026.",
        "distribution": "Unsigned internal review prerelease; not a GA or live acceptance claim.",
        "pending": ["PR20 contract merge and persistent workflow/legacy-backfill implementation",
                    "Claude review", "Human accessibility/newcomer acceptance",
                    "Separately authorised tenant/service/device acceptance", "Business support ownership"],
        "tenantOperationsPerformed": False,
    }
    (output / "RELEASE-RECORD.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    sums = "".join(f"{digest(path)}  {path.name}\n" for path in sorted(output.iterdir()))
    (output / "SHA256SUMS.txt").write_text(sums, encoding="utf-8")
    notes = f"""Preview.18 is an **unsigned internal review prerelease**, published at the user's request. It is not a completed A–G/GA release or live tenant acceptance.

Download `{PACKAGE}`, compare its SHA-256 with the value below, extract the complete ZIP and run `Start.cmd`. The .NET 10 runtime is included; engineers do not need the SDK. Preserve existing workspace evidence and use `docs/WORKSPACE-CONTINUITY.md` when upgrading. Follow organisational application-control policy.

This preview adds integrated all-control/default/settings HTML, exact reusable JSON and Markdown exports; shared read-only desktop/CLI assessment with strict Exchange evidence; previewed technical support export; raw-record backup and separate verified restore; current operator/incident/servicing guides and resolved dependency/licence inventory. Historical catalogues and deployment safeguards remain intact.

Source: `{SOURCE}`. Standard: **{STANDARD}**, 93 controls / 61 candidate recipes. [Validated Windows run](https://github.com/{REPOSITORY}/actions/runs/{RUN}): **902 Engine + 112 App tests**, **42 native layouts / 78 commands**, zero binding issues/tenant calls, PowerShell and inventory negative checks, and fresh 297-file package startup/shutdown and right-click Copy passed.

**Application ZIP SHA-256: `{ZIP_SHA256}`**

Assets include the original ZIP/sidecar/extraction evidence, native .NET 10 screenshots, catalogue-only definition exports, exact release record and a Claude review prompt. Supplementary review archives are packaging of the original CI output; the application ZIP is unchanged.

Persistent jobs, version-aware observations, lineage and legacy dispositions/cutover remain pending [PR #20](https://github.com/{REPOSITORY}/pull/20)'s required human merge and implementation. [PR #19](https://github.com/{REPOSITORY}/pull/19) remains the active source claim. No branches were merged and no live tenant authentication, consent or writes were performed. Live acceptance and supported-production promotion require their separately approved evidence and decisions.
"""
    Path("release-notes.md").write_text(notes, encoding="utf-8")
    return output


def find_release():
    # Draft releases are not reliably discoverable through the public tag route.
    matches = [release for release in api("releases?per_page=100") if release["tag_name"] == TAG]
    require(len(matches) <= 1, "Duplicate release identity.")
    return matches[0] if matches else None


def publish(output):
    release = find_release()
    if release:
        require(release["draft"] and release["prerelease"] and release["target_commitish"] == SOURCE,
                "An existing release cannot be replaced or repointed.")
    else:
        tag = api(f"git/ref/tags/{TAG}", missing_ok=True)
        if tag is None:
            gh("api", f"repos/{REPOSITORY}/git/refs", "--method", "POST",
               "-f", f"ref=refs/tags/{TAG}", "-f", f"sha={SOURCE}")
        else:
            require(tag["object"]["type"] == "commit" and tag["object"]["sha"] == SOURCE,
                    "An existing tag cannot be repointed by this publisher.")
        gh("release", "create", TAG, "--draft", "--prerelease", "--verify-tag", "--target", SOURCE,
           "--title", f"M365 BuildStandard Tool {VERSION}", "--notes-file", "release-notes.md")
    for path in sorted(output.iterdir()):
        release = find_release()
        matches = [asset for asset in release["assets"] if asset["name"] == path.name]
        if matches:
            require(len(matches) == 1 and matches[0].get("digest") == "sha256:" + digest(path),
                    f"Existing draft asset {path.name} has different bytes; refusing overwrite.")
        else:
            gh("release", "upload", TAG, str(path))
    release = find_release()
    assets = {asset["name"]: asset for asset in release["assets"]}
    require(set(assets) == {path.name for path in output.iterdir()}, "Draft asset coverage mismatch.")
    for path in output.iterdir():
        require(assets[path.name].get("digest") == "sha256:" + digest(path),
                f"Uploaded asset {path.name} failed server digest verification.")
    require(api(f"git/ref/tags/{TAG}")["object"]["sha"] == SOURCE, "Release tag points at a different source.")
    gh("release", "edit", TAG, "--draft=false", "--prerelease", "--latest=false")
    published = find_release()
    require(not published["draft"] and published["prerelease"], "Prerelease publication was not confirmed.")
    print(published["html_url"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-run", action="store_true")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    check_run()
    if not args.check_run:
        prepared = prepare()
        if not args.prepare_only:
            publish(prepared)
