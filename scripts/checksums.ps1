param(
  [string]$ReleaseDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "release"),
  [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "release\SHA256SUMS.txt")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ReleaseDir)) {
  throw "Release directory does not exist: $ReleaseDir"
}

$releaseFullPath = (Resolve-Path -Path $ReleaseDir).Path.TrimEnd("\", "/")
$outputFullPath = if (Test-Path $OutputPath) { (Resolve-Path -Path $OutputPath).Path } else { $null }

$files = Get-ChildItem -Path $ReleaseDir -File |
  Where-Object { -not $outputFullPath -or $_.FullName -ne $outputFullPath } |
  Sort-Object FullName

$lines = foreach ($file in $files) {
  $hash = Get-FileHash -Algorithm SHA256 -Path $file.FullName
  $relative = $file.FullName.Substring($releaseFullPath.Length).TrimStart("\", "/").Replace("\", "/")
  "$($hash.Hash.ToLowerInvariant())  $relative"
}

$lines | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Wrote $OutputPath"
