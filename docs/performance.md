# Performance

DeckHost runs all day next to everything else on your computer, so it should never be the reason
the computer feels slow. Every pull request and every release is measured on Windows, Linux and
macOS, and a change that makes DeckHost heavier than its budget doesn't get in.

## The numbers

--8<-- "benchmarks/performance-data.md"

## What is measured

`benchmarks/DeckHost.Benchmark` starts the same DeckHost you download, with its own empty data
folder, and a simulated MiraBox N4 Pro (the [device simulator](devices.md)) set up like a real desk:
a clock, CPU and memory readouts, a counter on a key and on a knob, and a key that flips between two
pages. It measures DeckHost from the outside, the way the system's task manager would:

| | How |
|---|---|
| **Start to first key image** | From starting the program until the device shows its first key. |
| **Idle, window open** | One minute with DeckHost's window open and the live keys updating, after a minute to settle (so start-up work isn't counted: DeckHost runs all day, and these numbers are how it spends it). |
| **Idle, in the tray** | One minute (again after a minute to settle) after starting the way it does at sign-in with *Start minimized*: no window, only the tray icon and the device. This is how DeckHost spends most of its day. |
| **Busy** | One minute of ten inputs a second: key presses, knob turns and a page flip every five seconds. |
| **Press → new key image** | For each press in the busy minute: the time until the device receives the key's new picture. |
| **Memory growth** | How much the memory grew from the start to the end of the busy minute, to catch leaks. |

CPU is the share of **one** processor core, summed over every DeckHost process (the app and plugins
that run in their own process): 100 % would be one core fully busy. Memory is the working set (what
the system shows as memory in use) of all of them together. Every number is sampled once a second.
Update checks and plugin updates are off during the run, so the numbers are DeckHost's own and not
the network's.

## Budgets

The limits are in `benchmarks/budgets.json`, with a default for every system and room to set one
system apart. A pull request whose numbers go over a limit fails its **performance** check, and the
comment on the pull request shows each number next to the last release's and the budget. A limit only
changes in a reviewed pull request that says why.

The runners are GitHub's shared virtual machines (Linux draws on a virtual screen without a graphics
card), so numbers vary somewhat from run to run and are higher than on a typical desktop. They are
meant to show changes and catch regressions; your own computer will usually do better.

## Measure it yourself

```bash
dotnet publish DeckHost.App -c Release -r linux-x64 --self-contained -o publish/DeckHost
dotnet run -c Release --project benchmarks/DeckHost.Benchmark -- --app publish/DeckHost/DeckHost.App --out result.json --budgets benchmarks/budgets.json
```

Use `win-x64` and `DeckHost.App.exe` on Windows, `osx-arm64` on a Mac. On Linux without a desktop,
put `xvfb-run -a` in front. `--phase-seconds` and `--warmup-seconds` shorten the run while you try
things. On Windows the benchmark uses the real `%APPDATA%\DeckHost`: with `--replace-data` it moves
yours aside and puts it back afterwards; without it, it refuses to run when that folder exists.
