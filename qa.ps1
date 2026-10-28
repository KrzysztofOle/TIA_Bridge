# Python 3.12+; no environment activation or package installation.
$ErrorActionPreference = 'Stop'
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3.12 -B "$PSScriptRoot/scripts/qa.py" --config "$PSScriptRoot/qa.project.json" @args
} elseif (Get-Command python3 -ErrorAction SilentlyContinue) {
    & python3 -B "$PSScriptRoot/scripts/qa.py" --config "$PSScriptRoot/qa.project.json" @args
} else {
    Write-Error 'Install Python 3.12+ and rerun .\qa.ps1 doctor. See docs/QA_RUNNER.md.'
    exit 2
}
exit $LASTEXITCODE
