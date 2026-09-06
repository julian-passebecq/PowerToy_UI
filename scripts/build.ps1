$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    dotnet restore .\JUtilityPalette.sln
    dotnet build .\JUtilityPalette.sln -c Release --no-restore
    dotnet run --project .\tests\JUtility.SmokeTests\JUtility.SmokeTests.csproj -c Release --no-build
}
finally {
    Pop-Location
}
