# Security Policy

## Reporting a Vulnerability

Please report security issues through GitHub Security Advisories if available, or by opening a private report channel with the maintainer.

Do not publish exploit details in a public issue before maintainers have had a reasonable chance to respond.

## Scope

This utility:

- changes local Windows power settings,
- writes backups under `%APPDATA%\VibeCodingAlwaysOn`,
- optionally creates a Windows scheduled task named `VibeCodingAlwaysOnTrayHelper`,
- does not use network access at runtime.

The installer and npm wrapper may download release assets only when explicitly used for installation.
