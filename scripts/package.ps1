param(
  [string]$Configuration = "Release",
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $root "dist"
$release = Join-Path $root "release"
$packageDir = Join-Path $release "VibeCodingAlwaysOn"
$zip = Join-Path $release "VibeCodingAlwaysOnTray.zip"

if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
}

Remove-Item -Recurse -Force $packageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path $release | Out-Null

Copy-Item (Join-Path $dist "VibeCodingAlwaysOnTray.exe") $packageDir -Force
Copy-Item (Join-Path $root "README.md") $packageDir -Force
Copy-Item (Join-Path $root "README.zh-CN.md") $packageDir -Force
Copy-Item (Join-Path $root "RELEASE_NOTES.md") $packageDir -Force
Copy-Item (Join-Path $root "SECURITY.md") $packageDir -Force
Copy-Item (Join-Path $root "LICENSE") $packageDir -Force
Copy-Item (Join-Path $root "install.ps1") $packageDir -Force

Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zip -Force

Write-Host "Packaged $zip"
