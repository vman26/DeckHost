#!/usr/bin/env python3
"""Links the issues of the public releases repository to the private source repository.

Users report problems in the public repository (DeckHost's "Report a problem" opens an issue
there). This script, run by .github/workflows/sync-issues.yml:

* opens a copy of each new public issue in the private repository (label ``public-report``) with a
  link back, and labels the public one ``tracked``;
* copies new comments on the public issue to its copy;
* posts a comment on the copy that starts with ``/public`` to the public issue (the rest of the
  comment), so maintainers answer from the private repository;
* when the copy is closed as completed, thanks the reporter and closes the public issue; when the
  public issue is closed, says so on the copy.

Nothing from the private repository reaches the public one except ``/public`` comments and the
closing note. Everything the script writes carries a hidden marker, so it never copies its own
comments back and can run any number of times.

Environment: PUBLIC_REPO, PRIVATE_REPO (owner/name), PUBLIC_TOKEN (Issues read and write on the
public repository), PRIVATE_TOKEN (Issues read and write here), LOOKBACK_DAYS (default 14),
DRY_RUN=1 to only print what it would do.
"""

from __future__ import annotations

import datetime as dt
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
PRIVATE_LABEL = "public-report"
PUBLIC_LABEL = "tracked"
SYNC_MARK = "<!-- deckhost-sync -->"
CLOSED_MARK = "<!-- deckhost-public-closed -->"
ISSUE_MARK = re.compile(r"<!-- deckhost-public-issue: (\S+)#(\d+) -->")
COMMENT_MARK = re.compile(r"<!-- deckhost-public-comment: (\d+) -->")
POSTED_MARK = "<!-- deckhost-posted -->"
PUBLIC_COMMAND = re.compile(r"\A\s*/public\b[ \t]*\r?\n?", re.IGNORECASE)


class GitHub:
    def __init__(self, token: str, dry_run: bool) -> None:
        self.token = token
        self.dry_run = dry_run

    def request(self, method: str, path: str, body: dict | None = None, query: dict | None = None):
        url = API + path + ("?" + urllib.parse.urlencode(query) if query else "")
        if self.dry_run and method != "GET":
            print(f"  (dry run) {method} {path} {json.dumps(body)[:200] if body else ''}")
            return {}
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(url, data=data, method=method, headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {self.token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "deckhost-issue-sync",
        })
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                text = response.read().decode()
                return json.loads(text) if text else {}
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")[:300]
            raise RuntimeError(f"{method} {path}: HTTP {error.code} {detail}") from error

    def pages(self, path: str, query: dict | None = None, limit: int = 1000) -> list:
        items: list = []
        page = 1
        while len(items) < limit:
            batch = self.request("GET", path, query={**(query or {}), "per_page": 100, "page": page})
            if not batch:
                break
            items.extend(batch)
            if len(batch) < 100:
                break
            page += 1
        return items


def quote(text: str | None) -> str:
    """The text as a Markdown quote (an empty body reads as such)."""
    text = (text or "").strip() or "_(no text)_"
    return "\n".join("> " + line if line else ">" for line in text.splitlines())


def mirror_body(public_repo: str, issue: dict) -> str:
    return (
        f"Reported in the public repository: {issue['html_url']} by **{issue['user']['login']}**.\n"
        "Comment `/public <text>` here to answer there; closing this as completed closes it there too.\n\n"
        f"{quote(issue.get('body'))}\n\n"
        f"<!-- deckhost-public-issue: {public_repo}#{issue['number']} -->"
    )


def mirror_title(issue: dict) -> str:
    return f"[public #{issue['number']}] {issue['title']}"[:250]


def mirrored_comment_ids(comments: list) -> set[int]:
    return {int(match.group(1)) for comment in comments for match in COMMENT_MARK.finditer(comment.get("body") or "")}


def public_command(body: str | None) -> str | None:
    """The text of a ``/public`` comment, or None when the comment isn't one (or was posted)."""
    if not body or POSTED_MARK in body:
        return None
    match = PUBLIC_COMMAND.match(body)
    if not match:
        return None
    text = body[match.end():].strip()
    return text or None


