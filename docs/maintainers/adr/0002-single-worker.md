# 0002: One worker owns the host's state

**Status:** Accepted

## Context
Input arrives on SDK and HID threads, timers fire on pool threads, the UI calls in from its thread, and actions ask for redraws from background tasks. Locking all of the host's state (profiles, variables, pushed frames, lights, running workflows) would be slow and easy to get wrong.

## Decision
`DeviceHostService` puts every request onto one `Channel`; a single worker task takes them in order and is the only code that touches the host's state. Waiting (delays, Multi Action waits, workflow waits) never blocks the worker: it schedules work back onto the queue (`ActionRouter.Scheduler`). After each batch the worker renders the dirty devices and pushes only frames that changed.

## Consequences
- Almost no locks; ordering is the queue's order, so input is never handled out of turn.
- `Execute` and `Render` must be fast: slow work goes to a background task that asks for a render.
- Events are raised on the worker; UI code must post to the UI thread.
- Tests wait with `WhenIdleAsync`, not with delays.
