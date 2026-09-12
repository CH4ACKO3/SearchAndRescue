param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData,
    [switch]$Rh2
)
$ErrorActionPreference='Stop'
$modRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project=Join-Path $PSScriptRoot 'StandingTreatmentProbe/StandingTreatmentProbe.csproj'
dotnet build $project -c Release --nologo
if($LASTEXITCODE){throw 'Standing treatment probe build failed.'}
$additional=@();if($Rh2){$additional=@('RH2.BCDs.First.Aid')}
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $modRoot -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods $additional | Out-Null
$runtime=(Resolve-Path $RuntimeDir).Path
$profile=(Resolve-Path $SaveData).Path
$id='ch4acko3.standingprobe'+[Guid]::NewGuid().ToString('N')
foreach($file in @((Join-Path $runtime 'About/About.xml'),(Join-Path $profile 'Config/ModsConfig.xml'))){
    (Get-Content -LiteralPath $file -Raw).Replace('ch4acko3.sarbenchmarkruntime',$id) | Set-Content -LiteralPath $file
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StandingTreatmentProbe/bin/Release/net48/StandingTreatmentProbe.dll') -Destination (Join-Path $runtime 'Assemblies')
$arguments=@('-batchmode','-quicktest','-screen-width','1280','-screen-height','720',('-savedatafolder="'+$profile+'"'),'-logFile',('"'+(Join-Path $profile 'Player.log')+'"'))
$probe=Start-Process -FilePath (Resolve-Path $GameExe).Path -ArgumentList $arguments -WindowStyle Hidden -PassThru
$probe.Id | Set-Content -LiteralPath (Join-Path $profile 'pid.txt')
[pscustomobject]@{pid=$probe.Id;profile=$profile;results=(Join-Path $profile 'standing-results.txt')}
