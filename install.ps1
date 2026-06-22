param(
  [string]$Repo = $env:VCAO_REPO,
  [string]$Version = "latest",
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\VibeCodingAlwaysOn"),
  [switch]$Enable,
  [switch]$InstallHelper,
  [switch]$NoLaunch,
  [switch]$NoShortcuts,
  [switch]$NoDesktopShortcut,
  [switch]$NoStartMenuShortcut
)

$ErrorActionPreference = "Stop"

function Write-Step {
  param([string]$Message)
  Write-Host "[vcao] $Message"
}

function Get-CurrentUserSid {
  return [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

function Get-LocalReleaseZip {
  $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
  $candidates = @(
    (Join-Path $scriptDir "release\VibeCodingAlwaysOnTray.zip"),
    (Join-Path $scriptDir "VibeCodingAlwaysOnTray.zip")
  )

  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
      return (Resolve-Path $candidate).Path
    }
  }

  return $null
}

function Get-DownloadUrl {
  if ([string]::IsNullOrWhiteSpace($Repo)) {
    throw "Repo is required when no local release zip is available. Pass -Repo owner/name or set VCAO_REPO=owner/name."
  }

  if ($Version -eq "latest") {
    return "https://github.com/$Repo/releases/latest/download/VibeCodingAlwaysOnTray.zip"
  }

  return "https://github.com/$Repo/releases/download/$Version/VibeCodingAlwaysOnTray.zip"
}

function Expand-Package {
  param(
    [string]$ZipPath,
    [string]$Destination
  )

  $temp = Join-Path $env:TEMP ("vcao-install-" + [Guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory -Force -Path $temp | Out-Null

  try {
    Expand-Archive -Path $ZipPath -DestinationPath $temp -Force
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    $exe = Get-ChildItem -Path $temp -Recurse -Filter "VibeCodingAlwaysOnTray.exe" | Select-Object -First 1
    if (-not $exe) {
      throw "VibeCodingAlwaysOnTray.exe was not found in package."
    }

    Copy-Item $exe.FullName (Join-Path $Destination "VibeCodingAlwaysOnTray.exe") -Force

    foreach ($name in @("README.md", "README.zh-CN.md", "RELEASE_NOTES.md", "LICENSE")) {
      $file = Get-ChildItem -Path $temp -Recurse -Filter $name | Select-Object -First 1
      if ($file) {
        Copy-Item $file.FullName (Join-Path $Destination $name) -Force
      }
    }
  }
  finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
  }
}

function Invoke-ElevatedExeAction {
  param(
    [string]$ExePath,
    [string]$Action
  )

  $appDir = Join-Path $env:APPDATA "VibeCodingAlwaysOn"
  $sid = Get-CurrentUserSid
  $args = "--$Action --appdir `"$appDir`" --usersid `"$sid`""
  Start-Process -FilePath $ExePath -ArgumentList $args -Verb RunAs -WindowStyle Hidden -Wait
}

function Test-HelperInstalled {
  $result = Start-Process -FilePath "schtasks.exe" -ArgumentList @("/Query", "/TN", "VibeCodingAlwaysOnTrayHelper") -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$env:TEMP\vcao-schtasks-query.out" -RedirectStandardError "$env:TEMP\vcao-schtasks-query.err"
  Remove-Item "$env:TEMP\vcao-schtasks-query.out", "$env:TEMP\vcao-schtasks-query.err" -Force -ErrorAction SilentlyContinue
  return $result.ExitCode -eq 0
}

function Invoke-HelperAction {
  param([string]$Action)

  $appDir = Join-Path $env:APPDATA "VibeCodingAlwaysOn"
  New-Item -ItemType Directory -Force -Path $appDir | Out-Null

  $requestId = [Guid]::NewGuid().ToString("N")
  $requestPath = Join-Path $appDir "helper-request.txt"
  $resultPath = Join-Path $appDir "helper-result.txt"

  Set-Content -Path $requestPath -Value @($requestId, $Action) -Encoding UTF8
  Remove-Item $resultPath -Force -ErrorAction SilentlyContinue

  $run = Start-Process -FilePath "schtasks.exe" -ArgumentList @("/Run", "/TN", "VibeCodingAlwaysOnTrayHelper") -NoNewWindow -Wait -PassThru
  if ($run.ExitCode -ne 0) {
    throw "No-prompt helper task failed to start."
  }

  $deadline = (Get-Date).AddSeconds(30)
  while ((Get-Date) -lt $deadline) {
    if (Test-Path $resultPath) {
      $lines = Get-Content -Path $resultPath -Encoding UTF8
      if ($lines.Count -ge 2 -and $lines[0].Trim() -eq $requestId) {
        if ($lines[1].Trim() -eq "OK") {
          return
        }

        $message = if ($lines.Count -gt 2) { ($lines[2..($lines.Count - 1)] -join "`n") } else { "No-prompt helper action failed." }
        throw $message
      }
    }

    Start-Sleep -Milliseconds 200
  }

  throw "No-prompt helper did not return in time."
}

function New-AppShortcut {
  param(
    [string]$ShortcutPath,
    [string]$TargetPath,
    [string]$Description
  )

  $folder = Split-Path -Parent $ShortcutPath
  New-Item -ItemType Directory -Force -Path $folder | Out-Null

  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($ShortcutPath)
  $shortcut.TargetPath = $TargetPath
  $shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
  $shortcut.IconLocation = "$TargetPath,0"
  $shortcut.Description = $Description
  $shortcut.Save()
}

function Install-Shortcuts {
  param([string]$ExePath)

  if ($NoShortcuts) {
    return
  }

  $description = "Keep Windows awake for long-running coding and agent sessions."

  if (-not $NoStartMenuShortcut) {
    $programs = [Environment]::GetFolderPath("Programs")
    $startMenuShortcut = Join-Path $programs "Vibe Coding Always-On\Vibe Coding Always-On.lnk"
    New-AppShortcut -ShortcutPath $startMenuShortcut -TargetPath $ExePath -Description $description
    Write-Step "Created Start Menu shortcut"
  }

  if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("DesktopDirectory")
    $desktopShortcut = Join-Path $desktop "Vibe Coding Always-On.lnk"
    New-AppShortcut -ShortcutPath $desktopShortcut -TargetPath $ExePath -Description $description
    Write-Step "Created desktop shortcut"
  }
}

