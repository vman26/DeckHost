#!/usr/bin/env bash
# How often each DeckHost release was downloaded, per file and in total (GitHub counts every
# download of a release file; update checks aren't counted, installs through the updater are).
#
#   scripts/downloads.sh [owner/repo]     default: vman26/DeckHost
#
set -euo pipefail

repo="${1:-}"
if [[ -z "$repo" ]]; then
    repo="vman26/DeckHost"
fi

auth=()
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    auth=(-H "Authorization: Bearer $GITHUB_TOKEN")
fi

json="$(curl -fsSL "${auth[@]}" -H "Accept: application/vnd.github+json" "https://api.github.com/repos/$repo/releases?per_page=100")" || {
    echo "Couldn't read the releases of $repo (does it exist, and is it public or is GITHUB_TOKEN set?)." >&2
    exit 1
}

RELEASES_JSON="$json" python3 - "$repo" <<'PY'
import json, os, sys
repo = sys.argv[1]
releases = json.loads(os.environ["RELEASES_JSON"])
total = 0
print(f"Downloads of {repo}")
for release in releases:
    count = sum(asset.get("download_count", 0) for asset in release.get("assets", []))
    total += count
    print(f"\n{release.get('tag_name')}  {count:>8}  {'(pre-release)' if release.get('prerelease') else ''}")
    for asset in sorted(release.get("assets", []), key=lambda a: -a.get("download_count", 0)):
        if asset["name"] != "SHA256SUMS.txt":
            print(f"    {asset['download_count']:>8}  {asset['name']}")
print(f"\nTotal: {total}")
PY
