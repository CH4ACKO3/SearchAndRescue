param(
    [Parameter(Mandatory=$true)][string]$ModDir,
    [Parameter(Mandatory=$true)][string]$RuntimeDir,
    [Parameter(Mandatory=$true)][string]$SaveData,
    [string[]]$AdditionalMods = @()
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $ModDir).Path
$runtime = [IO.Path]::GetFullPath($RuntimeDir)
$profile = [IO.Path]::GetFullPath($SaveData)
$ids = @('brrainz.harmony','ludeon.rimworld') + @($AdditionalMods | ForEach-Object { $_.ToLowerInvariant() }) + @('ch4acko3.sarbenchmarkruntime')
if ('ch4acko3.searchandrescue' -in $ids -or ($ids | Select-Object -Unique).Count -ne $ids.Count) {
    throw 'Provide distinct additional mod IDs; SAR is supplied by the isolated runtime.'
}
# Fresh destinations avoid mixing old runtime files or saves from another mod configuration.
if ((Test-Path -LiteralPath $runtime) -or (Test-Path -LiteralPath $profile)) {
    throw 'RuntimeDir and SaveData must be new directories.'
}
foreach ($required in @('About/About.xml','Assemblies/SearchAndRescue.dll','Defs','Patches','Languages','Textures','LoadFolders.xml')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing runtime content: $required" }
}
New-Item -ItemType Directory -Path $runtime, "$profile/Config" -Force | Out-Null
foreach ($part in @('About','Assemblies','Defs','Patches','Languages','Textures','LoadFolders.xml')) {
    Copy-Item -LiteralPath (Join-Path $source $part) -Destination $runtime -Recurse
}
[xml]$about = Get-Content -LiteralPath "$runtime/About/About.xml" -Raw
$about.ModMetaData.packageId = 'ch4acko3.sarbenchmarkruntime'
$about.ModMetaData.name = 'Search and Rescue Benchmark Runtime'
$about.Save("$runtime/About/About.xml")
$config = [xml]'<ModsConfigData><version>1.6</version><activeMods/><knownExpansions/></ModsConfigData>'
foreach ($id in $ids) {
    $node = $config.CreateElement('li'); $node.InnerText = $id.ToLowerInvariant()
    [void]$config.SelectSingleNode('//activeMods').AppendChild($node)
}
foreach ($id in @('royalty','ideology','biotech','anomaly','odyssey')) {
    $node = $config.CreateElement('li'); $node.InnerText = "ludeon.rimworld.$id"
    [void]$config.SelectSingleNode('//knownExpansions').AppendChild($node)
}
$config.Save("$profile/Config/ModsConfig.xml")
Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName.Substring($runtime.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
} | ConvertTo-Json | Set-Content -LiteralPath "$profile/runtime-manifest.json"
[pscustomobject]@{ runtime=$runtime; profile=$profile; config="$profile/Config/ModsConfig.xml" }
