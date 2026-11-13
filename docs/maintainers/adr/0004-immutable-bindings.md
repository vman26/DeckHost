# 0004: Bindings and profiles are immutable records

**Status:** Accepted

## Context
The UI edits what a control does while the host worker reads it. Shared mutable objects would need locks and make undo hard.

## Decision
`ControlBinding`, `DeviceProfile`, `Workflow` and the records in them are immutable. Every edit builds a new value and goes through one place (`MainViewModel.ApplyBinding` / `ApplyWorkflow`), which hands the host a new snapshot. View models only wrap them.

## Consequences
- The host never sees half an edit; a snapshot can be kept for revert and undo for free.
- Records with lists compare by reference unless they override `Equals` (several do, list-aware).
- Edits allocate; profiles are small, so this hasn't mattered.
