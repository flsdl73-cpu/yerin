$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'artifacts/windows'
$output = Join-Path $package 'validation/windows-ci'
$arguments = '--validate "{0}" "{1}"' -f (Join-Path $package 'validation/screens'), $output
function Write-Summary([string]$text) {
    Write-Host $text
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Encoding utf8 $env:GITHUB_STEP_SUMMARY $text }
}
Write-Summary 'Checking saved screens with the installed Windows OCR engine. No game input is sent.'
$process = Start-Process -FilePath (Join-Path $package 'AbyssAuto.exe') -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit(45000)) {
    $process.Kill($true)
    $process.WaitForExit()
    Write-Summary 'Windows OCR saved-screen validation: NOT COMPLETED (45-second limit). Run validate-screens.ps1 on the user PC. Build success does not mean OCR passed.'
    exit 2
}
$exitCode = $process.ExitCode
$errorFile = Join-Path $output 'windows-ocr-error.txt'
if ($exitCode -eq 2) {
    Write-Summary 'Windows OCR saved-screen validation: NOT EXECUTED. See validation/windows-ci/windows-ocr-error.txt. This runner does not validate the user PC OCR environment.'
    if (Test-Path $errorFile) { Write-Host (Get-Content $errorFile -Raw -Encoding utf8) }
    exit 2
}
$report = Get-Content (Join-Path $output 'windows-ocr-report.json') -Raw -Encoding utf8 | ConvertFrom-Json
Write-Summary "Windows OCR saved-screen validation: $($report.checks) checks, $($report.failures) mismatches."
foreach ($row in $report.rows) {
    if ($row.expected -ne $row.detection.Passed) {
        Write-Summary "Mismatch: $($row.screen)/$($row.target); expected=$($row.expected), passed=$($row.detection.Passed), OCR=$($row.detection.Ocr)"
    }
}
exit $exitCode
