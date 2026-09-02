# ALICE Godot Demo

This directory contains the production Town Demo and the shared runtime used by the standalone experiment runners.

## Requirements

- Godot 4.7.x .NET.
- .NET 8 SDK.
- LM Studio at the endpoint configured in `Config/town_world.json` for live local-model behavior.
- The configured remote credential, currently `DEEPSEEK_API_KEY`, for live remote planning.

Missing live endpoints or credentials remain explicit runtime failures; there is no recorded-response fallback.

## Build and run

From the repository root:

```powershell
.\godot\dev.ps1 -Task build
.\godot\dev.ps1 -Task demo-check
```

`GODOT_BIN` may override the default Godot console executable used by `demo-check`.

For interactive use, open `project.godot` and run `Scenes/World/TownMap.tscn`. The map, population and provider
profiles are loaded from `Config/town_world.json`.

The cost-bearing live escalation diagnostic is separate and requires explicit authorization:

```powershell
.\godot\dev.ps1 -Task live-escalation-check -Live
```

## Main directories

- `Scenes/World/` — Town Demo entry and presentation wiring.
- `Config/` — runtime configuration and formal-study readiness values.
- `Data/` — Demo data plus frozen experiment inputs.
- `Src/` — shared domain, cognition, model, validation, Authority and Godot integration code.
- `Artifacts/` — generated experiment output; ignored and not retained in the repository.

Standalone experiments are documented in [`../experiments/README.md`](../experiments/README.md). The latest selected
result batches are stored in [`../results/latest-experiment-results.zip`](../results/latest-experiment-results.zip).
