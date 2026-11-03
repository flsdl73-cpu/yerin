$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'artifacts/windows'
$output = Join-Path $package 'validation/windows-ci'
$arguments = '--validate "{0}" "{1}"' -f (Join-Path $package 'validation/screens'), $output
function Invoke-ScreenCheck {
    $process = Start-Process -FilePath (Join-Path $package 'AbyssAuto.exe') -ArgumentList $arguments -PassThru -Wait
    return $process.ExitCode
}
function Write-Summary([string]$text) {
    Write-Host $text
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Encoding utf8 $env:GITHUB_STEP_SUMMARY $text }
}
$exitCode = Invoke-ScreenCheck
$errorFile = Join-Path $output 'windows-ocr-error.txt'
# Hosted Windows images may not include Korean OCR. Install only the official OS feature.
if ($exitCode -eq 2 -and (Test-Path $errorFile) -and (Get-Content $errorFile -Raw -Encoding utf8).Contains('한국어 OCR')) {
    try {
        $feature = Get-WindowsCapability -Online -Name 'Language.OCR~~~ko-KR~0.0.1.0'
        if ($feature.State -ne 'Installed') {
            $installed = Add-WindowsCapability -Online -Name 'Language.OCR~~~ko-KR~0.0.1.0'
            if ($installed.RestartNeeded) { throw 'OCR feature requires a restart; not validated in this runner.' }
        }
        $exitCode = Invoke-ScreenCheck
    } catch {
        Write-Summary "Windows OCR setup unavailable: $($_.Exception.Message)"
    }
}
if ($exitCode -eq 2) {
    Write-Summary 'Windows OCR saved-screen validation: NOT EXECUTED. See validation/windows-ci/windows-ocr-error.txt. Build success does not mean OCR passed.'
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
$retry = $report.rows | Where-Object { $_.screen -eq 'reward' -and $_.target -eq 'retry' }
Write-Summary "Reward retry: passed=$($retry.detection.Passed), OCR=$($retry.detection.Ocr), color=$($retry.detection.Color)"
exit $exitCode
