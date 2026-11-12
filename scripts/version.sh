#!/usr/bin/env bash
# Works out the version of the checked-out commit and prints it as KEY=VALUE lines (for
# $GITHUB_OUTPUT):
#   version  1.4.0-alpha.3          what the build stamps (-p:Version=)
#   display  1.4.0-alpha.3+4b09090  what the app shows; a release is just 1.4.0
#   file     1.4.0-alpha.3-4b09090  for package file names (GitHub renames '+' in release files)
#   numeric  1.4.0                  for installers that want plain numbers
#
#   scripts/version.sh [branch]
#
# On a tag build (GITHUB_REF_TYPE=tag) the tag is the version: v1.4.0-rc.1 gives 1.4.0-rc.1.
# Otherwise GitVersion (rules in GitVersion.yml) works it out from the branch. CI checks out a
# detached HEAD, so pass the branch name: it becomes a local branch at the same commit. GitVersion
# runs as on a developer's machine (GITHUB_ACTIONS unset), so CI and local builds agree.
set -euo pipefail

branch="${1:-}"
cd "$(dirname "$0")/.."

if [[ "${GITHUB_REF_TYPE:-}" == "tag" ]]; then
    version="${GITHUB_REF_NAME#v}"
else
    if [[ -n "$branch" && "$(git rev-parse --abbrev-ref HEAD)" != "$branch" ]]; then
        git checkout -q -B "$branch"
    fi
    version="$(env -u GITHUB_ACTIONS dotnet msbuild DeckHost.Contracts/DeckHost.Contracts.csproj -nologo -restore \
        -t:GetVersion -getProperty:GitVersion_SemVer -p:EnableMiraBoxNativeBuild=false | tail -n 1 | tr -d '\r')"
fi

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "'$version' isn't a version. Tags look like v1.4.0, v1.4.0-beta.1 or v1.4.0-rc.1." >&2
    exit 1
fi

display="$version"
file="$version"
if [[ "$version" == *-* ]]; then
    commit="$(git rev-parse HEAD | cut -c1-7)"
    display="$version+$commit"
    file="$version-$commit"
fi

echo "version=$version"
echo "display=$display"
echo "file=$file"
echo "numeric=${version%%-*}"
