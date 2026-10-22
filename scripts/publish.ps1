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
$version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Directory.Build.props has no <Version>; freeze one before publishing.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo "artifacts\PowerOps-$version-$Runtime"
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

Version $version ($Runtime, self-contained: no .NET install needed).

Install for the current user (no admin rights; keeps every older version for rollback):
  powershell -ExecutionPolicy Bypass -File .\install.ps1
  then start %LOCALAPPDATA%\Programs\PowerOps\PowerOps.cmd
Roll back to the previously installed version:
  powershell -ExecutionPolicy Bypass -File .\install.ps1 -Rollback
Or run JUtilityPalette.exe directly from this folder.

For a separate isolated workspace:
JUtilityPalette.exe --data-dir "D:\PowerOps-Test"

Workspace data is stored outside this package in:
%LOCALAPPDATA%\JUtilityPalette\workspace.json

This package does not contain your local workspace, GitHub credentials,
or browser data. GitHub private-repository discovery uses your locally
authenticated gh CLI when available.
"@
    Set-Content -Path (Join-Path $OutputDirectory 'README.txt') -Value $releaseNotes -Encoding UTF8
    Copy-Item (Join-Path $PSScriptRoot 'install.ps1') (Join-Path $OutputDirectory 'install.ps1')

    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host "Published Power Ops:"
    Write-Host "  Folder: $OutputDirectory"
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ascii
    Write-Host "  ZIP:    $zipPath"
    Write-Host "  SHA256: $hash"
}
finally {
    Pop-Location
}
