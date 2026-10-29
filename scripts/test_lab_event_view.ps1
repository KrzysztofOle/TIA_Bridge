# Offline only. Windows PowerShell 5.1 x64 and .NET Framework csc. No packages or Siemens calls.
# BaselineSource: preserved LAB-03K LabEventExport.cs from the pre-change evidence directory.
param(
    [string]$LabRoot = 'X:\TESTY_TIA_Bridge',
    [Parameter(Mandatory=$true)][string]$BaselineSource
)
$ErrorActionPreference = 'Stop'
$run = Join-Path $LabRoot ('evidence\LAB-03L-offline-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$common = @((Join-Path $PSScriptRoot 'lab_events_tests.cs'), (Join-Path $PSScriptRoot 'lab_event_view_tests.cs'))
$exe = Join-Path $run 'LabEventViewTests.exe'
& $csc /nologo /platform:x64 /target:exe /main:LabEventViewTests "/out:$exe" /reference:System.Web.Extensions.dll (Join-Path $LabRoot 'src\LabEventExport.cs') (Join-Path $LabRoot 'src\LabEventView.cs') @common
if ($LASTEXITCODE -ne 0) { throw 'LAB-03L tests compilation failed' }
& $exe $run | Tee-Object -FilePath (Join-Path $run 'console.txt')
$testExit = $LASTEXITCODE
$baselineExe = Join-Path $run 'Lab03KBaseline.exe'
& $csc /nologo /platform:x64 /target:exe /define:BASELINE /main:LabEventViewTests "/out:$baselineExe" /reference:System.Web.Extensions.dll $BaselineSource @common
if ($LASTEXITCODE -ne 0) { throw 'Baseline fixture compilation failed' }
$before = Join-Path $run 'before'
$after = Join-Path $run 'after'
New-Item -ItemType Directory -Path $before,$after | Out-Null
& $baselineExe $before --fixture
if ($LASTEXITCODE -ne 0) { throw 'Baseline fixture failed' }
& $exe $after --fixture
if ($LASTEXITCODE -ne 0) { throw 'Current fixture failed' }
$compared = @()
Get-ChildItem -LiteralPath (Join-Path $before 'fixture') -File -Recurse | Where-Object Name -ne 'manifest.json' | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $before 'fixture').Length + 1)
    $actual = Join-Path (Join-Path $after 'fixture') $relative
    if (-not (Test-Path -LiteralPath $actual) -or (Get-FileHash -LiteralPath $_.FullName).Hash -cne (Get-FileHash -LiteralPath $actual).Hash) { throw "LAB-03K artifact changed: $relative" }
    $compared += $relative
}
[ordered]@{status='PASS';baseline_source_sha256=(Get-FileHash -LiteralPath $BaselineSource).Hash;unchanged_artifacts=$compared} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'lab03k-byte-regression.json') -Encoding UTF8
Write-Output "LAB-03K byte regression PASS: $($compared.Count) original artifacts. Evidence: $run"
exit $testExit
