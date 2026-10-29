# Build only. Windows PowerShell 5.1 x64, .NET Framework csc and installed V19 PublicAPI.
# No Siemens calls. The previous laboratory binary must be retained before replacement.
param([string]$LabRoot = 'X:\TESTY_TIA_Bridge')
$ErrorActionPreference = 'Stop'
$vendor = 'C:\Program Files\Siemens\Automation\Portal V19'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$run = Join-Path $LabRoot ('evidence\LAB-03K-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$binary = Join-Path $LabRoot 'bin\LabExporter.exe'
if (Test-Path -LiteralPath $binary) { Copy-Item -LiteralPath $binary -Destination (Join-Path $run 'LabExporter.before.exe') }
$candidate = Join-Path $run 'LabExporter.exe'
$sources = @('LabExporter.cs','LabEventExport.cs','LabEventVendor.cs','LabEventView.cs','LabEventConsole.cs') | ForEach-Object { Join-Path $LabRoot ('src\' + $_) }
& $csc /nologo /platform:x64 /target:exe "/out:$candidate" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$vendor\PublicAPI\V19\Siemens.Engineering.dll" "/reference:$vendor\PublicAPI\V19\Siemens.Engineering.Hmi.dll" "/reference:$vendor\Bin\PublicAPI\Siemens.Engineering.Contract.dll" "/reference:$vendor\Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll" @sources
if ($LASTEXITCODE -ne 0) { throw 'Laboratory compilation failed; current binary preserved' }
Copy-Item -LiteralPath $candidate -Destination $binary -Force
@($sources + $binary | ForEach-Object { $file=Get-Item -LiteralPath $_; [ordered]@{path=$file.FullName;length=$file.Length;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'build-manifest.json') -Encoding UTF8
Write-Output "Build PASS. Evidence: $run"
