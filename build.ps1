$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet test tests/AbyssAuto.Tests/AbyssAuto.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish src/AbyssAuto/AbyssAuto.csproj -c Release -r win-x64 --self-contained true -o artifacts/windows
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md, VALIDATION.md, validate-screens.ps1, UPDATE-0.4.2.txt artifacts/windows/ -Force
    New-Item -ItemType Directory -Path artifacts/windows/validation -Force | Out-Null
    Copy-Item validation/* artifacts/windows/validation -Recurse -Force
    New-Item -ItemType Directory -Path artifacts/update/validation/screens -Force | Out-Null
    Copy-Item artifacts/windows/AbyssAuto.dll, src/AbyssAuto/targets.json artifacts/update/ -Force
    Copy-Item validation/screens/battle-dither.png artifacts/update/validation/screens/ -Force
    Copy-Item UPDATE-0.4.2.txt artifacts/update/ -Force
    dotnet publish src/AbyssAuto/AbyssAuto.csproj -c Release -r win-x64 --self-contained true -p:DriverFree=true -p:Version=0.5.0 -o artifacts/no-driver
    if ($LASTEXITCODE -ne 0) { throw 'Driver-free publish failed' }
    Copy-Item NO-DRIVER.txt artifacts/no-driver/README.txt -Force
    Copy-Item validate-screens.ps1 artifacts/no-driver/ -Force
    New-Item -ItemType Directory -Path artifacts/no-driver/validation -Force | Out-Null
    Copy-Item validation/* artifacts/no-driver/validation -Recurse -Force
    New-Item -ItemType Directory -Path artifacts/no-driver-update -Force | Out-Null
    Copy-Item artifacts/no-driver/AbyssAuto.dll artifacts/no-driver-update/ -Force
    Copy-Item NO-DRIVER.txt artifacts/no-driver-update/README.txt -Force
    $managedBytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'artifacts/no-driver/AbyssAuto.dll'))
    $metadataText = [Text.Encoding]::UTF8.GetString($managedBytes)
    if ($metadataText.Contains('interception.dll') -or $metadataText.Contains('interception_create_context')) { throw 'Driver import remained in driver-free build' }
    if (Get-ChildItem artifacts/no-driver -Recurse -File -Filter *.sys) { throw 'Unexpected driver file in driver-free package' }
    Write-Host 'Legacy: artifacts/windows. Driver-free: artifacts/no-driver.'
} finally { Pop-Location }
