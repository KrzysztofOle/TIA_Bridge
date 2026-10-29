# Windows PowerShell 5.1 x64, .NET Framework csc, writable localhost X$ and NTFS TEMP.
# Offline filesystem tests only. No packages, TIA calls, shares or mappings are created.
param([string]$LabRoot = 'X:\TESTY_TIA_Bridge')
$ErrorActionPreference = 'Stop'
$id = [guid]::NewGuid().ToString('N')
$run = Join-Path $LabRoot ('evidence\LAB-03O-offline-' + $id)
New-Item -ItemType Directory -Path $run | Out-Null
$target = Join-Path $env:TEMP ('LAB-03O-target-' + $id)
$junction = Join-Path $env:TEMP ('LAB-03O-junction-' + $id)
New-Item -ItemType Directory -Path $target | Out-Null
New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\.tia-bridge\LAB-03O-' + $id)))
$unc = '\\localhost\X$\TESTY_TIA_Bridge\evidence\LAB-03O-offline-' + $id + '\unc'
$exe = Join-Path $run 'LabEventSyncTests.exe'
$sources = @('LabEventExport.cs','LabEventView.cs','LabEventSync.cs') | ForEach-Object { Join-Path $LabRoot ('src\' + $_) }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /platform:x64 /target:exe /main:LabEventSyncTests "/out:$exe" /reference:System.Web.Extensions.dll @sources (Join-Path $PSScriptRoot 'lab_events_tests.cs') (Join-Path $PSScriptRoot 'lab_event_sync_tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'LAB-03O test compilation failed' }
& $exe $run $repo $unc $junction | Tee-Object -FilePath (Join-Path $run 'console.txt')
$testExit = $LASTEXITCODE
if ($testExit -eq 0) {
    $compare = & py -3 (Join-Path $PSScriptRoot 'compare_events.py') compare-events --before (Join-Path $run 'first\events.json') --after (Join-Path $run 'update\events.json') --output (Join-Path $run 'comparisons')
    if ($LASTEXITCODE -ne 0) { throw 'Published overwrite events failed compare-events integration' }
    $report = Get-Content -LiteralPath (Join-Path $compare[0] 'comparison.json') -Raw | ConvertFrom-Json
    if ($report.counts.handler.MODIFIED -ne 1 -or $report.counts.handler.UNKNOWN -ne 0) { throw 'Updated JS not matched as exactly one modified handler' }
    [ordered]@{status='PASS';modified_handlers=1;unknown_handlers=0;report_directory=$compare[0]} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'compare-integration.json') -Encoding UTF8
}
Write-Output "Evidence: $run"
exit $testExit
