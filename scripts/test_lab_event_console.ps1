# Offline only. Windows PowerShell 5.1 x64 and system .NET Framework csc, no new packages.
# BaselineDirectory: preserved LAB-03L sources from the pre-LAB-03M evidence snapshot.
param(
    [string]$LabRoot = 'X:\TESTY_TIA_Bridge',
    [Parameter(Mandatory=$true)][string]$BaselineDirectory
)
$ErrorActionPreference = 'Stop'
$run = Join-Path $LabRoot ('evidence\LAB-03M-offline-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $PSScriptRoot 'lab_event_console_tests.cs'
$exe = Join-Path $run 'LabEventConsoleTests.exe'
& $csc /nologo /platform:x64 /target:exe "/out:$exe" /reference:System.Web.Extensions.dll (Join-Path $LabRoot 'src\LabEventExport.cs') (Join-Path $LabRoot 'src\LabEventView.cs') (Join-Path $LabRoot 'src\LabEventConsole.cs') $test
if ($LASTEXITCODE -ne 0) { throw 'LAB-03M tests compilation failed' }
& $exe $run | Tee-Object -FilePath (Join-Path $run 'console.txt')
$testExit = $LASTEXITCODE
$baselineExe = Join-Path $run 'Lab03LBaseline.exe'
& $csc /nologo /platform:x64 /target:exe /define:BASELINE "/out:$baselineExe" /reference:System.Web.Extensions.dll (Join-Path $BaselineDirectory 'LabEventExport.cs') (Join-Path $BaselineDirectory 'LabEventView.cs') $test
if ($LASTEXITCODE -ne 0) { throw 'LAB-03L baseline compilation failed' }
$before = Join-Path $run 'before'
$after = Join-Path $run 'after'
New-Item -ItemType Directory -Path $before,$after | Out-Null
& $baselineExe $before --fixture
if ($LASTEXITCODE -ne 0) { throw 'LAB-03L fixture failed' }
& $exe $after --fixture
if ($LASTEXITCODE -ne 0) { throw 'LAB-03M fixture failed' }
$compared = @()
Get-ChildItem -LiteralPath (Join-Path $before 'fixture') -File -Recurse | Where-Object Name -ne 'manifest.json' | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $before 'fixture').Length + 1)
    $actual = Join-Path (Join-Path $after 'fixture') $relative
    if (-not (Test-Path -LiteralPath $actual) -or (Get-FileHash -LiteralPath $_.FullName).Hash -cne (Get-FileHash -LiteralPath $actual).Hash) { throw "Export artifact changed: $relative" }
    $compared += $relative
}
$baselinePaths = @(Get-ChildItem -LiteralPath (Join-Path $before 'fixture') -File -Recurse)
$currentPaths = @(Get-ChildItem -LiteralPath (Join-Path $after 'fixture') -File -Recurse)
if ($baselinePaths.Count -ne $currentPaths.Count) { throw 'Artifact file set changed' }
# Only assembly hash differs in manifest; no tool version/schema change is needed for console presentation.
$baselineManifest = Get-Content (Join-Path $before 'fixture\manifest.json') -Raw | ConvertFrom-Json
$currentManifest = Get-Content (Join-Path $after 'fixture\manifest.json') -Raw | ConvertFrom-Json
$baselineManifest.tool_sha256 = 'BUILD_HASH'
$currentManifest.tool_sha256 = 'BUILD_HASH'
if (($baselineManifest | ConvertTo-Json -Depth 30 -Compress) -cne ($currentManifest | ConvertTo-Json -Depth 30 -Compress)) { throw 'Manifest changed beyond binary hash' }
# Check every non-presentation statement of the actual vendor lifecycle, including all return branches.
$expectedVendor = [IO.File]::ReadAllText((Join-Path $BaselineDirectory 'LabEventVendor.cs')).Replace("`r`n","`n")
$expectedVendor = $expectedVendor.Replace('            var options = new Dictionary<string, string>(StringComparer.Ordinal);', "            LabEventConsole.Initialize();`n            var options = new Dictionary<string, string>(StringComparer.Ordinal);")
$expectedVendor = [regex]::Replace($expectedVendor, 'Console\.Error\.WriteLine\("EXPORT EVENTS: read-only Attach\.[^\n]+\);', 'LabEventConsole.AttachNotice(Console.Error);')
$expectedVendor = $expectedVendor.Replace('Console.WriteLine("EXPORT EVENTS " + result.status + "; objects=" + result.objects.Count + "; handlers=" + result.handlers.Count + "; observations=" + result.observations.Count + "; output=" + output);', 'LabEventConsole.Summary(Console.Out, result, output);')
$oldFailure = @'
Console.Error.WriteLine("EXPORT EVENTS FAILED; exception_type=" + ex.GetType().FullName + "; code=" + (ex is LabEventFailure ? ((LabEventFailure)ex).Code : "UNSPECIFIED") + "; hresult=" + ex.HResult
                + "; output=" + output + "; incomplete artifacts must not be treated as complete");
