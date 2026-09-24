param(
    [string] $DataDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$arguments = @(
    'run',
    '--project',
    '.\src\JUtility.App\JUtility.App.csproj'
)

if (-not [string]::IsNullOrWhiteSpace($DataDirectory)) {
    $arguments += @('--', '--data-dir', $DataDirectory)
}

Push-Location $repo
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
