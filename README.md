# ALICE

ALICE is a Godot 4.7 .NET demo of a persistent NPC town with bounded local and remote LLM cognition.

## Repository contents

- `godot/` — the runnable Demo, runtime configuration and shared model/runtime code.
- `experiments/` — separate RQ1 and RQ2 launch modes plus the retained supplementary-study runners.
- `results/` — one archive containing the latest retained machine-readable experiment batches.

Runtime regression projects, writing material, source-paper copies and generated build/run directories are not
retained.

## Run the Demo

Build from the repository root:

```powershell
.\godot\dev.ps1 -Task build
```

Open `godot/project.godot` in Godot 4.7.x .NET and run `Scenes/World/TownMap.tscn`, or run the headless Demo
check:

```powershell
.\godot\dev.ps1 -Task demo-check
```

See [`godot/README.md`](godot/README.md) for runtime configuration, [`experiments/README.md`](experiments/README.md)
for experiment commands, and [`results/README.md`](results/README.md) for the retained result archive.
