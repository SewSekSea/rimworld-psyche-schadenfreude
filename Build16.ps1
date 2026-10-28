param([Parameter(Mandatory)][string]$GameDir,[Parameter(Mandatory)][string]$HarmonyDll,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$managed=Join-Path $GameDir 'RimWorldWin64_Data\Managed'
if(!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'Supply your legacy RimWorld 1.6 installation. Its managed reference assemblies are not bundled.'}
if(!(Test-Path -LiteralPath $HarmonyDll)){throw 'Supply the matching Harmony assembly'}
$project=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Recurse -File -Filter '*.csproj' | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'})
if($project.Count -ne 1){throw 'Expected one standalone project'}
& $Dotnet build $project[0].FullName -c Release ('-p:RimWorldDir='+$GameDir) ('-p:GameDir='+$GameDir) ('-p:HarmonyDir='+(Split-Path $HarmonyDll -Parent))
if($LASTEXITCODE){throw 'Legacy build failed'}
