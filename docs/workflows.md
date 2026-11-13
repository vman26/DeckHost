# Workflows

A workflow is what a control does, drawn as nodes linked on a canvas. Drop the **Workflow** action on a control (or use **Turn into a workflow…** in its editor), then **Open workflow editor**.

## How a run goes

A run starts at a **When** node and follows the links from left to right. A node's dots on the right are its outputs; drag from one onto another node to link them. An output with several links runs them side by side, and the run ends when every branch is done.

## Nodes

| Node | What it does |
|---|---|
| **When** | Starts the workflow: the control is pressed, released, tapped, double pressed, held down, turned left or right, or swiped left or right. A workflow can have several. |
| **Action** | Runs an action, with its own settings. Drop an action from the list on the left onto the canvas. |
| **Wait** | Waits, then continues. The device keeps working. |
| **If** | Continues at **Yes** or **No**: a variable (equal to, greater than, containing, empty, …), the app in front (part of its name), or the time of day (also past midnight, 22:00–06:00). |
| **Toggle** | Takes **1st** and **2nd** in turn: on, then off. |
| **Repeat** | Runs **Each time** a number of times, one round after the other, then **Done**. |
| **Set variable** | Sets, adds to, subtracts from, flips (true/false) or clears a variable. |
| **Stop** | Ends the run, including branches that are still waiting. |

Variables are shared by all workflows (names ignore case). The **Show Variable** action shows one on a key. They start empty when DeckHost starts.

## While a run is going

When the same input comes again before its run is done (a wait), the workflow can **start another run** next to it (the default), **ignore** the input, **stop the run and start again**, or **stop the run**, which makes the key a stop button.

## The editor

- Click a node to set it up on the right; click a link to select it. **Delete** removes, **Ctrl+D** duplicates, **Ctrl+Z / Ctrl+Y** undo and redo.
- **Arrange** lays the nodes out from left to right; **Fit** shows them all. Drag the background to pan, and use Ctrl + the mouse wheel to zoom.
- **Run** starts the workflow as if the control was used; **Stop** stops every run. Each step lights up on the canvas and appears in the run log.
- Links can't form a loop (use Repeat) and can't lead into a When node.
- Every change reaches the device right away; **Revert** puts back the workflow as it was when you opened the editor.
