# Asking Claude to fix issues

The **claude** workflow (`.github/workflows/claude.yml`) runs Claude Code on GitHub when you ask
for it, using [Anthropic's Claude Code GitHub Action](https://github.com/anthropics/claude-code-action).

## Asking

- In an issue or a pull request, write a comment that starts with **`@claude`** and says what you
  want: `@claude fix this`, `@claude why does this happen?`, `@claude review this pull request`.
- Or put the **`claude`** label on an issue: Claude works on the issue as written.

Claude reads the repository (with `CLAUDE.md` as its guide), can build DeckHost and run the tests,
pushes its work to a branch and answers in the issue with what it did, how it checked it and a link
to open the pull request. You review and merge; nothing is merged by itself. Ask follow-ups with
another `@claude` comment.

## Who can start it

Only people with **write access** to the repository: the action checks the person who wrote the
comment or added the label. On this public repository, a stranger writing `@claude` does nothing.
Claude does read what others wrote in the issue, so read an outside report before handing it over.

## Setting it up (once)

1. **Install the Claude GitHub App** on the repository: https://github.com/apps/claude. (Or, in the
   Claude Code terminal app, run `/install-github-app`, which does this and the secret.)
2. **Add one secret** (Settings → Secrets and variables → Actions):
   - `CLAUDE_CODE_OAUTH_TOKEN` if you have Claude Pro or Max: run `claude setup-token` on your
     computer and paste what it prints. Runs count toward your subscription.
   - or `ANTHROPIC_API_KEY`, an API key from console.anthropic.com. Runs are billed to that account.
3. Create the label **`claude`** (Issues → Labels) if you want the label trigger.

## Costs and limits

Each run is capped (`--max-turns 60`, 60 minutes). A run starts only when you ask, never on its own.
