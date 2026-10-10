#!/usr/bin/env python3
"""Run offline report correctness tests and retain comparable, source-labelled measurements."""
import argparse
import json
import platform
from pathlib import Path
import statistics
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
PREFIX = "BDIT_SCALE "
EXPECTED = {(rows, stage) for rows in (100, 1000, 5000) for stage in ("seal", "read", "html", "csv", "xlsx")}


def command(*args):
    return subprocess.check_output(args, cwd=REPO, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New local JSON result file; existing files are refused.")
    parser.add_argument("--repeats", type=int, default=3, choices=range(1, 6), help="Separate test processes, 1–5 (default 3).")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("The output already exists. Choose a new file to preserve the earlier measurement.")
    metadata = {
        "sourceCommit": command("git", "rev-parse", "HEAD"),
        "workingTreeDirty": bool(command("git", "status", "--porcelain")),
        "platform": platform.platform(),
        "architecture": platform.machine(),
        "pythonVersion": platform.python_version(),
        "dotnetSdk": command("dotnet", "--version"),
        "scope": "Synthetic historical Intune report only; no authentication, network reads or tenant evidence.",
        "limits": "Existing 5,000-row section cap; over-cap refusal checked, no cap changes.",
        "measurement": "Warm-up of every path per test; synchronous current-thread allocations, not peak memory; fixture generation and assertions excluded. Process repeats are not isolated machine benchmarks.",
    }
    samples = []
    with tempfile.TemporaryDirectory(prefix="bdit-report-scale-") as temporary:
        for repeat in range(args.repeats):
            results = Path(temporary) / str(repeat)
            invocation = ["dotnet", "test", "tests/BDIT.TenantToolkit.Tests", "-c", "Release", "--no-restore",
                          "-p:EnableWindowsTargeting=true", "-warnaserror", "--filter", "FullyQualifiedName~ReportScaleValidationTests",
                          "--logger", "trx;LogFileName=scale.trx", "--results-directory", str(results)]
            completed = subprocess.run(invocation, cwd=REPO)
            if completed.returncode:
                return completed.returncode
            root = ET.parse(results / "scale.trx").getroot()
            ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
            tests = root.findall(".//t:UnitTestResult", ns)
            if len(tests) != 4 or any(test.get("outcome") != "Passed" for test in tests):
                raise ValueError("Expected all four correctness tests to pass; zero, skipped or incomplete runs are refused.")
            current = []
            for test in tests:
                for line in (test.findtext("t:Output/t:StdOut", default="", namespaces=ns)).splitlines():
                    if line.startswith(PREFIX):
                        sample = json.loads(line[len(PREFIX):])
                        if (sample["rows"], sample["stage"]) not in EXPECTED:
                            raise ValueError("Unexpected measurement stage or row count.")
                        for field in ("elapsedMilliseconds", "allocatedBytesOnCurrentThread", "outputBytes"):
                            if not isinstance(sample[field], (int, float)) or sample[field] < 0:
                                raise ValueError("Invalid measurement value.")
                        sample["repeat"] = repeat + 1
                        current.append(sample)
            if len(current) != len(EXPECTED) or {(s["rows"], s["stage"]) for s in current} != EXPECTED:
                raise ValueError("The successful run did not contain every distinct measurement.")
            samples.extend(current)
    medians = []
    for rows, stage in sorted(EXPECTED):
        group = [s for s in samples if (s["rows"], s["stage"]) == (rows, stage)]
        medians.append({"rows": rows, "stage": stage, **{
            field: statistics.median(s[field] for s in group)
            for field in ("elapsedMilliseconds", "allocatedBytesOnCurrentThread", "outputBytes")}})
    result = {"metadata": metadata, "repeats": args.repeats, "correctnessTestsPerRepeat": 4, "samples": samples, "medians": medians}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation also protects a file created after the initial existence check.
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2, allow_nan=False)
        stream.write("\n")
    print("Saved verified offline scale measurements to", args.output)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, ET.ParseError, subprocess.CalledProcessError) as error:
        print("Scale measurement refused:", error, file=sys.stderr)
        sys.exit(1)
