$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build CommunityFrontier.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    dotnet run --project tests/CommunityFrontier.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed.' }
    dotnet run --project tests/CommunityFrontier.SessionTests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Saved session tests failed.' }
} finally { Pop-Location }
