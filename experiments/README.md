# Experiment entry points

The experiment runners are independent console projects. They share production types from `godot/Alice.csproj`,
but they are not part of the Godot Demo startup path.

Run commands from the repository root. Offline validation and dry modes do not call paid providers. Live modes may
incur API cost.

## Formal RQ1 and RQ2 study

Validate the retained 30-block RQ1 inputs and RQ2 fixture design without calling a provider:

```powershell
.\experiments\run.ps1 formal --validate-inputs
```

From a clean Git checkout, create a freeze bundle. RQ1 and RQ2 then have separate launch commands:

```powershell
.\experiments\run.ps1 formal --write-freeze-bundle tmp/formal-freeze.json
.\experiments\run.ps1 formal --run-rq1 tmp/formal-freeze.json
.\experiments\run.ps1 formal --run-rq2 tmp/formal-freeze.json
```

RQ1 defaults to 30 fixed workers and accepts `--worker-count N`. To give workers separate credential variables,
pass `--credential-environment-names NAME1,NAME2,...`; otherwise the live study reads `DEEPSEEK_API_KEY`.

The runner deliberately consumes the preserved frozen preregistration, model profile and source manifest. Those
files describe the exact inputs used by the retained final result even though the runner has moved out of `tests/`.

## Cognitive LOD dialogue study

Run the offline preflight:

```powershell
.\experiments\run.ps1 cognitive-lod preflight
```

Run a dry controlled batch or a live controlled batch:

```powershell
.\experiments\run.ps1 cognitive-lod dry-controlled --output tmp/cognitive-lod/dry-controlled
.\experiments\run.ps1 cognitive-lod live-controlled --output tmp/cognitive-lod/live-controlled
```

The other modes are `dry-workload` and `live-workload`. Controlled modes accept `--repeats N`.

## L1 cost and agreement study

Run the offline preflight:

```powershell
.\experiments\run.ps1 l1-cost-agreement --preflight
```

Run the live study, optionally limiting it to the configured local model:

```powershell
.\experiments\run.ps1 l1-cost-agreement --live --local-only --output tmp/l1-cost/local-result.json
.\experiments\run.ps1 l1-cost-agreement --live --output tmp/l1-cost/full-result.json
```

Analyze a completed result:

```powershell
.\experiments\run.ps1 l1-cost-agreement --analyze --input tmp/l1-cost/full-result.json --output tmp/l1-cost/analysis.json
```

New outputs belong below ignored `tmp/` or `godot/Artifacts/` paths; they are not retained in Git.
