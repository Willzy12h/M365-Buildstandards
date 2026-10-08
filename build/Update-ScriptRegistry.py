#!/usr/bin/env python3
"""Re-pin the script library after a reviewed change.

Recomputes each manifest's scriptSha256 and rewrites scripts/registry.json with the SHA-256 of every manifest and
script. Run it after editing a library script or manifest, review the diff, and commit both. With --check it changes
nothing and fails if any pin is stale, which is what CI runs.
"""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "scripts"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    check = "--check" in sys.argv[1:]
    stale = []
    entries = []
    for manifest_path in sorted(ROOT.glob("*/*.json")):
        relative = manifest_path.relative_to(ROOT).as_posix()
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        script_path = ROOT / manifest["scriptPath"]
        script_sha = sha256(script_path)
        if manifest.get("scriptSha256") != script_sha:
            stale.append(relative + " scriptSha256")
            if not check:
                manifest["scriptSha256"] = script_sha
                manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
        entries.append({
            "id": manifest["id"],
            "manifest": relative,
            "manifestSha256": sha256(manifest_path),
            "scriptSha256": script_sha,
        })
    registry = json.dumps({"schemaVersion": 1, "scripts": entries}, indent=2) + "\n"
    registry_path = ROOT / "registry.json"
    if not registry_path.exists() or registry_path.read_text(encoding="utf-8") != registry:
        stale.append("registry.json")
        if not check:
            registry_path.write_text(registry, encoding="utf-8", newline="\n")
    if check and stale:
        print("Stale script library pins: " + ", ".join(stale) + ". Run python3 build/Update-ScriptRegistry.py and review the diff.")
        return 1
    print(("Checked " if check else "Pinned ") + str(len(entries)) + " library item(s)." + ("" if check or not stale else " Updated: " + ", ".join(stale)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
