param(
    [string] $DataDirectory = '',
    [switch] $Reset,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    $DataDirectory = Join-Path $env:TEMP 'PowerOps-Codex-LiveTest-V1'
}

$DataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)

if ($Reset -and (Test-Path $DataDirectory)) {
    Write-Host "Resetting isolated live-test workspace: $DataDirectory"
    Remove-Item $DataDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null

Push-Location $repo
try {
    Write-Host "Repository: $repo"
    Write-Host "Live-test data: $DataDirectory"

    $branch = (& git branch --show-current).Trim()
    if ($branch -ne 'codex/to-be-tested-v1') {
        throw "Expected branch 'codex/to-be-tested-v1' but current branch is '$branch'."
    }

    if (-not $SkipBuild) {
        & .\scripts\build.ps1
        if ($LASTEXITCODE -ne 0) {
            throw "Build/smoke gate failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host ""
    Write-Host "Launching Power Ops against the isolated workspace."
    Write-Host "Use handover\CODEX_LIVE_TEST_V1.md as the acceptance matrix."
    Write-Host ""

    & .\scripts\run.ps1 -DataDirectory $DataDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Power Ops exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
