$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet test tests/AbyssAuto.Tests/AbyssAuto.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish src/AbyssAuto/AbyssAuto.csproj -c Release -r win-x64 --self-contained true -o artifacts/windows
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md, VALIDATION.md, validate-screens.ps1, UPDATE-0.4.1.txt artifacts/windows/ -Force
    New-Item -ItemType Directory -Path artifacts/windows/validation -Force | Out-Null
    Copy-Item validation/* artifacts/windows/validation -Recurse -Force
    New-Item -ItemType Directory -Path artifacts/update/validation/screens -Force | Out-Null
    Copy-Item artifacts/windows/AbyssAuto.dll, src/AbyssAuto/targets.json artifacts/update/ -Force
    Copy-Item validation/screens/battle-dither.png artifacts/update/validation/screens/ -Force
    Copy-Item UPDATE-0.4.1.txt artifacts/update/ -Force
    Write-Host 'Output: artifacts/windows. Add official x64 interception.dll separately.'
} finally { Pop-Location }
