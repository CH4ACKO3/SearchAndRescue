param(
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData,
    [string[]]$AdditionalMods = @(),
    [switch]$Initialize,
    [switch]$LastUse
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $root `
    -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods ($AdditionalMods + @('xmb.ancienturbanruins.mo')) | Out-Null
$package = 'ch4acko3.saraurprobe.p' + [Guid]::NewGuid().ToString('N')
$a = [xml](Get-Content "$RuntimeDir/About/About.xml" -Raw)
$a.ModMetaData.packageId = $package
$a.Save("$RuntimeDir/About/About.xml")
$c = [xml](Get-Content "$SaveData/Config/ModsConfig.xml" -Raw)
($c.SelectNodes('//activeMods/li') | Where-Object InnerText -eq 'ch4acko3.sarbenchmarkruntime').InnerText = $package
$c.Save("$SaveData/Config/ModsConfig.xml")
$arguments = @('-batchmode','-nographics','-quicktest','-sar-bench-worker','-sar-aur-probe',
    ('-savedatafolder="'+$SaveData+'"'),'-logFile',('"'+$SaveData+'\Player.log"'))
if ($Initialize) { $arguments += '-sar-aur-initialize' }
if ($LastUse) { $arguments += '-sar-aur-last-use' }
$process = Start-Process -FilePath 'D:\App\Steam\steamapps\common\RimWorld\RimWorldWin64.exe' `
    -ArgumentList $arguments -WindowStyle Hidden -PassThru
[pscustomobject]@{ pid=$process.Id; profile=$SaveData }
