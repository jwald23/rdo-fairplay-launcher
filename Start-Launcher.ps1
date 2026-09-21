$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build launcher/CommunityFrontier.Launcher -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Install the .NET 10 SDK and try again.' }
    & '.\launcher\CommunityFrontier.Launcher\bin\Release\net10.0-windows\RDOFairPlay.exe'
} finally { Pop-Location }
