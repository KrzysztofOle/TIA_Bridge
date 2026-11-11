param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Build directory must be new'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$vendor='C:\Program Files\Siemens\Automation\Portal V19'
$source=Join-Path $PSScriptRoot 'recipe_editor_v19.cs'
$exe=Join-Path $OutputDirectory 'RecipeEditorV19.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /platform:x64 /target:exe /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$vendor\PublicAPI\V19\Siemens.Engineering.dll" "/reference:$vendor\PublicAPI\V19\Siemens.Engineering.Hmi.dll" "/reference:$vendor\Bin\PublicAPI\Siemens.Engineering.Contract.dll" "/out:$exe" $source
if($LASTEXITCODE -ne 0){throw 'Build failed'}
@($source,$exe,"$vendor\PublicAPI\V19\Siemens.Engineering.dll","$vendor\PublicAPI\V19\Siemens.Engineering.Hmi.dll")|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}}|ConvertTo-Json|Set-Content (Join-Path $OutputDirectory 'build.json')
Write-Output $exe
