param(
 [Parameter(Mandatory=$true)][string]$GameExe,
 [Parameter(Mandatory=$true)][string]$RuntimeDir,
 [Parameter(Mandatory=$true)][string]$SaveData,
 [string]$BaseSave,
 [string]$BasePackageId
)
$ErrorActionPreference='Stop'
$mod=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
dotnet build (Join-Path $PSScriptRoot 'ScalingProbe/ScalingProbe.csproj') -c Release --nologo
if($LASTEXITCODE){throw 'Scaling probe build failed'}
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $mod -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods sarg.smartspeed | Out-Null
$r=(Resolve-Path $RuntimeDir).Path;$s=(Resolve-Path $SaveData).Path
$id='ch4acko3.scaling'+[Guid]::NewGuid().ToString('N')
foreach($f in @("$r/About/About.xml","$s/Config/ModsConfig.xml")){(Get-Content -LiteralPath $f -Raw).Replace('ch4acko3.sarbenchmarkruntime',$id) | Set-Content -LiteralPath $f}
if($BaseSave){
 if(!$BasePackageId){throw 'Specify the package ID used by BaseSave'}
 New-Item -ItemType Directory "$s/Saves" | Out-Null
 (Get-Content -LiteralPath $BaseSave -Raw).Replace($BasePackageId,$id) | Set-Content -LiteralPath "$s/Saves/ScalingBase.rws"
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ScalingProbe/bin/Release/net48/ScalingProbe.dll') -Destination "$r/Assemblies/"
$p=Start-Process -FilePath (Resolve-Path $GameExe).Path -ArgumentList @('-batchmode','-quicktest','-screen-width','1280','-screen-height','720',('-savedatafolder="'+$s+'"'),'-logFile',('"'+$s+'/Player.log"')) -WindowStyle Hidden -PassThru
$p.Id | Set-Content "$s/pid.txt"
[pscustomobject]@{pid=$p.Id;profile=$s}
