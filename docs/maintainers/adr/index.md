# Decisions

Architecture decision records: why DeckHost is built the way it is. Each one says what was decided, why, and what it costs. A new decision that changes an old one gets a new record and marks the old one *Replaced*.

| | Decision | Status |
|---|---|---|
| [0001](0001-dependency-rule.md) | Contracts ← Core ← App → device plugins | Accepted |
| [0002](0002-single-worker.md) | One worker owns the host's state | Accepted |
| [0003](0003-plugin-api.md) | Plugins compile against Contracts; the API only grows | Accepted |
| [0004](0004-immutable-bindings.md) | Bindings and profiles are immutable records | Accepted |
| [0005](0005-profile-migrations.md) | Profiles carry a version and migrate forward | Accepted |
| [0006](0006-workflows.md) | Workflows are a graph run on the host worker | Accepted |
| [0007](0007-in-process-plugins.md) | DeckHost plugins run in the app's process | Accepted, revisit |

Format: *Context* (what forced a choice), *Decision*, *Consequences* (good and bad). Keep them short.
