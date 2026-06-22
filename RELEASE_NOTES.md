# Vibe Coding Always-On 1.3.4

Tiny Windows tray switch for keeping coding and agent sessions alive.

## Highlights

- Keep Windows awake, prevent sleep/hibernate, and keep lid-close as "do nothing".
- Restore previous power settings from the tray menu.
- Install with one npx or PowerShell command.
- Desktop and Start Menu shortcuts are created by default.
- Optional one-time helper avoids repeated permission prompts.

## Install

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --enable
```

## Notes

The app is unsigned, so SmartScreen may warn on first run. Some enterprise/OEM power policies can override Windows settings.
