# 0005: Profiles carry a version and migrate forward

**Status:** Accepted

## Context
`profiles.json` outlives DeckHost versions. Renaming a setting, moving data or changing a default must not lose what users built.

## Decision
- The document carries `version`. `ProfileMigrations` holds one step per version (`n → n+1`), each a function on the JSON tree, so a step never depends on today's C# types.
- On load, an older file is backed up (`backups/profiles-before-v<n>.json`), migrated step by step, then read.
- A file from a newer DeckHost is not migrated: it is read as well as possible, the app says so, and before the first save over it the file is copied to `backups/profiles-from-v<n>.json`, so going back to an older version doesn't destroy the newer file.
- Additive changes (a new optional property) need no step; only renames, moves and meaning changes do.

## Consequences
- Each format change ships with a step and a test with a sample of the old format.
- Old steps stay forever; they are small.
