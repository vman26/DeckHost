#!/usr/bin/env python3
"""Scans a release's Windows downloads with VirusTotal, so a false positive shows up at release time
and not first on a user's computer.

  virustotal.py scan <release-dir> --output results.json    upload, wait for the verdicts (needs VT_API_KEY)
  virustotal.py summary results.json                         Markdown for the job summary; a GitHub
                                                             warning per file that an engine flags
  virustotal.py selftest                                     the parts without the network

The release workflow runs it after the release exists, so VirusTotal being slow or down never holds a
release back; release-notes.py adds the verdicts (compose --virustotal results.json). What to do
about a detection: docs/maintainers/releasing.md#false-positives.

The free API allows 4 requests a minute: every request waits its turn (REQUEST_INTERVAL).
"""

import argparse
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

API = "https://www.virustotal.com/api/v3"
GUI = "https://www.virustotal.com/gui/file/"
# The downloads Windows users run (the update packages are checked by DeckHost's updater).
WINDOWS_SUFFIXES = ("-win-x64-install.exe", "-win-x64.msi", "-win-x64.zip", "-win-x64-setup.exe")
DIRECT_UPLOAD_LIMIT = 32 * 1024 * 1024  # larger files need an upload URL first
REQUEST_INTERVAL = 16.0  # seconds: 4 requests a minute
TIMEOUT = 40 * 60  # seconds for all verdicts
FLAGGED = ("malicious", "suspicious")
# Engines that gave a verdict on the file (VirusTotal's "x / y" counts these).
VERDICTS = ("malicious", "suspicious", "undetected", "harmless")


def pick_files(names):
    """The Windows downloads among the release's file names, in a stable order."""
    return sorted(name for name in names if name.startswith("DeckHost-") and name.endswith(WINDOWS_SUFFIXES))


def read_analysis(analysis):
    """(done, verdict) from a GET /analyses/<id> response: the counts and every flagging engine."""
    attributes = analysis.get("data", {}).get("attributes", {})
    if attributes.get("status") != "completed":
        return False, None
    stats = attributes.get("stats", {})
    detections = sorted(
        ({"engine": engine, "category": result.get("category"), "result": result.get("result") or ""}
         for engine, result in attributes.get("results", {}).items()
         if result.get("category") in FLAGGED),
        key=lambda detection: detection["engine"].lower())
    return True, {
        "flagged": sum(stats.get(category, 0) for category in FLAGGED),
        "engines": sum(stats.get(category, 0) for category in VERDICTS),
        "detections": detections,
    }


def summary(results):
    """(Markdown table, warnings) for the job summary."""
    lines = ["### VirusTotal", "", "| File | Verdict | Flagged by |", "|---|---|---|"]
    warnings = []
    for item in results:
        if item.get("status") != "completed":
            lines.append(f"| `{item['file']}` | not scanned ({item.get('error', item.get('status'))}) | |")
            continue
        flagged_by = ", ".join(f"{d['engine']} ({d['result'] or d['category']})" for d in item["detections"])
        lines.append(f"| `{item['file']}` | [{item['flagged']}/{item['engines']}]({item['link']}) | {flagged_by} |")
        if item["flagged"]:
            warnings.append(f"{item['file']}: flagged by {flagged_by}. If these are false positives, report them "
                            "(docs/maintainers/releasing.md#false-positives).")
    return "\n".join(lines) + "\n", warnings


