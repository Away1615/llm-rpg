param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("formal", "cognitive-lod", "l1-cost-agreement")]
    [string]$Experiment,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExperimentArgs
)

$ErrorActionPreference = "Stop"
$RepositoryRoot = Split-Path -Parent $PSScriptRoot

$ProjectPath = switch ($Experiment) {
    "formal" {
        Join-Path $PSScriptRoot "Alice.FormalStudyExperiment\Alice.FormalStudyExperiment.csproj"
    }
    "cognitive-lod" {
        Join-Path $PSScriptRoot "Alice.CognitiveLodDialogueExperiment\Alice.CognitiveLodDialogueExperiment.csproj"
    }
    "l1-cost-agreement" {
        Join-Path $PSScriptRoot "Alice.L1CostAgreementExperiment\Alice.L1CostAgreementExperiment.csproj"
    }
}

if ($ExperimentArgs.Count -eq 0) {
    throw "An experiment mode is required. See experiments/README.md."
}

Push-Location $RepositoryRoot
try {
    & dotnet run --project $ProjectPath -- @ExperimentArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$Experiment failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
