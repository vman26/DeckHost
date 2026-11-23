#!/usr/bin/env python3
"""Release notes from CHANGELOG.md.

  release-notes.py check                       the changelog's format (CI runs this on every PR)
  release-notes.py section <tag>               the changelog section for a tag
  release-notes.py compose <tag> [--pull-requests FILE] [--files DIR] [--unsigned windows,macos]
                           [--virustotal results.json]
                                               the full release notes: the section, downloads, what
                                               to do the first time on systems whose packages aren't
                                               signed, checks (with VirusTotal's verdicts from
                                               virustotal.py), then the pull request list (folded)

Which section a tag uses: the one for its exact version (`## [1.2.0-rc.1]`), else the one for the
version without its pre-release label (`## [1.2.0]`, prepared on the release branch), else, for a
pre-release only, `## [Unreleased]`. A stable release without a section of its own fails, and so
does an empty section: release notes are never left out by accident.

Everything after END_MARKER is for the release page only; DeckHost's update screen stops there.
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

END_MARKER = "<!-- deckhost:end-of-notes -->"
DOCS = "https://vman26.github.io/DeckHost/"
SYSTEMS = ("windows", "macos")
GROUPS = ("Added", "Changed", "Deprecated", "Removed", "Fixed", "Security")
VERSION = r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?"
HEADING = re.compile(rf"^## \[(Unreleased|{VERSION})\](?: - (\d{{4}}-\d{{2}}-\d{{2}}))?\s*$")


class ChangelogError(Exception):
    pass


def parse(text):
    """The sections as (name, date, body) in file order; the text before the first one is skipped."""
    sections = []
    current = None
    for line in text.splitlines():
        if line.startswith("## "):
            match = HEADING.match(line)
            if not match:
                raise ChangelogError(f"Unexpected heading: {line!r} (use '## [1.2.3] - YYYY-MM-DD' or '## [Unreleased]')")
            current = [match.group(1), match.group(2), []]
            sections.append(current)
        elif current is not None:
            if line.startswith("[") and "]: http" in line:
                continue  # the link definitions at the bottom
            current[2].append(line)
    return [(name, date, "\n".join(body).strip()) for name, date, body in sections]


def check(text):
    """A list of problems; empty when the changelog is fine."""
    try:
        sections = parse(text)
    except ChangelogError as error:
        return [str(error)]

    problems = []
    names = [name for name, _, _ in sections]
    if not names or names[0] != "Unreleased":
        problems.append("The first section must be '## [Unreleased]'.")
    if len(set(names)) != len(names):
        problems.append("A version appears more than once.")
    for name, date, body in sections:
        if name != "Unreleased" and date is None:
            problems.append(f"[{name}] has no date ('## [{name}] - YYYY-MM-DD').")
        for group in re.findall(r"^### (.+)$", body, re.MULTILINE):
            if group.strip() not in GROUPS:
                problems.append(f"[{name}] has a group '### {group}'; use {', '.join(GROUPS)}.")
    return problems


def section_for(text, tag):
    version = tag[1:] if tag.startswith("v") else tag
    if not re.fullmatch(VERSION, version):
        raise ChangelogError(f"{tag!r} isn't a version tag (v1.2.3 or v1.2.3-rc.1).")

    sections = {name: body for name, _, body in parse(text)}
    base = version.split("-", 1)[0]
    candidates = [version, base] + (["Unreleased"] if "-" in version else [])
    for name in candidates:
        if name in sections:
            if not sections[name]:
                raise ChangelogError(f"The changelog section [{name}] for {tag} is empty.")
            return sections[name]
    raise ChangelogError(
        f"No section for {tag}. Rename '## [Unreleased]' to "
        f"'## [{version if '-' not in version else base}] - <date>' before tagging.")


def package_names(version, files=()):
    """The release's package names by system: the real file names when the release files are
    given (pre-release names carry the commit, DeckHost-1.2.0-rc.1-abc1234-...), else the pattern."""
    def find(suffix):
        return next((name for name in sorted(files) if name.startswith("DeckHost-") and name.endswith(suffix)), f"DeckHost-{version}{suffix}")
    return {
        "install": find("-win-x64-install.exe"),
        "msi": find("-win-x64.msi"),
        "windows": find("-win-x64.zip"),
        "linux": find("-linux-x64.tar.gz"),
        "appimage": find("-linux-x86_64.AppImage"),
        "macos": find("-osx-arm64.zip"),
        "pkg": find("-osx-arm64.pkg"),
    }


def first_start(unsigned):
    """What to do the first time an unsigned package is opened, for the systems in `unsigned`."""
    steps = []
    if "windows" in unsigned:
        steps.append("- **Windows** says \"Windows protected your PC\": choose **More info → Run anyway**. "
                     "Unpacking the zip? Right-click it → Properties → **Unblock** first.")
    if "macos" in unsigned:
        steps.append("- **macOS** won't open it the first time: right-click DeckHost.app → **Open** (macOS 14 and "
                     "older), or open it once and then **System Settings → Privacy & Security → Open Anyway** "
                     "(macOS 15 and newer).")
    if not steps:
        return ""
    return ("### First start\n\n"
            "These packages aren't code-signed yet, so the system asks once before it opens DeckHost. "
            "Updates DeckHost installs itself don't ask again.\n\n"
            + "\n".join(steps) + "\n\n"
            f"An antivirus program blocks it? See [If your antivirus blocks DeckHost]"
            f"({DOCS}troubleshooting/#if-your-antivirus-blocks-deckhost); "
            f"more on the [download page]({DOCS}download/#first-start-and-signing).")


def virustotal_lines(results):
    """One line per scanned file: VirusTotal's verdict and the report link."""
    lines = []
    for item in results:
        if item.get("status") == "completed":
            lines.append(f"  - `{item['file']}`: {item['flagged']}/{item['engines']} engines flag it "
                         f"([report]({item['link']}))")
    if not lines:
        return ""
    return "- VirusTotal, scanned when the release was made:\n" + "\n".join(lines) + "\n"


