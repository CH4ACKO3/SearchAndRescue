param(
 [Parameter(Mandatory=$true)][string]$GameExe,
 [Parameter(Mandatory=$true)][string]$RuntimeDir,
 [Parameter(Mandatory=$true)][string]$SaveData,
 [Parameter(Mandatory=$true)][string]$BaselineProfile,
 [Parameter(Mandatory=$true)][string]$BasePackageId,
 [string]$ClinicalSave,
 [switch]$PerformanceOnly
)
$ErrorActionPreference='Stop'
if($ClinicalSave -and $PerformanceOnly){throw 'Choose ClinicalSave or PerformanceOnly, not both'}
$mod=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
dotnet build (Join-Path $mod 'Source/SearchAndRescue/SearchAndRescue.csproj') -c Release -p:UseReferencePackages=true --nologo
if($LASTEXITCODE){throw 'SAR build failed'}
dotnet build (Join-Path $PSScriptRoot 'CoordinationProbe/CoordinationProbe.csproj') -c Release --nologo
if($LASTEXITCODE){throw 'Coordination probe build failed'}
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $mod -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods sarg.smartspeed | Out-Null
$r=(Resolve-Path $RuntimeDir).Path;$s=(Resolve-Path $SaveData).Path
$id='ch4acko3.coordination'+[Guid]::NewGuid().ToString('N')
foreach($f in @("$r/About/About.xml","$s/Config/ModsConfig.xml")){(Get-Content -LiteralPath $f -Raw).Replace('ch4acko3.sarbenchmarkruntime',$id) | Set-Content -LiteralPath $f}
New-Item -ItemType Directory "$s/Saves" | Out-Null
foreach($name in @('d5p34h4r10_Initial','d20p136h16r8_Initial','d40p272h32r9_Initial')) {
 (Get-Content -LiteralPath "$BaselineProfile/Saves/$name.rws" -Raw).Replace($BasePackageId,$id) | Set-Content -LiteralPath "$s/Saves/$name.rws"
}
if($ClinicalSave){
 $clinicalXml=[xml](Get-Content -LiteralPath $ClinicalSave -Raw)
 $oldId=@($clinicalXml.savegame.meta.modIds.li | Where-Object { $_ -like 'ch4acko3.*' })
 if($oldId.Count -ne 1){throw 'Expected one SAR package in clinical save'}
 (Get-Content -LiteralPath $ClinicalSave -Raw).Replace($oldId[0],$id) | Set-Content "$s/Saves/d5p34h4r10_Initial.rws"
 'EmergencyAuto' | Set-Content "$s/clinical-only.txt"
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CoordinationProbe/bin/Release/net48/CoordinationProbe.dll') -Destination "$r/Assemblies/"
Get-FileHash -LiteralPath "$r/Assemblies/SearchAndRescue.dll" | ConvertTo-Json | Set-Content "$s/assembly-hash.json"
if($PerformanceOnly){'performance' | Set-Content "$s/performance-only.txt"}
$p=Start-Process -FilePath (Resolve-Path $GameExe).Path -ArgumentList @('-batchmode','-quicktest','-screen-width','1280','-screen-height','720',('-savedatafolder="'+$s+'"'),'-logFile',('"'+$s+'/Player.log"')) -WindowStyle Hidden -PassThru
$p.Id | Set-Content "$s/pid.txt"
[pscustomobject]@{pid=$p.Id;profile=$s}
