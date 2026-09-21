param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo "artifacts\PowerOps-$Runtime"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo $OutputDirectory
}

$zipPath = "$OutputDirectory.zip"

Push-Location $repo
try {
    if (Test-Path $OutputDirectory) {
        Remove-Item $OutputDirectory -Recurse -Force
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    Invoke-Dotnet -Arguments @(
        'publish',
        '.\src\JUtility.App\JUtility.App.csproj',
        '-c', 'Release',
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $OutputDirectory
    )

    $releaseNotes = @"
Power Ops / J Utility Palette

Run JUtilityPalette.exe.

Workspace data is stored outside this package in:
%LOCALAPPDATA%\JUtilityPalette\workspace.json

This package does not contain your local workspace, GitHub credentials,
or browser data. GitHub private-repository discovery uses your locally
authenticated gh CLI when available.
"@
    Set-Content -Path (Join-Path $OutputDirectory 'README.txt') -Value $releaseNotes -Encoding UTF8

    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host "Published Power Ops:"
    Write-Host "  Folder: $OutputDirectory"
    Write-Host "  ZIP:    $zipPath"
}
finally {
    Pop-Location
}
