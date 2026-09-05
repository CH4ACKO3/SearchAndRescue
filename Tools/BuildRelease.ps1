param(
    [string]$Version = "0.1.0-alpha.1",
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$modRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = [System.IO.Path]::GetFullPath((Join-Path $modRoot "artifacts\releases"))
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

$stage = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot "SearchAndRescue-$Version"))
$archive = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot "SearchAndRescue-$Version.zip"))
if (Test-Path -LiteralPath $stage) {
    throw "Release directory already exists: $stage"
}
if (Test-Path -LiteralPath $archive) {
    throw "Release archive already exists: $archive"
}

$requiredFiles = @(
    "About\About.xml",
    "About\preview.png",
    "Assemblies\SearchAndRescue.dll",
    "LoadFolders.xml",
    "LICENSE"
)
foreach ($relativePath in $requiredFiles) {
    $source = Join-Path $modRoot $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing release input: $source"
    }
}

$about = [xml](Get-Content -LiteralPath (Join-Path $modRoot "About\About.xml") -Raw)
if ([string]$about.ModMetaData.modVersion -ne $Version) {
    throw "About.xml version '$($about.ModMetaData.modVersion)' does not match requested '$Version'."
}

New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($directory in @("About", "Assemblies", "Defs", "Languages", "Patches", "Textures")) {
    Copy-Item -LiteralPath (Join-Path $modRoot $directory) -Destination $stage -Recurse
}
Copy-Item -LiteralPath (Join-Path $modRoot "LoadFolders.xml") -Destination $stage
Copy-Item -LiteralPath (Join-Path $modRoot "LICENSE") -Destination $stage

$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -Force |
    Where-Object { $_.FullName -match '[\\/](Source|SourceAssets|References|Tools|Docs|bin|obj)([\\/]|$)' }
if ($forbidden) {
    throw "Forbidden development files entered the release stage."
}

Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$fileCount = (Get-ChildItem -LiteralPath $stage -Recurse -File).Count
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
Write-Host "Release stage: $stage"
Write-Host "Files: $fileCount"
Write-Host "Archive: $archive"
Write-Host "SHA256: $archiveHash"
