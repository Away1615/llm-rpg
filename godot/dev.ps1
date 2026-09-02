# Godot Demo build and focused runtime checks.
# Usage:
#   .\dev.ps1 -Task build
#   .\dev.ps1 -Task demo-check
#   .\dev.ps1 -Task live-escalation-check -Live
param(
    [ValidateSet("build", "demo-check", "live-escalation-check")]
    [string]$Task = "build",
    [switch]$Live
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $ProjectDir
$LogRoot = Join-Path $RepositoryRoot "tmp\godot"

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Resolve-GodotBinary {
    if ($env:GODOT_BIN) {
        return $env:GODOT_BIN
    }

    $Candidates = if ($IsWindows) {
        @("C:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64_console.exe")
    }
    elseif ($IsMacOS) {
        @(
            "/Applications/Godot_mono.app/Contents/MacOS/Godot",
            "/Applications/Godot.app/Contents/MacOS/Godot"
        )
    }
    else {
        @("godot4", "godot")
    }

    foreach ($Candidate in $Candidates) {
        if ([System.IO.Path]::IsPathRooted($Candidate)) {
            if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
                return $Candidate
            }
        }
        else {
            $Command = Get-Command $Candidate -ErrorAction SilentlyContinue
            if ($Command) {
                return $Command.Source
            }
        }
    }

    throw "Godot .NET console executable was not found. Set GODOT_BIN to its path."
}

function Assert-GodotLogClean {
    param(
        [string]$LogPath,
        [string]$Operation
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "$Operation did not produce its expected log: $LogPath"
    }

    $ForbiddenMarkers = @(
        "ERROR:",
        "SCRIPT ERROR:",
        "Missing .uid",
        "Parse Error",
        "Failed to load",
        "handle_crash:",
        "Program crashed with signal"
    )
    $Failures = @(Select-String -LiteralPath $LogPath -SimpleMatch -Pattern $ForbiddenMarkers)
    if ($Failures.Count -ne 0) {
        $FailureText = $Failures | Select-Object -First 10 | ForEach-Object { $_.Line }
        throw "$Operation log contains failures:`n$($FailureText -join [Environment]::NewLine)"
    }
}

function Invoke-AliceBuild {
    dotnet build (Join-Path $ProjectDir "Alice.csproj")
    Assert-LastExitCode "Alice build"
}

function Invoke-DemoCheck {
    $GodotBin = Resolve-GodotBinary
    New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null

    $ImportLog = Join-Path $LogRoot "editor-import.log"
    $ImportProcessLog = Join-Path $LogRoot "editor-import-process.log"
    & $GodotBin --headless --path $ProjectDir --log-file $ImportLog --import 2>&1 |
        Tee-Object -FilePath $ImportProcessLog
    Assert-LastExitCode "Godot editor import"
    Assert-GodotLogClean $ImportLog "Godot editor import"
    Assert-GodotLogClean $ImportProcessLog "Godot editor import process"

    $RunLog = Join-Path $LogRoot "town-map.log"
    $RunProcessLog = Join-Path $LogRoot "town-map-process.log"
    & $GodotBin --headless --path $ProjectDir --log-file $RunLog `
        "res://Scenes/World/TownMap.tscn" --fixed-fps 60 --quit-after 600 -- --auto-validate 2>&1 |
        Tee-Object -FilePath $RunProcessLog
    Assert-LastExitCode "Town Demo headless run"
    Assert-GodotLogClean $RunLog "Town Demo headless run"
    Assert-GodotLogClean $RunProcessLog "Town Demo headless process"

    $LogText = Get-Content -LiteralPath $RunLog -Raw
    if ($LogText.IndexOf("LIVING_TOWN_DEMO PASS", [StringComparison]::Ordinal) -lt 0) {
        throw "Town Demo exited without the required LIVING_TOWN_DEMO PASS marker."
    }

    Write-Host "DEMO_CHECK=PASS"
}

function Invoke-LiveEscalationCheck {
    if (-not $Live) {
        throw "This diagnostic calls configured live model endpoints. Re-run with -Live to authorize it."
    }

    $GodotBin = Resolve-GodotBinary
    New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
    $RunLog = Join-Path $LogRoot "live-escalation.log"
    $RunProcessLog = Join-Path $LogRoot "live-escalation-process.log"
    & $GodotBin --headless --path $ProjectDir --log-file $RunLog `
        "res://Scenes/World/TownMap.tscn" -- --live-escalation-check 2>&1 |
        Tee-Object -FilePath $RunProcessLog
    Assert-LastExitCode "Live escalation diagnostic"
    Assert-GodotLogClean $RunLog "Live escalation diagnostic"
    Assert-GodotLogClean $RunProcessLog "Live escalation diagnostic process"

    $LogText = Get-Content -LiteralPath $RunLog -Raw
    if ($LogText.IndexOf("LIVE_ESCALATION_CHECK PASS", [StringComparison]::Ordinal) -lt 0) {
        throw "Live escalation diagnostic exited without its required PASS marker."
    }
}

switch ($Task) {
    "build" {
        Invoke-AliceBuild
    }
    "demo-check" {
        Invoke-AliceBuild
        Invoke-DemoCheck
    }
    "live-escalation-check" {
        Invoke-AliceBuild
        Invoke-LiveEscalationCheck
    }
}
