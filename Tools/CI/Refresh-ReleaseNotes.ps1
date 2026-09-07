param([Parameter(Mandatory=$true)][string]$ArtifactRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path -LiteralPath $ArtifactRoot).Path
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$manifestPath=Join-Path $root 'manifest.json'
$m=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($m.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$' -or
    $m.tag -cne "v$($m.version)" -or $m.appid -cne '294100' -or $m.publishedfileid -cne '3796056278') {
    throw 'Expected an existing SAR release package.'
}
$texts=@{}
foreach ($language in @('en','zh-CN')) {
    $text=Get-Content -LiteralPath (Join-Path $repo "Docs/releases/$($m.version).$language.md") -Raw
    if ([string]::IsNullOrWhiteSpace($text) -or $text.Contains('\n')) { throw 'Invalid release notes.' }
    $texts[$language]=$text.TrimEnd()
}
foreach ($language in @('en','zh-CN')) {
    $path=Join-Path $root "release-notes.$language.md"
    [IO.File]::WriteAllText($path,$texts[$language]+"`n",[Text.UTF8Encoding]::new($false))
    $m.localizedNotes.$language=(Get-FileHash -LiteralPath $path).Hash
}
$combined=Join-Path $root 'release-notes.md'
[IO.File]::WriteAllText($combined,$texts['en']+"`n`n"+$texts['zh-CN']+"`n",[Text.UTF8Encoding]::new($false))
$m.notesSha256=(Get-FileHash -LiteralPath $combined).Hash
$m | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
Write-Host 'Refreshed release-note text and checksums; package files, archive and source commit preserved.'