def sync(public: GitHub, private: GitHub, public_repo: str, private_repo: str, since: dt.datetime) -> None:
    mirrors: dict[int, dict] = {}
    for issue in private.pages(f"/repos/{private_repo}/issues", {"labels": PRIVATE_LABEL, "state": "all"}):
        match = ISSUE_MARK.search(issue.get("body") or "")
        if match and match.group(1).lower() == public_repo.lower():
            mirrors.setdefault(int(match.group(2)), issue)

    stamp = since.strftime("%Y-%m-%dT%H:%M:%SZ")
    reports = [issue for issue in public.pages(f"/repos/{public_repo}/issues", {"state": "all", "since": stamp})
               if "pull_request" not in issue]

    for issue in reports:
        number = issue["number"]
        mirror = mirrors.get(number)
        if mirror is None:
            if issue["state"] != "open":
                continue
            print(f"New public issue #{number}: {issue['title']}")
            mirror = private.request("POST", f"/repos/{private_repo}/issues", {
                "title": mirror_title(issue), "body": mirror_body(public_repo, issue), "labels": [PRIVATE_LABEL]})
            try:
                public.request("POST", f"/repos/{public_repo}/issues/{number}/labels", {"labels": [PUBLIC_LABEL]})
            except RuntimeError as error:
                print(f"  couldn't label it: {error}")
            if not mirror:
                continue
            mirrors[number] = mirror

        copy = mirror["number"]
        copies = private.pages(f"/repos/{private_repo}/issues/{copy}/comments")
        done = mirrored_comment_ids(copies)
        for comment in public.pages(f"/repos/{public_repo}/issues/{number}/comments", {"since": stamp}):
            if comment["id"] in done or SYNC_MARK in (comment.get("body") or ""):
                continue
            print(f"Comment on public #{number} → #{copy}")
            private.request("POST", f"/repos/{private_repo}/issues/{copy}/comments", {"body": (
                f"**{comment['user']['login']}** [commented]({comment['html_url']}) in the public issue:\n\n"
                f"{quote(comment.get('body'))}\n\n<!-- deckhost-public-comment: {comment['id']} -->")})

        if issue["state"] == "closed" and mirror.get("state") == "open" and not any(CLOSED_MARK in (c.get("body") or "") for c in copies):
            print(f"Public #{number} was closed → note on #{copy}")
            private.request("POST", f"/repos/{private_repo}/issues/{copy}/comments", {
                "body": f"The public issue was closed ({issue.get('state_reason') or 'closed'}).\n\n{CLOSED_MARK}"})

    # Answers and fixes from here to there.
    for number, mirror in mirrors.items():
        updated = dt.datetime.strptime(mirror["updated_at"], "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=dt.timezone.utc)
        if updated < since:
            continue
        copy = mirror["number"]
        for comment in private.pages(f"/repos/{private_repo}/issues/{copy}/comments", {"since": stamp}):
            text = public_command(comment.get("body"))
            if text is None:
                continue
            print(f"/public on #{copy} → public #{number}")
            public.request("POST", f"/repos/{public_repo}/issues/{number}/comments", {"body": f"{text}\n\n{SYNC_MARK}"})
            private.request("PATCH", f"/repos/{private_repo}/issues/comments/{comment['id']}",
                            {"body": f"{comment['body']}\n\n_Posted to the public issue._ {POSTED_MARK}"})

        if mirror.get("state") == "closed" and mirror.get("state_reason") == "completed":
            original = public.request("GET", f"/repos/{public_repo}/issues/{number}")
            if original.get("state") == "open":
                print(f"#{copy} was fixed → closing public #{number}")
                public.request("POST", f"/repos/{public_repo}/issues/{number}/comments", {"body": (
                    "Thanks for the report: this is fixed, and the fix comes with the next release "
                    f"(DeckHost tells you when it's out).\n\n{SYNC_MARK}")})
                public.request("PATCH", f"/repos/{public_repo}/issues/{number}", {"state": "closed", "state_reason": "completed"})


def main() -> int:
    public_repo = os.environ.get("PUBLIC_REPO", "")
    private_repo = os.environ.get("PRIVATE_REPO", "")
    public_token = os.environ.get("PUBLIC_TOKEN", "")
    private_token = os.environ.get("PRIVATE_TOKEN", "")
    if not (public_repo and private_repo and public_token and private_token):
        print("Needs PUBLIC_REPO, PRIVATE_REPO, PUBLIC_TOKEN and PRIVATE_TOKEN.", file=sys.stderr)
        return 2
    if public_repo.lower() == private_repo.lower():
        print("The public and private repositories are the same: nothing to link.")
        return 0

    dry_run = os.environ.get("DRY_RUN") == "1"
    days = int(os.environ.get("LOOKBACK_DAYS") or "14")
    since = dt.datetime.now(dt.timezone.utc) - dt.timedelta(days=days)
    sync(GitHub(public_token, dry_run), GitHub(private_token, dry_run), public_repo, private_repo, since)
    return 0


if __name__ == "__main__":
    sys.exit(main())
