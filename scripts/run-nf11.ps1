[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Preflight')]
    [switch] $Preflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'Run')]
    [switch] $Run,

    [Parameter(Mandatory = $true, ParameterSetName = 'Report')]
    [switch] $Report,

    [Parameter(ParameterSetName = 'Run')]
    [switch] $Smoke
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resilienceRoot = Join-Path $repoRoot '.tia-bridge/resilience'
$runner = Join-Path $repoRoot 'scripts/validate_offline.py'
$lockPath = Join-Path $resilienceRoot 'nf11-synthetic.active.json'

function Write-Check([string] $Name, [bool] $Passed, [string] $Detail) {
    $state = if ($Passed) { 'PASS' } else { 'BLOCKED' }
    Write-Host ('[{0}] {1}: {2}' -f $state, $Name, $Detail)
}

function Invoke-Nf11Preflight {
    $preflightId = [guid]::NewGuid().ToString('N')
    $preflightFolder = Join-Path (Join-Path $resilienceRoot 'launcher-preflight') $preflightId
    $null = New-Item -ItemType Directory -Path $preflightFolder -Force
    $blockers = [Collections.Generic.List[string]]::new()
    $checks = [ordered]@{}

    foreach ($path in @('TiaBridge.slnx', 'global.json', 'tests/resilience/matrix.json',
            'tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj',
            'tests/integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs',
            'scripts/validate_offline.py', 'scripts/d11_preflight.ps1', 'scripts/performance_evidence.py',
            'scripts/report_resilience.py', 'scripts/requirements-qa.txt')) {
        $exists = Test-Path -LiteralPath (Join-Path $repoRoot $path) -PathType Leaf
        $checks['file:' + $path] = $exists
        if (-not $exists) { $blockers.Add('Missing required harness file: ' + $path) }
    }

    $matrixPath = Join-Path $repoRoot 'tests/resilience/matrix.json'
    if (Test-Path -LiteralPath $matrixPath) {
        $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
        $criteriaCorrect = $matrix.nf11_synthetic_phase.required_seconds -eq 28800 -and
            $matrix.nf11_synthetic_phase.minimum_operations -eq 1000
        $checks['NF11 criteria unchanged'] = $criteriaCorrect
        if (-not $criteriaCorrect) { $blockers.Add('NF-11 matrix does not contain the canonical duration/operation minimum.') }
    }

    $python = Get-Command py.exe -ErrorAction SilentlyContinue
    $pythonVersion = $null
    if ($null -ne $python) {
        $versionCode = 'import sys; print("%d.%d.%d" % sys.version_info[:3])'
        $versionPayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($versionCode))
        $versionBootstrap = "exec(__import__('base64').b64decode('$versionPayload'))"
        $pythonOutput = & $python.Source -3 -c $versionBootstrap 2>&1
        if ($LASTEXITCODE -eq 0) { $pythonVersion = [string]($pythonOutput | Select-Object -Last 1) }
    }
    $pythonReady = $pythonVersion -match '^3\.12\.'
    $checks['Python 3.12'] = $pythonReady
    if (-not $pythonReady) { $blockers.Add('Python 3.12 via py -3 is unavailable.') }

    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    $dotnetVersion = $null
    if ($null -ne $dotnet) {
        $dotnetOutput = & $dotnet.Source --version 2>&1
        if ($LASTEXITCODE -eq 0) { $dotnetVersion = [string]($dotnetOutput | Select-Object -Last 1) }
    }
    $dotnetReady = $dotnetVersion -match '^10\.0\.4\d\d$'
    $checks['.NET SDK 10.0.4xx'] = $dotnetReady
    if (-not $dotnetReady) { $blockers.Add('.NET SDK selected by global.json is not 10.0.4xx.') }

    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    $checks['Git CLI'] = $null -ne $git
    if ($null -eq $git) { $blockers.Add('Git CLI is unavailable.') }

    $driveRoot = [IO.Path]::GetPathRoot($repoRoot)
    $drive = [IO.DriveInfo]::new($driveRoot)
    $repoNfts = $drive.IsReady -and $drive.DriveType -eq [IO.DriveType]::Fixed -and $drive.DriveFormat -eq 'NTFS'
    $checks['Repository local NTFS'] = $repoNfts
    if (-not $repoNfts) { $blockers.Add('Repository must be on a local fixed NTFS volume.') }

    $writeProbe = Join-Path $resilienceRoot ('.launcher-write-' + $preflightId)
    try {
        $null = New-Item -ItemType Directory -Path $resilienceRoot -Force
        $stream = [IO.File]::Open($writeProbe, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Dispose()
        [IO.File]::Delete($writeProbe)
        $checks['Evidence write access'] = $true
    }
    catch {
        $checks['Evidence write access'] = $false
        $blockers.Add('Cannot create a temporary evidence write probe: ' + $_.Exception.Message)
    }

    $lockExists = Test-Path -LiteralPath $lockPath
    $checks['NF-11 singleton lock free'] = -not $lockExists
    if ($lockExists) { $blockers.Add('NF-11 singleton lock exists; inspect its run and do not remove it automatically.') }
    $activeRunner = @(Get-CimInstance Win32_Process | Where-Object {
        $_.CommandLine -match 'validate_offline\.py' -and $_.CommandLine -match '--nf11-synthetic'
    })
    $checks['No NF-11 runner process'] = $activeRunner.Count -eq 0
    if ($activeRunner.Count -gt 0) { $blockers.Add('An NF-11 runner process is already visible.') }

    try {
        $process = Get-Process -Id $PID
        $metricReady = $process.WorkingSet64 -gt 0 -and $process.HandleCount -gt 0 -and
            $process.Threads.Count -gt 0 -and $process.TotalProcessorTime -ge [TimeSpan]::Zero -and
            @(Get-CimInstance Win32_Process -ErrorAction Stop).Count -gt 0
        $checks['CPU/RAM/handles/threads/process metrics'] = $metricReady
        if (-not $metricReady) { $blockers.Add('One or more required Windows process metrics are unavailable.') }
    }
    catch {
        $checks['CPU/RAM/handles/threads/process metrics'] = $false
        $blockers.Add('Windows process metrics are unavailable: ' + $_.Exception.Message)
    }

    $environmentFile = $null
    if ((Get-Command powershell.exe -ErrorAction SilentlyContinue) -and $repoNfts) {
        $environmentLog = Join-Path $preflightFolder 'd11-preflight.log'
        $preflightOutput = & powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $repoRoot 'scripts/d11_preflight.ps1') `
            -OutputDirectory $preflightFolder -Profile STRICT 2>&1
        $preflightExit = $LASTEXITCODE
        $preflightOutput | Out-File -LiteralPath $environmentLog -Encoding utf8
        if ($preflightExit -eq 0 -and $preflightOutput.Count -gt 0) {
            $environmentFile = [string]($preflightOutput | Select-Object -Last 1)
            if (Test-Path -LiteralPath $environmentFile) {
                $environment = Get-Content -LiteralPath $environmentFile -Raw | ConvertFrom-Json
                $environmentReady = $environment.status -in @('PASS', 'PASS_WITH_LOAD')
                $checks['Existing D-11 safety/environment preflight'] = $environmentReady
                if (-not $environmentReady) { $blockers.Add('Existing preflight BLOCKED: ' + ($environment.blockers -join ', ')) }
                $volumes = @($environment.volumes)
                $repoLetter = $driveRoot.TrimEnd(':', '\')
                $volume = @($volumes | Where-Object { $_.DriveLetter -eq $repoLetter }) | Select-Object -First 1
                $spaceReady = $null -ne $volume -and [long]$volume.SizeRemaining -ge 2GB
                $checks['At least 2 GiB free on repository volume'] = $spaceReady
                if (-not $spaceReady) { $blockers.Add('Repository volume has less than 2 GiB free or its free space is unavailable.') }
                $collisions = @($environment.bridge_processes)
                $noBridgeCollision = $collisions.Count -eq 0
                $checks['No colliding TiaBridge Host/Worker'] = $noBridgeCollision
                if (-not $noBridgeCollision) { $blockers.Add('Existing preflight found a TiaBridge Host/Worker process.') }
            }
            else { $blockers.Add('Existing D-11 preflight did not produce its JSON evidence file.') }
        }
        else {
            $checks['Existing D-11 safety/environment preflight'] = $false
            $blockers.Add('Existing D-11 preflight failed; see ' + $environmentLog)
        }
    }
    else {
        $checks['Existing D-11 safety/environment preflight'] = $false
        $blockers.Add('Windows PowerShell 5.1 or local NTFS is unavailable for the existing safety preflight.')
    }

    $dependencyLog = Join-Path $preflightFolder 'offline-build.log'
    $emptyFeed = Join-Path $preflightFolder 'empty-feed'
    $null = New-Item -ItemType Directory -Path $emptyFeed -Force
    $buildReady = $false
    if ($pythonReady -and $dotnetReady -and $checks['file:TiaBridge.slnx']) {
        Push-Location $repoRoot
        try {
            & $dotnet.Source restore TiaBridge.slnx --locked-mode --source $emptyFeed -p:NuGetAudit=false *> $dependencyLog
            $restoreExit = $LASTEXITCODE
            if ($restoreExit -eq 0) {
                & $dotnet.Source build TiaBridge.slnx -c Release --no-restore *> $dependencyLog
                $buildReady = $LASTEXITCODE -eq 0
            }
        }
        finally { Pop-Location }
    }
    $checks['Offline locked dependencies and current Release build'] = $buildReady
    if (-not $buildReady) { $blockers.Add('Locked offline restore or Release build failed; see ' + $dependencyLog) }

    $status = if ($blockers.Count -eq 0) { 'PASS' } else { 'BLOCKED' }
    $summary = [ordered]@{
        schema_version = '1.0'
        run_id = $preflightId
        scope = 'NF11_SYNTHETIC Host/FakeWorker only'
        status = $status
        checks = $checks
        blockers = @($blockers)
        python_version = $pythonVersion
        dotnet_sdk = $dotnetVersion
        environment_report = $environmentFile
        dependency_log = $dependencyLog
        checked_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $summaryPath = Join-Path $preflightFolder 'launcher-preflight.json'
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Host ("NF-11 preflight: {0}; evidence: {1}" -f $status, $preflightFolder)
    foreach ($entry in $checks.GetEnumerator()) { Write-Check $entry.Key ([bool]$entry.Value) ([string]$entry.Value) }
    foreach ($blocker in $blockers) { Write-Host ('BLOCKER: ' + $blocker) }
    return ($status -eq 'PASS')
}

function Show-Nf11Report {
    if (Test-Path -LiteralPath $lockPath) {
        $active = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        Write-Host 'NF-11: RUNNING (no final status yet; do not start another run).'
        Write-Host ('Run ID: ' + $active.run_id)
        Write-Host ('Full log: ' + (Join-Path $resilienceRoot ($active.run_id + '/validation/nf11-dotnet-test.log')))
        Write-Host ('Partial evidence: ' + (Join-Path $resilienceRoot $active.run_id))
        return 0
    }

    $candidates = foreach ($directory in (Get-ChildItem -LiteralPath $resilienceRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($directory.Name -notmatch '^[0-9a-f]{32}$') { continue }
        $runPath = Join-Path $directory.FullName 'validation/run.json'
        if (-not (Test-Path -LiteralPath $runPath)) { continue }
        try {
            $run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json
            if ($run.run_id -eq $directory.Name -and $run.scope -eq 'NF11_SYNTHETIC Host/FakeWorker only') {
                [pscustomobject]@{ Directory = $directory.FullName; Run = $run; Started = [DateTimeOffset]::Parse($run.started_at) }
            }
        }
        catch { }
    }
    $latest = $candidates | Sort-Object Started -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        Write-Host 'NF-11: NOT_RUN — no finalized NF-11 runner report exists.'
        return 2
    }

    $runId = $latest.Run.run_id
    $reportCode = @'
import json, os, sys
from pathlib import Path
root = Path(os.environ["TIA_NF11_REPORT_ROOT"]).resolve()
sys.path.insert(0, str(root / "scripts"))
import performance_evidence as pe
import report_resilience as rr
run_id = os.environ["TIA_NF11_REPORT_RUN_ID"]
folder = root / ".tia-bridge" / "resilience" / run_id
run = json.loads((folder / "validation" / "run.json").read_text(encoding="utf-8-sig"))
try:
    manifest = pe.verify_run(folder, run_id)
    integrity = {"status": "PASS", "files": len(manifest["files"]), "sha256": pe.digest(folder / "evidence-manifest.json")}
    phase = rr.nf11_synthetic_report(root, run_id)
    resources = {"samples": 0, "host_cpu_percent_mean": None, "host_cpu_percent_max": None,
                 "host_threads_first": None, "host_threads_last": None, "host_threads_max": None,
                 "machine_process_count_first": None, "machine_process_count_last": None,
                 "host_working_set_first_bytes": None, "host_working_set_last_bytes": None,
                 "host_handles_first": None, "host_handles_last": None}
    cpu = []
    samples_path = folder / "nf11-synthetic-samples.jsonl"
    if samples_path.exists():
        with samples_path.open(encoding="utf-8") as stream:
            for line in stream:
                row = json.loads(line)
                value = row.get("value", {})
                host = value.get("host", {})
                counts = value.get("process_counts", {})
                percent = value.get("cpu_percent_normalized_all_logical_processors")
                if isinstance(percent, (int, float)):
                    cpu.append(percent)
                threads = host.get("threads")
                processes = counts.get("machine")
                working_set = host.get("working_set_bytes")
                handles = host.get("handles")
                for key, item in (("host_threads_first", threads), ("machine_process_count_first", processes),
                                  ("host_working_set_first_bytes", working_set), ("host_handles_first", handles)):
                    if resources[key] is None:
                        resources[key] = item
                resources["host_threads_last"] = threads
                resources["machine_process_count_last"] = processes
                resources["host_working_set_last_bytes"] = working_set
                resources["host_handles_last"] = handles
                resources["host_threads_max"] = max(resources["host_threads_max"] or 0, threads or 0)
                resources["samples"] += 1
    if cpu:
        resources["host_cpu_percent_mean"] = sum(cpu) / len(cpu)
        resources["host_cpu_percent_max"] = max(cpu)
    result_path = folder / "nf11-synthetic-result.json"
    if result_path.exists():
        operation_result = json.loads(result_path.read_text(encoding="utf-8-sig"))
        phase["failed_operations"] = max(0, operation_result.get("accepted_operations", 0) - phase.get("completed_operations", 0))
    phase["resource_summary"] = resources
    phase["heartbeat_ipc"] = "NOT_RECORDED_BY_SYNTHETIC_HARNESS"
except Exception as error:
    integrity = {"status": "FAIL", "error": str(error)}
    phase = {"run_id": run_id, "NF11_SYNTHETIC": "BLOCKED", "NF11_LIVE": "NOT_RUN", "NF11_OVERALL": "BLOCKED", "blockers": ["Run evidence integrity failed."]}
print(json.dumps({"run": run, "phase": phase, "integrity": integrity}, ensure_ascii=False))
'@
    # Windows PowerShell 5.1 strips quotes from multiline native arguments.
    # Base64 keeps the existing reporter program intact across both PS editions.
    $encodedReport = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($reportCode))
    $reportBootstrap = "exec(__import__('base64').b64decode('$encodedReport'))"
    $env:TIA_NF11_REPORT_ROOT = $repoRoot
    $env:TIA_NF11_REPORT_RUN_ID = $runId
    try { $json = & py.exe -3 -B -c $reportBootstrap }
    finally {
        Remove-Item Env:\TIA_NF11_REPORT_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\TIA_NF11_REPORT_RUN_ID -ErrorAction SilentlyContinue
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'NF-11: BLOCKED — report validation failed.'
        return 1
    }
    $result = ($json -join "`n") | ConvertFrom-Json
    $run = $result.run
    $phase = $result.phase
    Write-Host 'NF-11 SYNTHETIC REPORT'
    Write-Host ('Run ID: {0}' -f $run.run_id)
    Write-Host ('Start: {0}; end: {1}' -f $run.started_at, $run.ended_at)
    Write-Host ('Runner status: {0}; test exit: {1}; integrity: {2}' -f $run.status, $run.test_exit_code, $result.integrity.status)
    Write-Host ('NF11_SYNTHETIC={0}; NF11_LIVE={1}; NF11_OVERALL={2}' -f $phase.NF11_SYNTHETIC, $phase.NF11_LIVE, $phase.NF11_OVERALL)
    if ($null -ne $phase.measured_seconds) {
        Write-Host ('Measured seconds: {0}; completed operations: {1}; failed/uncompleted operations: {2}; samples: {3}' -f $phase.measured_seconds, $phase.completed_operations, $phase.failed_operations, $phase.sample_count)
        Write-Host ('RAM growth MiB: {0}; handle growth fraction: {1}; cleanup: {2}' -f $phase.ram_growth_mib, $phase.handle_growth_fraction, $phase.cleanup_confirmed)
        Write-Host ('Host working set first/last bytes: {0}/{1}; handles first/last: {2}/{3}' -f $phase.resource_summary.host_working_set_first_bytes, $phase.resource_summary.host_working_set_last_bytes, $phase.resource_summary.host_handles_first, $phase.resource_summary.host_handles_last)
        Write-Host ('Host CPU mean/max %: {0}/{1}; threads first/last/max: {2}/{3}/{4}' -f $phase.resource_summary.host_cpu_percent_mean, $phase.resource_summary.host_cpu_percent_max, $phase.resource_summary.host_threads_first, $phase.resource_summary.host_threads_last, $phase.resource_summary.host_threads_max)
        Write-Host ('Machine processes first/last: {0}/{1}; heartbeat/IPC: {2}' -f $phase.resource_summary.machine_process_count_first, $phase.resource_summary.machine_process_count_last, $phase.heartbeat_ipc)
    }
    if ($phase.blockers) { foreach ($item in $phase.blockers) { Write-Host ('BLOCKER: ' + $item) } }
    Write-Host ('Run folder: ' + $latest.Directory)
    Write-Host ('Full log: ' + (Join-Path $latest.Directory 'validation/nf11-dotnet-test.log'))
    Write-Host ('Evidence manifest: ' + (Join-Path $latest.Directory 'evidence-manifest.json'))
    return $(if ($result.integrity.status -eq 'PASS') { 0 } else { 1 })
}

try {
    if ($PSCmdlet.ParameterSetName -eq 'Report') {
        $code = Show-Nf11Report
        exit $code
    }

    $ready = Invoke-Nf11Preflight
    if (-not $ready) { exit 2 }
    if ($PSCmdlet.ParameterSetName -eq 'Preflight') { exit 0 }

    $mode = if ($Smoke) { 'DIAGNOSTIC' } else { 'FULL' }
    if ($Smoke) { Write-Host 'SMOKE ONLY: this run cannot qualify NF-11.' }
    Push-Location $repoRoot
    try {
        & py.exe -3 -B $runner --nf11-synthetic --nf11-mode $mode
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    exit $code
}
catch {
    [Console]::Error.WriteLine(('NF-11 launcher: BLOCKED — ' + $_.Exception.Message))
    exit 1
}
