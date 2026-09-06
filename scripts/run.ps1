$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    dotnet run --project .\src\JUtility.App\JUtility.App.csproj
}
finally {
    Pop-Location
}
