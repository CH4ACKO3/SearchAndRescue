param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [Parameter(Mandatory=$true)][string]$SaveData,
    [Parameter(Mandatory=$true)][string]$WorkerRoot,
    [ValidateRange(1,12)][int]$WorkerCount = 6,
    [switch]$NoGraphics,
    [ValidateSet('Source','Vanilla')][string]$Profile = 'Source'
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $GameExe).Path
$source = (Resolve-Path -LiteralPath $SaveData).Path
$root = [IO.Path]::GetFullPath($WorkerRoot)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$processes = @()
for ($i = 0; $i -lt $WorkerCount; $i++) {
    $target = Join-Path $root "worker$i"
    if (Test-Path -LiteralPath "$target/pid.txt") {
        $existing = [int](Get-Content -LiteralPath "$target/pid.txt")
        if (Get-Process -Id $existing -ErrorAction SilentlyContinue) {
            throw "Recorded worker PID $existing is still running; stop it before reinitializing $target."
        }
    }
    New-Item -ItemType Directory -Force -Path "$target/Config", "$target/Saves", "$target/SAR_EngineBench" | Out-Null
    Get-ChildItem -LiteralPath "$source/Config" -Filter '*.xml' | Copy-Item -Destination "$target/Config"
    $saves = @(Get-ChildItem -LiteralPath "$source/Saves" -Filter 'SAR_Engine_*_Initial.rws')
    if ($saves.Count -eq 0 -and !(Test-Path -LiteralPath "$source/Saves/SAR_Engine_Template.rws")) {
        throw 'Provide an engine benchmark initial save or SAR_Engine_Template.rws.'
    }
    $saves | Copy-Item -Destination "$target/Saves"
    if (Test-Path -LiteralPath "$source/Saves/SAR_Engine_Template.rws") {
        Copy-Item -LiteralPath "$source/Saves/SAR_Engine_Template.rws" -Destination "$target/Saves"
    }
    # Workers use the file queue; only the Gabs bridge is omitted to avoid duplicate registration.
    $config = [xml](Get-Content -LiteralPath "$target/Config/ModsConfig.xml" -Raw)
    foreach ($node in @($config.SelectNodes('//activeMods/li'))) {
        if ($node.InnerText -ieq 'brrainz.rimbridgeserver' -or
            ($Profile -eq 'Vanilla' -and $node.InnerText -notin @('brrainz.harmony','ludeon.rimworld','ch4acko3.searchandrescue','ch4acko3.sarbenchmarkruntime'))) {
            [void]$node.ParentNode.RemoveChild($node)
        }
    }
    # RimWorld automatically enables newly discovered DLC; register them without enabling them.
    if ($null -eq $config.SelectSingleNode('//knownExpansions')) {
        [void]$config.ModsConfigData.AppendChild($config.CreateElement('knownExpansions'))
    }
    foreach ($id in @('royalty','ideology','biotech','anomaly','odyssey')) {
        $package = "ludeon.rimworld.$id"
        if ($package -notin @($config.SelectNodes('//knownExpansions/li') | ForEach-Object { $_.InnerText })) {
            $entry = $config.CreateElement('li'); $entry.InnerText = $package
            [void]$config.SelectSingleNode('//knownExpansions').AppendChild($entry)
        }
    }
    $config.Save("$target/Config/ModsConfig.xml")
    foreach ($name in @('ready','queued.xml')) {
        $path = Join-Path "$target/SAR_EngineBench" $name
        if (Test-Path -LiteralPath $path) {
            if ($name -eq 'queued.xml') { throw "Unfinished queued request $path; inspect it before reuse." }
            Remove-Item -LiteralPath $path
        }
    }
    $arguments = @('-batchmode', '-screen-fullscreen', '0',
        '-screen-width', '640', '-screen-height', '480', '-sar-bench-worker',
        "-savedatafolder=`"$target`"", '-logFile', "`"$target/Player.log`"")
    if ($NoGraphics) { $arguments += '-nographics' }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.Id | Set-Content -LiteralPath "$target/pid.txt"
    $processes += [pscustomobject]@{ pid=$process.Id; directory=$target; executable=$exe;
        configHash=(Get-FileHash -LiteralPath "$target/Config/ModsConfig.xml").Hash }
}
$processes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'workers.json')
$processes
