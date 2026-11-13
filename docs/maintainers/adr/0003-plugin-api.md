# 0003: Plugins compile against Contracts; the API only grows

**Status:** Accepted

## Context
Installed plugins are binaries built against one version of `DeckHost.Contracts`. When DeckHost updates, they must keep loading.

## Decision
- `DeckHost.Contracts` is always loaded from the host, never from a plugin package.
- The API only grows: new interface members get a default body, new record properties are `init` properties (so constructors stay the same), enums get new values at the end.
- A change that breaks this bumps `PluginManifest.CurrentHostApiVersion`; plugins built for another version are refused with a clear message.
- `ArchitectureTests.The_plugin_API_only_grows_unless_the_host_API_version_is_bumped` compares the public surface with `DeckHost.Tests/ContractsApi.txt`. Additions only need the file updated (`UPDATE_API_BASELINE=1 dotnet test …`); removals, changes and members without a body on existing interfaces need the version bump.

## Consequences
- Plugins keep working across updates without rebuilding.
- Contracts collects some awkward shapes (default members that do nothing, long records with init properties).
- The baseline file shows every API change in review.
