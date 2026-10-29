# Offline only: Windows PowerShell 5.1 x64, .NET Framework csc, writable localhost X$ share.
# Uses real filesystem/ACL operations on new synthetic directories. No Siemens calls/packages.
param([string]$LabRoot = 'X:\TESTY_TIA_Bridge')
$ErrorActionPreference = 'Stop'
$id = [guid]::NewGuid().ToString('N')
$run = Join-Path $LabRoot ('evidence\LAB-03N-offline-' + $id)
$repoOutput = Join-Path $PSScriptRoot ('..\.tia-bridge\LAB-03N-output-' + $id)
$uncOutput = '\\localhost\X$\TESTY_TIA_Bridge\evidence\LAB-03N-offline-' + $id + '\unc'
New-Item -ItemType Directory -Path $run | Out-Null
$junctionTarget = Join-Path $env:TEMP ('LAB-03N-target-' + $id)
$junction = Join-Path $env:TEMP ('LAB-03N-junction-' + $id)
New-Item -ItemType Directory -Path $junctionTarget | Out-Null
New-Item -ItemType Junction -Path $junction -Target $junctionTarget | Out-Null
$exe = Join-Path $run 'LabEventOutputTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /platform:x64 /target:exe "/out:$exe" /reference:System.Web.Extensions.dll (Join-Path $LabRoot 'src\LabEventExport.cs') (Join-Path $LabRoot 'src\LabEventView.cs') (Join-Path $PSScriptRoot 'lab_event_output_tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'LAB-03N test compilation failed' }
& $exe $run ([IO.Path]::GetFullPath($repoOutput)) $uncOutput $junction $junctionTarget | Tee-Object -FilePath (Join-Path $run 'console.txt')
$testExit = $LASTEXITCODE
Write-Output "Evidence: $run"
exit $testExit
