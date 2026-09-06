param([string]$Tag = '', [string]$OutputRoot = 'artifacts/ci')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $root
$about = [xml](Get-Content About/About.xml -Raw)
$version = [string]$about.ModMetaData.modVersion
$semver = '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$'
if ($version -notmatch $semver) { throw 'Invalid About.xml modVersion.' }
if ($Tag -and ($Tag -cne "v$version")) { throw "Tag '$Tag' must equal About.xml version 'v$version'." }
$notes = "Docs/releases/$version.md"
if ($Tag -and !(Test-Path -LiteralPath $notes)) { throw "Missing release notes: $notes" }
foreach ($xml in Get-ChildItem About,Defs,Languages,Patches -Filter '*.xml' -Recurse) { [void][xml](Get-Content -LiteralPath $xml.FullName -Raw) }
$baseKeys = @(([xml](Get-Content Languages/English/Keyed/SearchAndRescue.xml -Raw)).LanguageData.ChildNodes | Where-Object NodeType -eq 'Element' | ForEach-Object Name | Sort-Object)
foreach ($language in @('ChineseSimplified','ChineseTraditional')) {
    $keys = @(([xml](Get-Content "Languages/$language/Keyed/SearchAndRescue.xml" -Raw)).LanguageData.ChildNodes | Where-Object NodeType -eq 'Element' | ForEach-Object Name | Sort-Object)
    if (Compare-Object $baseKeys $keys) { throw "Translation keys differ: $language" }
}
dotnet build Source/SearchAndRescue/SearchAndRescue.csproj -c Release -warnaserror -p:UseReferencePackages=true
if ($LASTEXITCODE) { throw 'Build failed.' }
dotnet run --project Tools/SchedulerSimulation/SchedulerSimulation.csproj -c Release -warnaserror
if ($LASTEXITCODE) { throw 'Production rule regressions failed.' }
& "$root/Tools/BuildRelease.ps1" -Version $version -OutputRoot $OutputRoot
$out = (Resolve-Path -LiteralPath $OutputRoot).Path
$stage = Join-Path $out "SearchAndRescue-$version"
# The local Steam-generated ID is ignored by Git; retain the existing item in release packages.
Set-Content -LiteralPath "$stage/About/PublishedFileId.txt" -Value '3796056278' -NoNewline
# BuildRelease created the zip before the pinned item ID was written; replace only that generated archive.
$zip = Join-Path $out "SearchAndRescue-$version.zip"
Compress-Archive -LiteralPath $stage -DestinationPath $zip -Force
$noteText = if (Test-Path -LiteralPath $notes) { Get-Content -LiteralPath $notes -Raw } else { "Development build $version (not for Workshop publishing)." }
Set-Content -LiteralPath "$out/release-notes.md" -Value $noteText -NoNewline
$descriptions = @{}
foreach ($language in @('en','zh-CN')) {
    $name = "Description.$language.bbcode"
    $source = "Docs/workshop/$name"
    $description = Get-Content -LiteralPath $source -Raw
    if ([string]::IsNullOrWhiteSpace($description) -or [Text.Encoding]::UTF8.GetByteCount($description) -ge 8000) { throw "Empty or oversized Workshop description: $language" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $out $name)
    $descriptions[$language] = (Get-FileHash (Join-Path $out $name)).Hash
}
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
[ordered]@{ version=$version; tag=$Tag; appid='294100'; publishedfileid='3796056278';
    commit=(git rev-parse HEAD); archive=[IO.Path]::GetFileName($zip); archiveSha256=(Get-FileHash $zip).Hash;
    descriptions=$descriptions; notesSha256=(Get-FileHash "$out/release-notes.md").Hash; files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$out/manifest.json"
dotnet publish Tools/WorkshopPublisher/WorkshopPublisher.csproj -c Release -warnaserror -o "$out/publisher"
if ($LASTEXITCODE) { throw 'Bilingual publisher build failed.' }
dotnet "$out/publisher/WorkshopPublisher.dll" validate $out
if ($LASTEXITCODE) { throw 'Bilingual payload validation failed.' }
if ($env:GITHUB_OUTPUT) { "version=$version" >> $env:GITHUB_OUTPUT }
Write-Host "Validated release package: $zip"
