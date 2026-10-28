param(
 [Parameter(Mandatory)][string]$GameDir,
 [Parameter(Mandatory)][string]$HarmonyDll,
 [string]$Dotnet='dotnet'
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!(Test-Path -LiteralPath (Join-Path $GameDir 'Assembly-CSharp.dll'))){throw 'Supply the RimWorld 1.7 game root, containing Assembly-CSharp.dll'}
if(!(Test-Path -LiteralPath $HarmonyDll)){throw 'Supply the RimWorld 1.7 Harmony DLL from your installation'}
$sdkRows=& $Dotnet --list-sdks
if($LASTEXITCODE){throw '.NET SDK is required'}
$sdk=@($sdkRows | ForEach-Object {if($_ -match '^(\d+\.\d+\.\d+) \[(.*)\]$'){[pscustomobject]@{Version=[version]$matches[1];Folder=Join-Path $matches[2] $matches[1]}}} | Sort-Object Version -Descending)[0]
if(!$sdk){throw 'Could not locate an installed .NET SDK'}
$compiler=Join-Path $sdk.Folder 'Roslyn\bincore\csc.dll'
$metadata=Get-Content -LiteralPath (Join-Path $root 'Source-Snapshot.json') -Raw | ConvertFrom-Json
$out=Join-Path $root ('.build\1.7\'+$metadata.assemblyName+'.dll')
[void][IO.Directory]::CreateDirectory((Split-Path $out -Parent))
$refs=@(Get-ChildItem -LiteralPath $GameDir -File -Filter 'System*.dll' | Where-Object {try{[void][Reflection.AssemblyName]::GetAssemblyName($_.FullName);$true}catch{$false}} | ForEach-Object FullName)
foreach($name in @('mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll','Microsoft.Win32.Primitives.dll','Assembly-CSharp.dll','UnityStubs.dll','Waylith.dll')){$refs+=Join-Path $GameDir $name}
$refs+=[IO.Path]::GetFullPath($HarmonyDll)
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'Source') -Recurse -File -Filter '*.cs' | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | ForEach-Object FullName)
if(Test-Path -LiteralPath (Join-Path $root 'Shared')){$sources+=Get-ChildItem -LiteralPath (Join-Path $root 'Shared') -Recurse -File -Filter '*.cs' | ForEach-Object FullName}
if(!$sources.Count){throw 'No source files found'}
& $Dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest /deterministic+ /target:library ('/out:'+$out) @($refs | Sort-Object -Unique | ForEach-Object {'/reference:'+$_}) $sources
if($LASTEXITCODE){throw '1.7 compilation failed. No game or installed mod was changed.'}
Write-Output ('Compiled '+$metadata.assemblyName+' to .build/1.7. Native playtesting is a separate step.')
