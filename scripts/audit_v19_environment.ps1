param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$ProjectRoot
)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$appRoot = [IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'TIA_Bridge2', 'dev', 'x01') + '\'
if (-not $outputRoot.StartsWith($appRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-Content -LiteralPath (Join-Path $outputRoot '.tia-bridge2-storage-v1') -Raw) -ne "TIA_BRIDGE2_STORAGE_V1`n") { throw 'Owned X-01 evidence root required.' }
$outputDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($outputRoot))
if ($outputDrive.DriveType -ne 'Fixed' -or $outputDrive.DriveFormat -ne 'NTFS') { throw 'Local fixed NTFS required.' }
for ($item = $outputRoot; $item; $item = [IO.Path]::GetDirectoryName($item)) {
    if ([IO.File]::GetAttributes($item) -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point in evidence path.' }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$openness = Get-LocalGroup -Name 'Siemens TIA Openness'
$portalRoot = 'C:\Program Files\Siemens\Automation\Portal V19'
$dlls = @('PublicAPI\V19\Siemens.Engineering.dll', 'PublicAPI\V19\Siemens.Engineering.Hmi.dll', 'Bin\PublicAPI\Siemens.Engineering.Contract.dll', 'Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll') | ForEach-Object {
    $file = Join-Path $portalRoot $_
    [ordered]@{path=$file;assembly=[Reflection.AssemblyName]::GetAssemblyName($file).FullName;file_version=(Get-Item -LiteralPath $file).VersionInfo.FileVersion;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant();authenticode=(Get-AuthenticodeSignature -LiteralPath $file).Status.ToString()}
}
$processes = @(Get-CimInstance Win32_Process -Filter "Name='Siemens.Automation.Portal.exe'" | ForEach-Object {
    $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
    [ordered]@{pid=$_.ProcessId;created_at=$_.CreationDate.ToUniversalTime().ToString('o');executable=$_.ExecutablePath;owner_sid=$owner.Sid;owner_query_result=$owner.ReturnValue}
})
$projects = @(Get-ChildItem -LiteralPath $ProjectRoot -File -Filter '*.ap19' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName;size=$_.Length;mtime_utc=$_.LastWriteTimeUtc.ToString('o')}
})
$projectDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($ProjectRoot)))
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
$referencePack = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\mscorlib.dll'
$report = [ordered]@{
    observed_at_utc=[DateTimeOffset]::UtcNow.ToString('o');windows=[Environment]::OSVersion.VersionString;x64=[Environment]::Is64BitOperatingSystem
    user=$identity.Name;sid=$identity.User.Value;openness_group=$openness.SID.Value;effective_openness_member=($identity.Groups.Value -contains $openness.SID.Value)
    framework_release=$framework.Release;framework_version=$framework.Version;net48_reference_pack_nuget=(Test-Path -LiteralPath $referencePack)
    dotnet_sdk=(& dotnet --version);portal_product_version=(Get-Item -LiteralPath (Join-Path $portalRoot 'Bin\Siemens.Automation.Portal.exe')).VersionInfo.ProductVersion
    public_api=$dlls;public_xml_sha256=(Get-FileHash -LiteralPath (Join-Path $portalRoot 'PublicAPI\V19\Siemens.Engineering.xml') -Algorithm SHA256).Hash.ToLowerInvariant()
    license_manager_status=(Get-Service -Name almservice).Status.ToString();license_entitlements='NOT_VERIFIED: service/install is not entitlement proof; no license keys inspected'
    portal_processes=$processes;projects=$projects;project_drive=[ordered]@{root=$projectDrive.Name;type=$projectDrive.DriveType.ToString();filesystem=$projectDrive.DriveFormat}
    storage_drive=[ordered]@{root=$outputDrive.Name;type=$outputDrive.DriveType.ToString();filesystem=$outputDrive.DriveFormat}
    performed_project_writes=$false;performed_attach=$false
}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($report | ConvertTo-Json -Depth 8))
$file = [IO.FileStream]::new((Join-Path $outputRoot 'environment.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose(); $identity.Dispose() }
Write-Output 'ENVIRONMENT_RECORDED: read-only, no project writes'
