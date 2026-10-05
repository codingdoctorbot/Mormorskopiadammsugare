# Builds the solution and runs the tests.
# Usage (from the repo root):  .\scripts\build.ps1
# Same as running:            dotnet test --solution PhotoSorter.sln
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

dotnet test --solution (Join-Path $root 'PhotoSorter.sln') -c Debug
exit $LASTEXITCODE
