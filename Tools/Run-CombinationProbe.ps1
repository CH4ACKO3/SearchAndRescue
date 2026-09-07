param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData,
    [switch]$ChooseYourMedicine
)
$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$exe = (Resolve-Path -LiteralPath $GameExe).Path
$ids = @('ludeon.rimworld.biotech','ceteam.combatextended','memegoddess.smartmedicine',
    'th3fr3d.extendedinjuries','aoba.hemogendirect')
if ($ChooseYourMedicine) { $ids += 'kopp.chooseyourmedicine' }
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $modRoot `
    -RuntimeDir $RuntimeDir -SaveData $SaveData -AdditionalMods $ids | Out-Null
$runtime = [IO.Path]::GetFullPath($RuntimeDir)
$profile = [IO.Path]::GetFullPath($SaveData)
# Each disposable runtime has a distinct ID so existing benchmark installs remain intact.
$package = 'ch4acko3.sarcombinationprobe.' + [Guid]::NewGuid().ToString('N')
$aboutPath = Join-Path $runtime 'About/About.xml'
$about = [xml](Get-Content -LiteralPath $aboutPath -Raw)
$about.ModMetaData.packageId = $package
$about.Save($aboutPath)
$configPath = Join-Path $profile 'Config/ModsConfig.xml'
$config = [xml](Get-Content -LiteralPath $configPath -Raw)
($config.SelectNodes('//activeMods/li') | Where-Object { $_.InnerText -eq 'ch4acko3.sarbenchmarkruntime' }).InnerText = $package
$config.Save($configPath)
Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($runtime.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'runtime-manifest.json')
$arguments = @('-batchmode','-nographics','-quicktest','-sar-bench-worker','-sar-combination-probe',
    ('-savedatafolder="' + $profile + '"'),'-logFile',('"' + (Join-Path $profile 'Player.log') + '"'))
$probe = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
[pscustomobject]@{ pid=$probe.Id; profile=$profile; runtime=$runtime }
