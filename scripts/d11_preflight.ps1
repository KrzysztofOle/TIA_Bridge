<#! Read-only Windows/CIM preflight. Windows PowerShell 5.1+, no packages, elevation or Siemens API.
Only creates a new JSON artifact below the repository .tia-bridge directory. No process/config changes. #>
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$allowedRoot = $repoRoot + '\.tia-bridge\'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (-not $outputRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be in the repository .tia-bridge directory.' }
$started = [DateTimeOffset]::UtcNow
$all = @(Get-CimInstance Win32_Process)
$services = @(Get-CimInstance Win32_Service)
$byPid = @{}; foreach ($row in $all) { $byPid[[int]$row.ProcessId] = $row }
$selected = @($all | Where-Object { $_.Name -eq 'git.exe' -or $_.Name -match 'Siemens|almapp|almservice|s7oiehsx|S7Trace|almsrv' })
$rows = @($selected | ForEach-Object {
    $item = $_; $parent = $byPid[[int]$item.ParentProcessId]
    if ($null -ne $parent -and $parent.CreationDate -gt $item.CreationDate) { $parent = $null }
    $command = $item.CommandLine
    if ($null -ne $command) { $command = $command -replace '(https?://)[^\s/@]+@','$1[REDACTED]@' -replace '(?i)(token|password|secret|authorization|extraheader)([= :]+).*','$1$2[REDACTED]' }
    $kind = 'UNCERTAIN'
    if ($item.Name -eq 'git.exe') { if ($command -match 'fsmonitor--daemon run') { $kind = 'GIT_FSMONITOR_DAEMON' } else { $kind = 'GIT_OTHER' } }
    elseif ($item.Name -eq 'Siemens.Automation.Portal.exe') { $kind = 'TIA_PORTAL_MAIN' }
    elseif ($item.Name -match 'alm|almsrv') { $kind = 'AUTOMATION_LICENSE_MANAGER' }
    elseif (@($services | Where-Object { $_.ProcessId -eq $item.ProcessId }).Count -gt 0) { $kind = 'SYSTEM_SERVICE' }
    elseif ($item.Name -match 'PlcSim|Simulation') { $kind = 'SIMULATION_HELPER' }
    elseif ($item.Name -eq 'siemens-lsp.exe') { $kind = 'EDITOR_LANGUAGE_SERVER' }
    elseif ($item.Name -match 'Siemens') { $kind = 'SIEMENS_HELPER' }
    $running = 'UNAVAILABLE'; $threads = $null
    try { $process = [Diagnostics.Process]::GetProcessById([int]$item.ProcessId); try { $running = -not $process.HasExited; $threads = $process.Threads.Count } finally { $process.Dispose() } } catch { }
    [ordered]@{ name=$item.Name; pid=$item.ProcessId; parent_pid=$item.ParentProcessId; parent_name=$parent.Name; parent_path=$parent.ExecutablePath; executable=$item.ExecutablePath; command_line=$command; unavailable_reason=$(if (-not $command -or -not $item.ExecutablePath) {'CIM_FIELDS_UNAVAILABLE'} else {$null}); start_time=$item.CreationDate.ToUniversalTime().ToString('o'); session_id=$item.SessionId; running=$running; threads=$threads; classification=$kind; services=@($services | Where-Object { $_.ProcessId -eq $item.ProcessId } | Select-Object Name,State); cpu_100ns=([decimal]$item.KernelModeTime+[decimal]$item.UserModeTime); read_bytes=$item.ReadTransferCount; write_bytes=$item.WriteTransferCount }
})
$activity = @()
for ($round=0; $round -lt 5; $round++) {
    if ($round -gt 0) { Start-Sleep -Seconds 2 }
    $snapshot = @(Get-CimInstance Win32_Process -Filter "Name='git.exe'")
    $activity += [ordered]@{ at=[DateTimeOffset]::UtcNow.ToString('o'); system_cpu=(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" | Select-Object PercentProcessorTime); processes=@($snapshot | Select-Object ProcessId,CreationDate,KernelModeTime,UserModeTime,ReadTransferCount,WriteTransferCount) }
}
$paths = @($repoRoot, (Join-Path $repoRoot '.git'), (Join-Path $repoRoot '.tia-bridge\resilience\workspaces'), (Join-Path $repoRoot '.tia-bridge\b02c\workspaces'), (Join-Path $env:LOCALAPPDATA 'TIA_Bridge2\dev\storage-tests'), (Join-Path $env:LOCALAPPDATA 'TIA_Bridge2\dev\qa-b01a-20261025'))
$existingOutput = $outputRoot
while (-not (Test-Path -LiteralPath $existingOutput)) { $existingOutput = [IO.Path]::GetDirectoryName($existingOutput) }
$paths += $existingOutput
$chain = @{}
foreach ($path in $paths) {
    $directory = Get-Item -LiteralPath $path -Force
    while ($null -ne $directory) {
        if (-not $chain.ContainsKey($directory.FullName)) { $chain[$directory.FullName] = [ordered]@{path=$directory.FullName; reparse=($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0} }
        $directory = $directory.Parent
    }
}
$gitMonitor = (& git -C $repoRoot fsmonitor--daemon status 2>&1 | Out-String).Trim()
$gitMonitorExit = $LASTEXITCODE
$result = [ordered]@{schema_version='1.0'; started_at=$started.ToString('o'); ended_at=[DateTimeOffset]::UtcNow.ToString('o'); observations=$rows; git_activity=$activity; git_fsmonitor_status=$gitMonitor; git_fsmonitor_exit=$gitMonitorExit; git_core_fsmonitor=((& git -C $repoRoot config --show-origin --get core.fsmonitor) | Out-String).Trim(); paths=@($chain.Values); volumes=@(Get-Volume -DriveLetter C,X | Select-Object DriveLetter,FileSystemType,DriveType,HealthStatus,Size,SizeRemaining); cpu=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfLogicalProcessors,LoadPercentage); os=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,TotalVisibleMemorySize,FreePhysicalMemory); disks=@(Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Select-Object Name,CurrentDiskQueueLength,DiskBytesPersec,AvgDisksecPerRead,AvgDisksecPerWrite); top_io=@(Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object {$_.IDProcess -ne 0} | Sort-Object IODataBytesPersec -Descending | Select-Object -First 12 Name,IDProcess,IOReadBytesPersec,IOWriteBytesPersec,PercentProcessorTime); antivirus=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | Select-Object displayName,productState); dotnet=((& dotnet --info) | Out-String); python=((& py -3 --version 2>&1) | Out-String).Trim()}
$result.ended_at = [DateTimeOffset]::UtcNow.ToString('o')
$result.script_sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (@($chain.Values | Where-Object { $_.reparse }).Count -ne 0) { throw 'Reparse path detected; output not written.' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$outputFile = Join-Path $outputRoot ('environment-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($outputFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $bytes = [Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 9)); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
Write-Output $outputFile
