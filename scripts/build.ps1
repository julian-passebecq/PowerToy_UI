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
Push-Location $repo
try {
    Invoke-Dotnet -Arguments @('restore', '.\JUtilityPalette.sln')
    Invoke-Dotnet -Arguments @('build', '.\JUtilityPalette.sln', '-c', 'Release', '--no-restore')
    Invoke-Dotnet -Arguments @('run', '--project', '.\tests\JUtility.SmokeTests\JUtility.SmokeTests.csproj', '-c', 'Release', '--no-build')
}
finally {
    Pop-Location
}
