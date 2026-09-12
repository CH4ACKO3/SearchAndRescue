param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData
)
$ErrorActionPreference='Stop'
$modRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project=Join-Path $PSScriptRoot 'ExtractionProbe/ExtractionProbe.csproj'
dotnet build $project -c Release --nologo
if($LASTEXITCODE){throw 'Extraction probe build failed.'}
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $modRoot -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods @('ludeon.rimworld.biotech','th3fr3d.extendedinjuries','aoba.hemogendirect') | Out-Null
$runtime=(Resolve-Path $RuntimeDir).Path
$profile=(Resolve-Path $SaveData).Path
$id='ch4acko3.extractionprobe.p'+[Guid]::NewGuid().ToString('N')
foreach($path in @((Join-Path $runtime 'About/About.xml'),(Join-Path $profile 'Config/ModsConfig.xml'))){
    (Get-Content -LiteralPath $path -Raw).Replace('ch4acko3.sarbenchmarkruntime',$id) | Set-Content -LiteralPath $path
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ExtractionProbe/bin/Release/net48/ExtractionProbe.dll') -Destination (Join-Path $runtime 'Assemblies')
$arguments=@('-batchmode','-quicktest',('-savedatafolder="'+$profile+'"'),'-logFile',('"'+(Join-Path $profile 'Player.log')+'"'))
$probe=Start-Process -FilePath (Resolve-Path $GameExe).Path -ArgumentList $arguments -WindowStyle Hidden -PassThru
$probe.Id | Set-Content -LiteralPath (Join-Path $profile 'pid.txt')
[pscustomobject]@{pid=$probe.Id;profile=$profile;results=(Join-Path $profile 'extraction-results.txt')}