'@
$expectedVendor = $expectedVendor.Replace($oldFailure.Replace("`r`n","`n"), 'LabEventConsole.Failure(Console.Error, ex, output);')
$actualVendor = [IO.File]::ReadAllText((Join-Path $LabRoot 'src\LabEventVendor.cs')).Replace("`r`n","`n")
# LAB-03N changes only destination validation and run path composition, outside Siemens lifecycle.
$expectedVendor = $expectedVendor.Replace('output = Path.Combine(root, runId);', 'output = LabEventOutput.RunDirectory(root, runId);')
$expectedVendor = [regex]::Replace($expectedVendor, '(?s)(private static string ValidateEventOutput\(string argument, string projectDirectory\)\n    \{).*?\n    \}', '$1' + "`n        return LabEventOutput.Root(argument);`n    }")
# LAB-03O: exact CLI/default mode and publisher dispatch edits; Siemens lifecycle stays byte-identical.
$expectedVendor = $expectedVendor.Replace('"--project", "--output", "--hmi" }.Contains(args[i])', '"--project", "--output", "--hmi", "--mode" }.Contains(args[i])')
$expectedVendor = $expectedVendor.Replace('if (options.Count != 3) throw new ArgumentException("EXPORT_EVENTS_REQUIRES_PROJECT_OUTPUT_HMI");', 'if (!new[] { "--project", "--output", "--hmi" }.All(options.ContainsKey)) throw new ArgumentException("EXPORT_EVENTS_REQUIRES_PROJECT_OUTPUT_HMI");' + "`n            " + 'string mode = LabEventSync.Mode(options.ContainsKey("--mode") ? options["--mode"] : null);')
$expectedVendor = $expectedVendor.Replace('output = LabEventOutput.RunDirectory(root, runId);', 'output = mode == "snapshot" ? LabEventOutput.RunDirectory(root, runId) : root;')
$expectedVendor = $expectedVendor.Replace('LabEventArtifacts.Write(output, result);', 'string readStatus = result.status;' + "`n            " + 'Console.WriteLine("READ RESULT; status=" + readStatus);' + "`n            " + 'string artifactStatus;' + "`n            " + 'if (mode == "snapshot") LabEventArtifacts.Write(output, result);' + "`n            " + 'if (mode == "snapshot") artifactStatus = result.status;' + "`n            " + 'else artifactStatus = LabEventSync.Write(output, result);' + "`n            " + 'Console.WriteLine("PUBLICATION COMPLETE; mode=" + mode + "; read_status=" + readStatus + "; artifact_status=" + artifactStatus);')
$expectedVendor = $expectedVendor.Replace('return result.status == "COMPLETE_FOR_PUBLIC_SCOPE" ? 0 : 2;', 'return artifactStatus == "COMPLETE_FOR_PUBLIC_SCOPE" ? 0 : 2;')
$expectedVendor = $expectedVendor.Replace('ENCODED_DEVICE_ITEM_PATH");', 'ENCODED_DEVICE_ITEM_PATH [--mode overwrite|snapshot]");')
if ($expectedVendor -cne $actualVendor) { throw 'Vendor lifecycle changed beyond console, LAB-03N paths and LAB-03O CLI/publication dispatch' }
$ErrorActionPreference = 'Continue'
$invalid = & (Join-Path $LabRoot 'bin\LabExporter.exe') export-events 2>&1
$invalidExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($invalidExit -ne 1 -or ($invalid -join "`n") -notmatch 'ERROR EXPORT EVENTS FAILED' -or ($invalid -join "`n") -match 'read-only Attach') { throw 'Invalid CLI exit/pre-Attach failure changed' }
[ordered]@{status='PASS';unchanged_artifacts=$compared;manifest_change='tool_sha256 only';vendor_change='console, LAB-03N paths and LAB-03O CLI/publication only';exit_codes='0/2/1 semantics preserved; artifact omissions return 2; invalid CLI exit 1 executed without Attach';invalid_cli_output=@($invalid | ForEach-Object ToString)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'compatibility.json') -Encoding UTF8
Write-Output "LAB-03M compatibility PASS: $($compared.Count) identical artifacts. Evidence: $run"
exit $testExit
