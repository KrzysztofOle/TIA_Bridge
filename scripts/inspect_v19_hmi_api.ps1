# Windows PowerShell 5.1 x64 / installed V19 only. Metadata inspection; no Attach.
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess -or $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run with Windows PowerShell 5.1 x64.'
}
$vendorRoot = 'C:\Program Files\Siemens\Automation\Portal V19'
$relativeFiles = @('Bin\PublicAPI\Siemens.Engineering.Contract.dll',
    'Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll',
    'PublicAPI\V19\Siemens.Engineering.dll', 'PublicAPI\V19\Siemens.Engineering.Hmi.dll')
$files = @()
foreach ($relative in $relativeFiles) {
    $path = Join-Path $vendorRoot $relative
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
    $files += [ordered]@{relative_path=$relative; identity=$assembly.FullName;
        sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$resolver = [ResolveEventHandler]{ param($sender,$eventArgs)
    $match = [AppDomain]::CurrentDomain.ReflectionOnlyGetAssemblies() |
        Where-Object {$_.FullName -eq $eventArgs.Name} | Select-Object -First 1
    if ($match) { return $match }
    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
}
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolver)
try {
    $api = [AppDomain]::CurrentDomain.ReflectionOnlyGetAssemblies() |
        Where-Object {$_.GetName().Name -eq 'Siemens.Engineering'}
    $types = @()
    foreach ($name in @('Siemens.Engineering.HmiUnified.HmiSoftware',
        'Siemens.Engineering.HmiUnified.Scripts.HmiScriptModule',
        'Siemens.Engineering.HmiUnified.Scripts.HmiScriptModuleComposition')) {
        $type = $api.GetType($name, $true)
        $types += [ordered]@{name=$name;
            properties=@($type.GetProperties() | ForEach-Object {$_.ToString()});
            methods=@($type.GetMethods() | Where-Object {$_.Name -in @('Export','GetEnumerator')} |
                ForEach-Object {$_.ToString()})}
    }
    $document = [ordered]@{schema_version='1.0'; task='HMI-JS-02'; api_verified='VERIFIED';
        evidence='REFLECTION_ONLY_NO_ATTACH'; files=$files; types=$types}
    $absolute = [IO.Path]::GetFullPath($OutputPath)
    $stream = [IO.File]::Open($absolute, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($document | ConvertTo-Json -Depth 10))
        $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true)
    } finally { $stream.Dispose() }
    $document | ConvertTo-Json -Depth 10
} finally { [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolver) }
