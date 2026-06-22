param(
  [string]$FilePath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "dist\VibeCodingAlwaysOnTray.exe"),
  [string]$PfxPath = $env:VCAO_SIGN_PFX_PATH,
  [string]$PfxPassword = $env:VCAO_SIGN_PFX_PASSWORD,
  [string]$CertificateThumbprint = $env:VCAO_SIGN_CERT_THUMBPRINT,
  [string]$TimestampUrl = "http://timestamp.digicert.com",
  [switch]$SkipIfUnavailable
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
  $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($cmd) {
    return $cmd.Source
  }

  $kits = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
    "$env:ProgramFiles\Windows Kits\10\bin"
  )

  foreach ($kit in $kits) {
    if (-not (Test-Path $kit)) {
      continue
    }

    $candidate = Get-ChildItem -Path $kit -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
      Sort-Object FullName -Descending |
      Select-Object -First 1

    if ($candidate) {
      return $candidate.FullName
    }
  }

  return $null
}

function Invoke-SignTool {
  param(
    [string]$SignTool,
    [string[]]$Arguments
  )

  & $SignTool @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE."
  }
}

if (-not (Test-Path $FilePath)) {
  throw "File to sign does not exist: $FilePath"
}

$signTool = Find-SignTool
if (-not $signTool) {
  if ($SkipIfUnavailable) {
    Write-Host "signtool.exe not found; skipping signing."
    exit 0
  }

  throw "signtool.exe was not found. Install the Windows SDK or run with -SkipIfUnavailable."
}

$arguments = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")

if ($PfxPath) {
  if (-not (Test-Path $PfxPath)) {
    throw "PFX file does not exist: $PfxPath"
  }

  $arguments += @("/f", $PfxPath)
  if ($PfxPassword) {
    $arguments += @("/p", $PfxPassword)
  }
}
elseif ($CertificateThumbprint) {
  $arguments += @("/sha1", $CertificateThumbprint)
}
else {
  if ($SkipIfUnavailable) {
    Write-Host "No signing certificate configured; skipping signing."
    exit 0
  }

  throw "No signing certificate configured. Set VCAO_SIGN_PFX_PATH or VCAO_SIGN_CERT_THUMBPRINT."
}

$arguments += $FilePath

Write-Host "Signing $FilePath"
Invoke-SignTool -SignTool $signTool -Arguments $arguments

Write-Host "Verifying signature"
Invoke-SignTool -SignTool $signTool -Arguments @("verify", "/pa", "/v", $FilePath)