def compose(text, tag, pull_requests="", files=(), unsigned=(), virustotal=()):
    version = tag[1:] if tag.startswith("v") else tag
    names = package_names(version, files)
    # The pkg needs a Developer ID Installer certificate; without one the release has only the zip.
    mac_installer = f" or `{names['pkg']}` (installer)" if names["pkg"] in set(files) else ""
    parts = [
        section_for(text, tag),
        "## Downloads\n\n"
        "| System | File |\n"
        "|---|---|\n"
        f"| Windows | `{names['install']}` (installs for you, no administrator rights), `{names['msi']}` (for all users) or `{names['windows']}` (no install) |\n"
        f"| Linux | `{names['appimage']}` (no install, updates itself; `chmod +x` and run) or `{names['linux']}` (run `install.sh` inside; updated by hand) |\n"
        f"| macOS (Apple silicon) | `{names['macos']}` (the app){mac_installer} |\n\n"
        "DeckHost already installed? It offers this update by itself. The other files (`releases.*.json`, "
        "`*.nupkg`, `RELEASES*`) are what its updater downloads; `*-win-x64-setup.exe` only takes older "
        "DeckHost versions to this one.",
        END_MARKER,
    ]
    if notes := first_start(unsigned):
        parts.append(notes)
    parts.append(
        "### Checking the files\n\n"
        "- Checksums: `sha256sum -c SHA256SUMS.txt --ignore-missing` (Windows: `Get-FileHash <file>`)\n"
        f"- Build provenance: `gh attestation verify <file> --repo {os.environ.get('GITHUB_REPOSITORY', 'vman26/DeckHost')}` "
        "(built by this repository's release workflow from the tagged commit, and unchanged since)\n"
        + virustotal_lines(virustotal)
        + "- For plugin authors: `DeckHost.Contracts` and `DeckHost.Templates` are on NuGet and attached here.")
    if pull_requests.strip():
        parts.append("<details>\n<summary>Every pull request in this release</summary>\n\n"
                     + pull_requests.strip() + "\n\n</details>")
    return "\n\n".join(parts) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--changelog", default=str(Path(__file__).resolve().parent.parent / "CHANGELOG.md"))
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("check")
    section = commands.add_parser("section")
    section.add_argument("tag")
    full = commands.add_parser("compose")
    full.add_argument("tag")
    full.add_argument("--pull-requests", help="a file with the generated pull request list")
    full.add_argument("--files", help="the folder with the release files, for their real names")
    full.add_argument("--unsigned", default="", help="systems whose packages aren't signed: windows,macos")
    full.add_argument("--virustotal", help="the results of virustotal.py scan")
    args = parser.parse_args()

    text = Path(args.changelog).read_text(encoding="utf-8")
    try:
        if args.command == "check":
            problems = check(text)
            for problem in problems:
                print(f"CHANGELOG.md: {problem}", file=sys.stderr)
            return 1 if problems else 0
        if args.command == "section":
            print(section_for(text, args.tag))
            return 0
        pull_requests = Path(args.pull_requests).read_text(encoding="utf-8") if args.pull_requests else ""
        files = [path.name for path in Path(args.files).iterdir()] if args.files else []
        unsigned = [system.strip() for system in args.unsigned.split(",") if system.strip()]
        if unknown := [system for system in unsigned if system not in SYSTEMS]:
            parser.error(f"--unsigned: unknown system {', '.join(unknown)} (use {', '.join(SYSTEMS)})")
        virustotal = json.loads(Path(args.virustotal).read_text(encoding="utf-8")) if args.virustotal else []
        sys.stdout.write(compose(text, args.tag, pull_requests, files, unsigned, virustotal))
        return 0
    except ChangelogError as error:
        print(f"CHANGELOG.md: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
