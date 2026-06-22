param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$src = Join-Path $root "src\VibeCodingAlwaysOnTray"
$dist = Join-Path $root "dist"
$exe = Join-Path $dist "VibeCodingAlwaysOnTray.exe"
$program = Join-Path $src "Program.cs"
$manifest = Join-Path $src "app.manifest"
$icon = Join-Path $src "VibeCodingAlwaysOnTray.ico"

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$compilerCandidates = @(
  (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
  (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)

$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
  throw "Could not find the .NET Framework C# compiler. Expected csc.exe under $env:WINDIR\Microsoft.NET."
}

$optimize = if ($Configuration -ieq "Release") { "/optimize+" } else { "/optimize-" }

& $compiler `
  /nologo `
  /target:winexe `
  /platform:anycpu `
  $optimize `
  /codepage:65001 `
  /out:$exe `
  /win32manifest:$manifest `
  /win32icon:$icon `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Management.dll `
  /reference:System.Runtime.Serialization.dll `
  /reference:System.Xml.dll `
  $program

if ($LASTEXITCODE -ne 0) {
  throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $exe"
