# Publishes the portable, self-contained app to dist\Mormorskopiadammsugare\
# Usage (from the repo root):  .\scripts\publish.ps1
# Settings live in src\PhotoSorter.App\Properties\PublishProfiles\Portable.pubxml,
# so this is the same as:      dotnet publish src\PhotoSorter.App -p:PublishProfile=Portable
# (the script also clears dist\Mormorskopiadammsugare\ first so no stale files are left behind)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'dist\Mormorskopiadammsugare'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src\PhotoSorter.App\PhotoSorter.App.csproj') -p:PublishProfile=Portable
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`nPortable app ready: $out\Mormorskopiadammsugare.exe" -ForegroundColor Green
