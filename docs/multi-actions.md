# Multi Action and Key Logic

These turn one key into several actions. For conditions, loops and branches, use a [workflow](workflows.md).

## Multi Action

Runs its steps in order. Drop actions onto the steps in its editor (or onto the key), drag ⠿ to reorder, and give a step a wait: the rest runs after it, and the device keeps working meanwhile.

## Multi Action Switch

Two sets of steps: the first press runs the first set, the next press the second, and so on. Made for on/off pairs such as mute and unmute. Each set can show its own title.

## Key Logic

Up to three things on one key: a **press**, a **double press** and **holding** it. A press waits a moment to see whether a second one follows, but only when there are double-press steps.

## Turning it into a workflow

**Turn into a workflow…** in the editor makes a workflow that does exactly the same, which you can then extend.
