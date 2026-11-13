# 0006: Workflows are a graph run on the host worker

**Status:** Accepted

## Context
Multi Action, Multi Action Switch and Key Logic each cover one pattern of "do several things". Users asked for conditions, loops, waits and variables, and to see what happens.

## Decision
A workflow is a directed graph without loops (`Workflow`: nodes, links from an output port to a node) stored on the binding. Triggers (pressed, tapped, held, turned, …) start runs; the engine runs on the host worker with a work queue per run, schedules waits instead of blocking, fans out when a port has several links, and runs actions as press + release. Every step is traced for the editor. Loops are only Repeat nodes; links that would close a cycle are refused.

## Consequences
- Older step-based actions convert to workflows without changing behavior; they stay as simpler options.
- No unbounded loops: a run can't hang the host (and `MaxNodesPerRun` is a safety net).
- A profile file with a workflow can't be read by versions before workflows.
