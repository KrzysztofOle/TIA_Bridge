# Windows PowerShell 5.1 x64 and .NET Framework csc; no packages, Attach or Siemens calls.
param([string]$LabRoot = 'X:\TESTY_TIA_Bridge')
$ErrorActionPreference = 'Stop'
$run = Join-Path $LabRoot ('evidence\LAB-03K-offline-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $run 'LabEventsTests.exe'
& $csc /nologo /platform:x64 /target:exe "/out:$exe" /reference:System.Web.Extensions.dll (Join-Path $LabRoot 'src\LabEventExport.cs') (Join-Path $LabRoot 'src\LabEventView.cs') (Join-Path $PSScriptRoot 'lab_events_tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Offline test compilation failed' }
& $exe $run (Join-Path $LabRoot 'bin\LabExporter.exe') | Tee-Object -FilePath (Join-Path $run 'console.txt')
$testExit = $LASTEXITCODE
$launch = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\.vscode\launch.json') -Raw | ConvertFrom-Json
$configuration = @($launch.configurations | Where-Object { $_.name -like 'TIA Bridge: EXPORT EVENTS*' -and $_.name -notlike '*SNAPSHOT*' })
if ($configuration.Count -ne 1) { throw 'EXPORT EVENTS launch configuration missing or duplicated' }
# Verify the exact configured CLI arguments without invoking the exporter.
$expected = @(
    '& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" export-events',
    '--project "X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy"',
    '--output "X:\TESTY_TIA_Bridge\EventExports"',
    '--hmi "HMI_JSON_TEST/HMI_RT_1"'
)
foreach ($argument in $expected) {
    if (-not $configuration[0].command.Contains($argument)) { throw "Launch argument missing: $argument" }
}
if ($configuration[0].command -match 'Read-Host|ReadLine') { throw 'Complete launch configuration still prompts interactively' }
if ($configuration[0].command -match '--mode') { throw 'Default export launch must use implicit overwrite' }
$snapshot = @($launch.configurations | Where-Object { $_.name -like '*EXPORT EVENTS*SNAPSHOT*' })
if ($snapshot.Count -ne 1 -or $snapshot[0].command -cne ($configuration[0].command + ' --mode snapshot')) { throw 'Snapshot launch arguments differ beyond explicit mode' }
$forceExport = @($launch.configurations | Where-Object { $_.name -like 'TIA Bridge: FORCE EXPORT*' })
$forceImport = @($launch.configurations | Where-Object { $_.name -like 'TIA Bridge: FORCE IMPORT*' })
if ($forceExport.Count -ne 1 -or $forceExport[0].command -cne '& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-export --project "X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy" --output "X:\TESTY_TIA_Bridge\Scripts"') { throw 'FORCE EXPORT configuration changed' }
if ($forceImport.Count -ne 1 -or $forceImport[0].command -cne '& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-import --project "X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy" --input "X:\TESTY_TIA_Bridge\Scripts"') { throw 'FORCE IMPORT configuration changed' }
# Parse and validate actual arguments through a synthetic argument sink, never LabExporter.exe.
$command = $configuration[0].command.Replace('X:\TESTY_TIA_Bridge\bin\LabExporter.exe', $exe).Replace(' export-events ', ' --echo-args ')
if ($command -eq $configuration[0].command -or $command.Contains('LabExporter.exe')) { throw 'Synthetic launch substitution failed; refusing execution' }
$captured = & ([scriptblock]::Create($command)) | ConvertFrom-Json
if ($captured.Count -ne 7 -or $captured[0] -cne '--echo-args' -or $captured[2] -cne 'X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy' -or $captured[4] -cne 'X:\TESTY_TIA_Bridge\EventExports' -or $captured[6] -cne 'HMI_JSON_TEST/HMI_RT_1') { throw 'Launch argument binding failed' }
[ordered]@{status='PASS';check='launch config binds exact project, output and HMI arguments without prompts';live_vscode='NOT_RUN'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch-check.json') -Encoding UTF8
# Invalid arguments must fail before project discovery; never supply valid live arguments here.
$ErrorActionPreference = 'Continue'
$invalid = & (Join-Path $LabRoot 'bin\LabExporter.exe') export-events 2>&1
$invalidExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($invalidExit -ne 1 -or ($invalid -join "`n") -match 'EXPORT EVENTS: read-only Attach') { throw 'Invalid CLI did not fail before Attach' }
[ordered]@{status='PASS';check='invalid CLI fails before any Attach';exit_code=$invalidExit} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'cli-check.json') -Encoding UTF8
Write-Output "Evidence: $run"
exit $testExit
