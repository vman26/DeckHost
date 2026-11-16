#!/usr/bin/env python3
"""Release notes from CHANGELOG.md.

  release-notes.py check                       the changelog's format (CI runs this on every PR)
  release-notes.py section <tag>               the changelog section for a tag
  release-notes.py compose <tag> [--pull-requests FILE] [--files DIR]
                                               the full release notes: the section, downloads and
                                               checks, then the pull request list (folded)

Which section a tag uses: the one for its exact version (`## [1.2.0-rc.1]`), else the one for the
version without its pre-release label (`## [1.2.0]`, prepared on the release branch), else, for a
pre-release only, `## [Unreleased]`. A stable release without a section of its own fails, and so
does an empty section: release notes are never left out by accident.

Everything after END_MARKER is for the release page only; DeckHost's update screen stops there.
"""

import argparse
import os
import re
import sys
from pathlib import Path

END_MARKER = "<!-- deckhost:end-of-notes -->"
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
        "setup": find("-win-x64-setup.exe"),
        "windows": find("-win-x64.zip"),
        "linux": find("-linux-x64.tar.gz"),
        "macos": find("-osx-arm64.zip"),
    }


def compose(text, tag, pull_requests="", files=()):
    version = tag[1:] if tag.startswith("v") else tag
    names = package_names(version, files)
    parts = [
        section_for(text, tag),
        "## Downloads\n\n"
        "| System | File |\n"
        "|---|---|\n"
        f"| Windows | `{names['setup']}` (installer) or `{names['windows']}` |\n"
        f"| Linux | `{names['linux']}` (run `install.sh` inside) |\n"
        f"| macOS (Apple silicon) | `{names['macos']}` |\n\n"
        "DeckHost already installed? It offers this update by itself.",
        END_MARKER,
        "### Checking the files\n\n"
        "- Checksums: `sha256sum -c SHA256SUMS.txt --ignore-missing`\n"
        f"- Build provenance: `gh attestation verify <file> --repo {os.environ.get('GITHUB_REPOSITORY', 'vman26/DeckHost')}`\n"
        "- For plugin authors: `DeckHost.Contracts` and `DeckHost.Templates` are on NuGet and attached here.",
    ]
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
        sys.stdout.write(compose(text, args.tag, pull_requests, files))
        return 0
    except ChangelogError as error:
        print(f"CHANGELOG.md: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
