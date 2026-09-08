param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData
)
$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'SchedulerOptimizer/New-EngineProfile.ps1') -ModDir $modRoot `
    -RuntimeDir $RuntimeDir -SaveData $SaveData `
    -AdditionalMods @('dubwise.dubsrimkit','troopersmith1.deathrattle') | Out-Null
$runtime = [IO.Path]::GetFullPath($RuntimeDir)
$profile = [IO.Path]::GetFullPath($SaveData)
$package = 'ch4acko3.sarrimkitprobe.p' + [Guid]::NewGuid().ToString('N')
$aboutPath = Join-Path $runtime 'About/About.xml'
[xml]$about = Get-Content -LiteralPath $aboutPath -Raw
$about.ModMetaData.packageId = $package
$about.Save($aboutPath)
$configPath = Join-Path $profile 'Config/ModsConfig.xml'
[xml]$config = Get-Content -LiteralPath $configPath -Raw
($config.SelectNodes('//activeMods/li') | Where-Object { $_.InnerText -eq 'ch4acko3.sarbenchmarkruntime' }).InnerText = $package
$config.Save($configPath)
Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($runtime.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'runtime-manifest.json')
$argsSar = @('-batchmode','-nographics','-quicktest','-sar-bench-worker','-sar-rimkit-deathrattle-probe',
    ('-savedatafolder="' + $profile + '"'),'-logFile',('"' + (Join-Path $profile 'Player.log') + '"'))
$probe = Start-Process -FilePath $GameExe -ArgumentList $argsSar -WindowStyle Hidden -PassThru
[pscustomobject]@{ pid=$probe.Id; profile=$profile; runtime=$runtime }
