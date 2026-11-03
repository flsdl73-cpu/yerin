$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'AbyssAuto.exe'
if (-not (Test-Path $executable)) { throw '배포 ZIP을 풀어 AbyssAuto.exe 옆에서 실행하세요.' }
$screens = Join-Path $PSScriptRoot 'validation/screens'
$output = Join-Path $PSScriptRoot 'validation/windows-results'
$arguments = '--validate "{0}" "{1}"' -f $screens, $output
$process = Start-Process -FilePath $executable -ArgumentList $arguments -PassThru -Wait
Write-Host "검사 종료 코드: $($process.ExitCode) (0=모든 판정 일치, 1=불일치, 2=실행 오류)"
Write-Host "결과 위치: $output"
$report = Join-Path $output 'windows-ocr-report.json'
$errorReport = Join-Path $output 'windows-ocr-error.txt'
if (Test-Path $report) { Get-Content -Encoding UTF8 $report }
if ($process.ExitCode -eq 2 -and (Test-Path $errorReport)) { Get-Content -Encoding UTF8 $errorReport }
exit $process.ExitCode