if ($env:OS -notlike "*Windows*") {
  throw "Vibe Coding Always-On only supports Windows."
}

$zip = Get-LocalReleaseZip
$downloadedZip = $null

if (-not $zip) {
  $url = Get-DownloadUrl
  $downloadedZip = Join-Path $env:TEMP ("VibeCodingAlwaysOnTray-" + [Guid]::NewGuid().ToString("N") + ".zip")
  Write-Step "Downloading $url"
  Invoke-WebRequest -Uri $url -OutFile $downloadedZip -UseBasicParsing
  $zip = $downloadedZip
}
else {
  Write-Step "Using local package $zip"
}

try {
  Write-Step "Installing to $InstallDir"
  Expand-Package -ZipPath $zip -Destination $InstallDir

  $exePath = Join-Path $InstallDir "VibeCodingAlwaysOnTray.exe"

  Install-Shortcuts -ExePath $exePath

  if ($InstallHelper) {
    Write-Step "Installing no-prompt helper. Windows will ask for administrator permission once."
    Invoke-ElevatedExeAction -ExePath $exePath -Action "install-task"
  }

  if ($Enable) {
    if (Test-HelperInstalled) {
      Write-Step "Enabling always-on mode through no-prompt helper"
      Invoke-HelperAction -Action "enable"
    }
    else {
      Write-Step "Enabling always-on mode. Windows may ask for administrator permission."
      Invoke-ElevatedExeAction -ExePath $exePath -Action "enable"
    }
  }

  if (-not $NoLaunch) {
    Write-Step "Starting tray app"
    Start-Process -FilePath $exePath | Out-Null
  }

  Write-Step "Done"
  Write-Host ""
  Write-Host "Installed: $exePath"
  Write-Host "Tip: right-click the tray icon for enable, restore, helper install, and exit."
}
finally {
  if ($downloadedZip) {
    Remove-Item $downloadedZip -Force -ErrorAction SilentlyContinue
  }
}
