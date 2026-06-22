# Vibe Coding Always-On

Tiny Windows tray switch for long-running coding and agent sessions.

It keeps your laptop awake, prevents display/sleep/hibernate timeouts, and makes lid-close do nothing. One click restores your previous power settings.

[中文说明](README.zh-CN.md)

## Install

No npm account or Git setup required:

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --enable
```

Install the one-time helper to avoid repeated Windows permission prompts:

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --helper --enable
```

Prefer PowerShell only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/KujalaLucien/vibe-coding-always-on/main/install.ps1 -OutFile $env:TEMP\vcao-install.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\vcao-install.ps1 -Repo KujalaLucien/vibe-coding-always-on -Enable"
```

## Open Later

The app installs to:

```text
%LOCALAPPDATA%\Programs\VibeCodingAlwaysOn\VibeCodingAlwaysOnTray.exe
```

Install creates:

- Desktop shortcut: `Vibe Coding Always-On`
- Start Menu shortcut: `Vibe Coding Always-On`

Or launch it directly:

```powershell
& "$env:LOCALAPPDATA\Programs\VibeCodingAlwaysOn\VibeCodingAlwaysOnTray.exe"
```

## What It Changes

When enabled, the active Windows power plan is changed for both AC and battery:

- display timeout: never
- sleep timeout: never
- hibernate timeout: never
- lid close action: do nothing
- hybrid sleep: off, when supported
- unattended sleep timeout: never, when supported
- wake password / lock display timeout: relaxed when Windows exposes the setting

The first enable writes a backup to:

```text
%APPDATA%\VibeCodingAlwaysOn\power-settings-backup.json
```

## Restore

Right-click the tray icon and choose restore, or run the installed executable and use the tray menu.

Restored backups are archived under:

```text
%APPDATA%\VibeCodingAlwaysOn\history\
```

## CLI

```powershell
vcao install --enable
vcao install --helper --enable
vcao install --enable --no-desktop
vcao start
vcao path
vcao uninstall
```

`--helper` still needs one administrator confirmation because it creates a Windows scheduled task. It avoids repeated prompts after that.

## Build

```powershell
.\scripts\build.ps1
.\scripts\release.ps1 -NpmPack
```

The project builds with the .NET Framework compiler that ships with Windows. No .NET SDK is required for users.

## Notes

Some corporate policies, OEM power tools, BIOS settings, or Modern Standby firmware can override Windows power settings.

The app is unsigned today, so Windows SmartScreen may warn on first run.

## License

MIT
