param(
  [string]$Configuration = "Release",
  [switch]$Sign,
  [switch]$SkipSignIfUnavailable,
  [switch]$NpmPack
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$distExe = Join-Path $root "dist\VibeCodingAlwaysOnTray.exe"
$releaseDir = Join-Path $root "release"

& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration

if ($Sign) {
  & (Join-Path $PSScriptRoot "sign.ps1") -FilePath $distExe -SkipIfUnavailable:$SkipSignIfUnavailable
}

& (Join-Path $PSScriptRoot "package.ps1") -Configuration $Configuration -SkipBuild
& (Join-Path $PSScriptRoot "checksums.ps1") -ReleaseDir $releaseDir

if ($NpmPack) {
  Remove-Item (Join-Path $releaseDir "vibe-coding-always-on*.tgz") -Force -ErrorAction SilentlyContinue

  Push-Location $root
  try {
    npm pack --ignore-scripts --pack-destination $releaseDir
  }
  finally {
    Pop-Location
  }

  $package = Get-Content (Join-Path $root "package.json") -Raw | ConvertFrom-Json
  $versionedPackage = Join-Path $releaseDir ("vibe-coding-always-on-{0}.tgz" -f $package.version)
  $stablePackage = Join-Path $releaseDir "vibe-coding-always-on.tgz"
  if (-not (Test-Path $versionedPackage)) {
    throw "Expected npm package was not created: $versionedPackage"
  }
  Copy-Item $versionedPackage $stablePackage -Force

  & (Join-Path $PSScriptRoot "checksums.ps1") -ReleaseDir $releaseDir
}

Write-Host "Release artifacts are in $releaseDir"
