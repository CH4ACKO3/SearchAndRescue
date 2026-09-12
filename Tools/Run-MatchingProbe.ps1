param(
 [Parameter(Mandatory=$true)][string]$GameExe,
 [Parameter(Mandatory=$true)][string]$RuntimeDir,
 [Parameter(Mandatory=$true)][string]$SaveData,
 [Parameter(Mandatory=$true)][string]$BaselineProfile,
 [Parameter(Mandatory=$true)][string]$BasePackageId,
 [string]$LegacyAssembly,
 [string]$AuditAssembly
)
$ErrorActionPreference='Stop'
$mod=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
dotnet build (Join-Path $PSScriptRoot 'MatchingProbe/MatchingProbe.csproj') -c Release --nologo
if($LASTEXITCODE){throw 'Matching probe build failed'}
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $mod -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods sarg.smartspeed | Out-Null
$r=(Resolve-Path $RuntimeDir).Path;$s=(Resolve-Path $SaveData).Path
$id='ch4acko3.matching'+[Guid]::NewGuid().ToString('N')
foreach($f in @("$r/About/About.xml","$s/Config/ModsConfig.xml")){(Get-Content -LiteralPath $f -Raw).Replace('ch4acko3.sarbenchmarkruntime',$id) | Set-Content -LiteralPath $f}
New-Item -ItemType Directory "$s/Saves" | Out-Null
foreach($name in @('d5p34h4r10_Initial','d20p136h16r8_Initial','d40p272h32r9_Initial')) {
 (Get-Content -LiteralPath "$BaselineProfile/Saves/$name.rws" -Raw).Replace($BasePackageId,$id) | Set-Content -LiteralPath "$s/Saves/$name.rws"
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MatchingProbe/bin/Release/net48/MatchingProbe.dll') -Destination "$r/Assemblies/"
if($LegacyAssembly) {
 Copy-Item -LiteralPath $LegacyAssembly -Destination "$r/Assemblies/SearchAndRescue.dll"
 'legacy' | Set-Content "$s/legacy.txt"
}
Get-FileHash -LiteralPath "$r/Assemblies/SearchAndRescue.dll" | ConvertTo-Json | Set-Content "$s/assembly-hash.json"
if($AuditAssembly){Copy-Item -LiteralPath $AuditAssembly -Destination "$r/Assemblies/"}
$p=Start-Process -FilePath (Resolve-Path $GameExe).Path -ArgumentList @('-batchmode','-quicktest','-screen-width','1280','-screen-height','720',('-savedatafolder="'+$s+'"'),'-logFile',('"'+$s+'/Player.log"')) -WindowStyle Hidden -PassThru
$p.Id | Set-Content "$s/pid.txt"
[pscustomobject]@{pid=$p.Id;profile=$s}