class Client:
    def __init__(self, key):
        self.key = key
        self.last = 0.0

    def request(self, url, data=None, content_type=None):
        wait = self.last + REQUEST_INTERVAL - time.monotonic()
        if wait > 0:
            time.sleep(wait)
        self.last = time.monotonic()
        headers = {"x-apikey": self.key, "accept": "application/json"}
        if content_type:
            headers["content-type"] = content_type
        request = urllib.request.Request(url, data=data, headers=headers, method="POST" if data is not None else "GET")
        with urllib.request.urlopen(request, timeout=600) as response:
            return json.load(response)

    def upload(self, path):
        """The analysis id of the uploaded file."""
        boundary = uuid.uuid4().hex
        body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
                "Content-Type: application/octet-stream\r\n\r\n").encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
        url = f"{API}/files"
        if path.stat().st_size > DIRECT_UPLOAD_LIMIT:
            url = self.request(f"{API}/files/upload_url")["data"]
        return self.request(url, body, f"multipart/form-data; boundary={boundary}")["data"]["id"]


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def scan(folder, key):
    client = Client(key)
    results = []
    pending = {}
    for name in pick_files(path.name for path in folder.iterdir()):
        path = folder / name
        item = {"file": name, "sha256": sha256(path), "status": "uploading"}
        item["link"] = GUI + item["sha256"]
        results.append(item)
        try:
            pending[client.upload(path)] = item
            item["status"] = "queued"
            print(f"Uploaded {name}", file=sys.stderr)
        except (urllib.error.URLError, OSError, KeyError, ValueError) as error:
            item.update(status="error", error=str(error))
            print(f"Couldn't upload {name}: {error}", file=sys.stderr)

    deadline = time.monotonic() + TIMEOUT
    while pending and time.monotonic() < deadline:
        for analysis_id, item in list(pending.items()):
            try:
                done, verdict = read_analysis(client.request(f"{API}/analyses/{analysis_id}"))
            except (urllib.error.URLError, OSError, ValueError) as error:
                print(f"Waiting for {item['file']}: {error}", file=sys.stderr)
                continue
            if done:
                item.update(verdict, status="completed")
                del pending[analysis_id]
                print(f"{item['file']}: {item['flagged']}/{item['engines']}", file=sys.stderr)
    for item in pending.values():
        item.update(status="timeout", error="no verdict in time")
    return results


def selftest():
    assert pick_files(["DeckHost-1.2.0-win-x64-install.exe", "DeckHost-1.2.0-win-x64.msi", "SHA256SUMS.txt",
                       "DeckHost-1.2.0-full.nupkg", "DeckHost-1.2.0-linux-x64.tar.gz", "DeckHost-1.2.0-win-x64.zip"]) == [
        "DeckHost-1.2.0-win-x64-install.exe", "DeckHost-1.2.0-win-x64.msi", "DeckHost-1.2.0-win-x64.zip"]
    assert read_analysis({"data": {"attributes": {"status": "queued"}}}) == (False, None)
    done, verdict = read_analysis({"data": {"attributes": {
        "status": "completed",
        "stats": {"malicious": 1, "suspicious": 0, "undetected": 70, "harmless": 0, "type-unsupported": 5},
        "results": {"BitDefender": {"category": "malicious", "result": "Gen:Variant.Test"},
                    "ESET-NOD32": {"category": "undetected", "result": None}}}}})
    assert done and verdict == {"flagged": 1, "engines": 71,
                                "detections": [{"engine": "BitDefender", "category": "malicious", "result": "Gen:Variant.Test"}]}
    table, warnings = summary([
        {"file": "a.exe", "status": "completed", "link": GUI + "1", **verdict},
        {"file": "b.msi", "status": "completed", "link": GUI + "2", "flagged": 0, "engines": 70, "detections": []},
        {"file": "c.zip", "status": "timeout", "error": "no verdict in time"}])
    assert "[1/71](https://www.virustotal.com/gui/file/1) | BitDefender (Gen:Variant.Test)" in table
    assert "| `c.zip` | not scanned (no verdict in time) | |" in table
    assert len(warnings) == 1 and warnings[0].startswith("a.exe: flagged by BitDefender")
    print("virustotal.py: selftest passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    scan_command = commands.add_parser("scan")
    scan_command.add_argument("folder")
    scan_command.add_argument("--output", required=True)
    summary_command = commands.add_parser("summary")
    summary_command.add_argument("results")
    commands.add_parser("selftest")
    args = parser.parse_args()

    if args.command == "selftest":
        selftest()
        return 0
    if args.command == "summary":
        table, warnings = summary(json.loads(Path(args.results).read_text(encoding="utf-8")))
        sys.stdout.write(table)
        for warning in warnings:
            print(f"::warning title=VirusTotal::{warning}", file=sys.stderr)
        return 0
    key = os.environ.get("VT_API_KEY")
    if not key:
        print("VT_API_KEY isn't set; nothing scanned.", file=sys.stderr)
        return 1
    results = scan(Path(args.folder), key)
    Path(args.output).write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
