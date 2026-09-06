param([Parameter(Mandatory=$true)][string]$ArtifactRoot, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path -LiteralPath $ArtifactRoot).Path
$m=Get-Content -LiteralPath "$root/manifest.json" -Raw | ConvertFrom-Json
if ($m.appid -cne '294100' -or $m.publishedfileid -cne '3796056278' -or
    $m.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$' -or
    ($m.tag -cne "v$($m.version)" -and !($DryRun -and !$m.tag))) { throw 'Manifest must identify an explicit release tag and the existing SAR Workshop item.' }
$stage=Join-Path $root "SearchAndRescue-$($m.version)"
$archive=Join-Path $root "SearchAndRescue-$($m.version).zip"
if ((Get-FileHash $archive).Hash -cne $m.archiveSha256 -or
    (Get-FileHash "$root/release-notes.md").Hash -cne $m.notesSha256) { throw 'Archive/notes checksum mismatch.' }
foreach ($language in @('en','zh-CN')) {
    $description=Join-Path $root "Description.$language.bbcode"
    if ((Get-FileHash -LiteralPath $description).Hash -cne $m.descriptions.$language) { throw "Description checksum mismatch: $language" }
}
$seen=@{}
foreach ($f in $m.files) {
    if ($f.path -match '(^/|:|\\|(^|/)\.\.(/|$))' -or $seen.ContainsKey($f.path)) { throw 'Invalid/duplicate manifest path.' }
    $seen[$f.path]=$true
    if ((Get-FileHash -LiteralPath (Join-Path $stage $f.path)).Hash -cne $f.sha256) { throw "Content checksum mismatch: $($f.path)" }
}
if (@(Get-ChildItem -LiteralPath $stage -File -Recurse).Count -ne $seen.Count) { throw 'Unexpected files in package.' }
$about=[xml](Get-Content -LiteralPath "$stage/About/About.xml" -Raw)
if ($about.ModMetaData.modVersion -cne $m.version -or
    (Get-Content "$stage/About/PublishedFileId.txt" -Raw).Trim() -cne '3796056278') { throw 'Package identity mismatch.' }
function Escape-Vdf([string]$value) { $value.Replace('\','\\').Replace('"','\"').Replace("`r",'').Replace("`n",'\n') }
$vdf = '"workshopitem"' + "`n{`n" + '  "appid" "294100"' + "`n" + '  "publishedfileid" "3796056278"' + "`n" +
    '  "contentfolder" "' + (Escape-Vdf $stage) + '"' + "`n" +
    '  "changenote" "' + (Escape-Vdf (Get-Content "$root/release-notes.md" -Raw)) + '"' + "`n}`n"
$vdfPath=Join-Path $root 'workshop-upload.vdf'
[IO.File]::WriteAllText($vdfPath,$vdf,[Text.UTF8Encoding]::new($false))
if ($DryRun) { Write-Host 'PASS: checksums, release identity and content VDF and bilingual payload validated. No Steam login/upload performed.'; return }
foreach ($key in @('STEAM_USERNAME','STEAM_PASSWORD','STEAM_CONFIG_VDF_BASE64','STEAM_REFRESH_TOKEN')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) { throw "Missing steam-workshop environment secret: $key" }
}
$publisher=Join-Path $root 'publisher/WorkshopPublisher.dll'
dotnet $publisher check $root
if ($LASTEXITCODE) { throw 'Bilingual authorization/ownership preflight failed; no files uploaded.' }
$steam=Join-Path ([IO.Path]::GetTempPath()) ('sar-steam-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$steam/config" -Force | Out-Null
try {
    Invoke-WebRequest -Uri 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile "$steam/steamcmd.zip"
    Expand-Archive -LiteralPath "$steam/steamcmd.zip" -DestinationPath $steam
    [IO.File]::WriteAllBytes("$steam/config/config.vdf",[Convert]::FromBase64String($env:STEAM_CONFIG_VDF_BASE64))
    # Capture output instead of streaming login/session details into public Actions logs.
    $output = & "$steam/steamcmd.exe" '+@ShutdownOnFailedCommand' '1' '+@NoPromptForPassword' '1' '+login' $env:STEAM_USERNAME $env:STEAM_PASSWORD '+workshop_build_item' $vdfPath '+quit' 2>&1
    $exitCode=$LASTEXITCODE
    $text=$output -join "`n"
    if ($exitCode -ne 0 -or $text -notmatch '(?im)^Success\.\s+(?:Published|Updated)[^\r\n]*\b3796056278\b') {
        throw 'Steam did not confirm the Workshop update. Validate login/Steam Guard locally and refresh the environment secrets; raw authentication output is withheld.'
    }
    dotnet $publisher publish $root
    if ($LASTEXITCODE) { throw 'Workshop files uploaded, but bilingual description verification failed. Inspect both languages before retrying.' }
    Write-Host "Steam confirmed update of Workshop item 3796056278 for $($m.tag)."
} finally {
    # Only this script-created temporary directory is removed; no credentials enter artifacts/caches.
    if ($steam.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($steam) -match '^sar-steam-[0-9a-f]{32}$') { Remove-Item -LiteralPath $steam -Recurse -Force }
}
