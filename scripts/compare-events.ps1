# Python 3.12 (py launcher), stdlib only. Paths are data; no Siemens calls.
param([string]$Before, [string]$After, [string]$Output)
$ErrorActionPreference = 'Stop'
if (-not $Before) { $Before = Read-Host 'Before events.json (existing file)' }
if (-not $After) { $After = Read-Host 'After events.json (existing file)' }
if (-not $Output) { $Output = Read-Host 'Report parent directory' }
& py -3 -B (Join-Path $PSScriptRoot 'compare_events.py') compare-events --before $Before --after $After --output $Output
exit $LASTEXITCODE
