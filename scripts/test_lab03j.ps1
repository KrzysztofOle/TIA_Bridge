# Windows PowerShell 5.1 x64; built lab EXE. Offline only; no Siemens calls.
# Uses the same reflection and result-report pattern as LAB-03I/03H.
param([string]$LabRoot = 'X:\TESTY_TIA_Bridge')
$ErrorActionPreference = 'Stop'
$run = Join-Path $LabRoot ('evidence\LAB-03J-offline-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$type = [Reflection.Assembly]::LoadFrom((Join-Path $LabRoot 'bin\LabExporter.exe')).GetType('LabExporter',$true)
$results = New-Object 'Collections.Generic.List[object]'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class Lab03JProducers {
    public static bool Called;
    public static FileInfo[] Empty() { Called=true; return new FileInfo[0]; }
    public static FileInfo[] Sharing() { throw new IOException("synthetic sharing violation", unchecked((int)0x80070020)); }
}
'@
function Call([string]$Name,[object[]]$Arguments) {
    $method=$type.GetMethod($Name,[Reflection.BindingFlags]'NonPublic,Static')
    if (-not $method) { throw "Missing method: $Name" }
    $parameters=$method.GetParameters()
    for($i=0;$i -lt $Arguments.Length;$i++) { if($parameters[$i].ParameterType -eq [string]){$Arguments[$i]=[string]$Arguments[$i]} }
    try { $method.Invoke($null,$Arguments) } catch { if($_.Exception.InnerException){throw $_.Exception.InnerException};throw }
}
function Assert([bool]$Value,[string]$Message) { if(-not $Value){throw $Message} }
function Test([string]$Name,[scriptblock]$Action) {
    try { & $Action; $results.Add([ordered]@{name=$Name;status='PASS'}) }
    catch { $results.Add([ordered]@{name=$Name;status='FAIL';error=($_.Exception.ToString()+"`n"+$_.ScriptStackTrace)}) }
}
function Fixture {
    $path=Join-Path $run ([guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $path|Out-Null
    [IO.File]::WriteAllText((Join-Path $path 'Synthetic.hmi.js'),'synthetic code sentinel')
    [IO.File]::WriteAllText((Join-Path $path 'Synthetic.hmi.yml'),'opaque synthetic metadata')
    return $path
}
function Returned([string]$Path) { return ,([string[]]@(Join-Path $Path 'Synthetic.hmi.js';Join-Path $Path 'Synthetic.hmi.yml')) }
function Validate([string]$Path,[string[]]$Files) { $null=Call 'ValidateNativeExportResult' @('Synthetic',$Path,$Files) }
function Reject([string]$Path,[string[]]$Files,[string]$Reason) {
    $errorText=$null;try {Validate $Path $Files} catch {$errorText=$_.Exception.ToString()}
    Assert ($null -ne $errorText -and $errorText -match $Reason) "Expected $Reason; got $errorText"
}
Test 'valid_exact_native_pair' { $p=Fixture;Validate $p (Returned $p) }
Test 'missing_js_rejected' { $p=Fixture;$f=Returned $p;[IO.File]::Delete($f[0]);Reject $p $f 'NATIVE_' }
Test 'missing_yaml_rejected' { $p=Fixture;$f=Returned $p;[IO.File]::Delete($f[1]);Reject $p $f 'NATIVE_' }
Test 'additional_technical_file_rejected' { $p=Fixture;[IO.File]::WriteAllText((Join-Path $p 'extra.tmp'),'extra');Reject $p (Returned $p) 'NATIVE_' }
Test 'unexpected_subdirectory_rejected' { $p=Fixture;New-Item -ItemType Directory -Path (Join-Path $p 'unexpected')|Out-Null;Reject $p (Returned $p) 'NATIVE_' }
Test 'stale_destination_blocks_before_producer' {
    $p=Fixture;[Lab03JProducers]::Called=$false
    $producer=[Delegate]::CreateDelegate([Func[IO.FileInfo[]]],[Lab03JProducers].GetMethod('Empty'))
    $caught=$null;try {$null=Call 'ObserveNativeExport' @('Synthetic',$p,$producer)} catch {$caught=$_.Exception.ToString()}
    Assert ($caught -match 'NATIVE_DESTINATION_NOT_EMPTY' -and -not [Lab03JProducers]::Called) 'stale directory reached producer'
}
Test 'wrong_module_filenames_rejected' {
    $p=Fixture;[IO.File]::Move((Join-Path $p 'Synthetic.hmi.js'),(Join-Path $p 'Other.hmi.js'));Reject $p ([string[]]@(Join-Path $p 'Other.hmi.js';Join-Path $p 'Synthetic.hmi.yml')) 'NATIVE_'
}
Test 'empty_directory_and_api_result_rejected' {
    $p=Join-Path $run ([guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $p|Out-Null;Reject $p ([string[]]@()) 'NATIVE_'
}
Test 'ambiguous_duplicate_pairs_rejected' {
    $p=Fixture;$child=Join-Path $p 'other';New-Item -ItemType Directory -Path $child|Out-Null
    [IO.File]::WriteAllText((Join-Path $child 'Synthetic.hmi.js'),'second');[IO.File]::WriteAllText((Join-Path $child 'Synthetic.hmi.yml'),'second')
    Reject $p (Returned $p) 'NATIVE_'
}
Test 'returned_paths_must_equal_actual_pair' {
    $p=Fixture;$other=Fixture;Reject $p (Returned $other) 'NATIVE_';Reject $p ([string[]]@((Join-Path $p 'Synthetic.hmi.js'),(Join-Path $p 'Synthetic.hmi.js'))) 'NATIVE_'
}
Test 'diagnostics_capture_structure_and_omit_source_content' {
    $p=Fixture;$child=Join-Path $p 'nested';New-Item -ItemType Directory -Path $child|Out-Null;[IO.File]::WriteAllText((Join-Path $child 'technical.tmp'),'business-content-must-not-leak')
    $snapshot=Call 'NativeTreeSnapshot' @($p);$json=$snapshot|ConvertTo-Json -Depth 12
    Assert ($snapshot.file_count -eq 3 -and $snapshot.directories.Count -eq 1 -and $json -match 'technical.tmp' -and $json -notmatch 'business-content-must-not-leak|synthetic code sentinel') 'snapshot incomplete or code leaked'
}
Test 'failure_report_has_precise_reason_and_windows_error' {
    $p=Join-Path $run ([guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $p|Out-Null
    $producer=[Delegate]::CreateDelegate([Func[IO.FileInfo[]]],[Lab03JProducers].GetMethod('Sharing'))
    $before=@(Get-ChildItem (Join-Path $LabRoot 'evidence') -Filter LAB-03J-native-*.json |ForEach-Object FullName)
    $caught=$null;try{$null=Call 'ObserveNativeExport' @('Synthetic',$p,$producer)}catch{$caught=$_.Exception.ToString()}
    $new=@(Get-ChildItem (Join-Path $LabRoot 'evidence') -Filter LAB-03J-native-*.json |Where-Object {$_.FullName -notin $before} |Where-Object {(Get-Content $_.FullName -Raw|ConvertFrom-Json).directory -eq $p})
    Assert ($caught -and $new.Count -eq 1) 'failure or report missing'
    $r=Get-Content $new[0].FullName -Raw|ConvertFrom-Json
    Assert ($r.status -eq 'FAIL' -and $r.module_name -eq 'Synthetic' -and $r.directory -eq $p -and $r.before.file_count -eq 0 -and $r.after.file_count -eq 0 -and $r.expected.root_files.Count -eq 2 -and $r.win32_error -eq 32 -and $r.exception_type -eq 'System.IO.IOException') 'diagnostic fields incomplete'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Test 'backup_archive_integrity_and_exact_ap19_binding' {
    $p=Join-Path $run 'backup-source';New-Item -ItemType Directory -Path $p|Out-Null
    $ap19=Join-Path $p 'Synthetic.ap19';[IO.File]::WriteAllText($ap19,'synthetic project')
    $zip=Join-Path $run 'valid.zap19';[IO.Compression.ZipFile]::CreateFromDirectory($p,$zip)
    $r=Call 'VerifyNativeArchive' @($zip,$ap19)
    Assert ($r.status -eq 'PASS_ARCHIVE_READ_AND_AP19_BINDING' -and $r.entries.Count -eq 1) 'backup verification failed'
}
Test 'wrong_ap19_backup_rejected' {
    $caught=$null;try {$null=Call 'VerifyNativeArchive' @((Join-Path $run 'valid.zap19'),(Join-Path $run 'Other.ap19'))}catch{$caught=$_.Exception.ToString()}
    Assert ($caught -match 'BACKUP_AP19_BINDING_INVALID') 'wrong AP19 accepted'
}
Test 'corrupt_backup_rejected' {
    $zip=Join-Path $run 'corrupt.zap19';[IO.File]::WriteAllText($zip,'not a zip archive')
    $caught=$null;try {$null=Call 'VerifyNativeArchive' @($zip,'Synthetic.ap19')}catch{$caught=$_.Exception.ToString()}
    Assert ($null -ne $caught) 'corrupt backup accepted'
}
$failed=@($results|Where-Object status -eq FAIL).Count
$report=[ordered]@{task='LAB-03J';status=$(if($failed -eq 0){'PASS'}else{'FAIL'});passed=$results.Count-$failed;failed=$failed;results=$results.ToArray();binary_sha256=(Get-FileHash (Join-Path $LabRoot 'bin\LabExporter.exe')).Hash;run=$run}
$report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding utf8
$results|ForEach-Object{Write-Output ($_.status+' '+$_.name);if($_.error){Write-Output $_.error}}
Write-Output ('REPORT='+$run+'\results.json')
if($failed -ne 0){exit 1}
