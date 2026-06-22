#!/usr/bin/env node
"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync, spawn } = require("child_process");

const ROOT = path.resolve(__dirname, "..", "..");
const BUNDLED_EXE = path.join(ROOT, "dist", "VibeCodingAlwaysOnTray.exe");
const INSTALL_DIR = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "Programs", "VibeCodingAlwaysOn");
const INSTALLED_EXE = path.join(INSTALL_DIR, "VibeCodingAlwaysOnTray.exe");

function main() {
  const args = process.argv.slice(2);
  const command = args[0] || "help";
  const flags = parseFlags(args.slice(1));

  if (process.platform !== "win32") {
    fail("Vibe Coding Always-On only supports Windows.");
  }

  if (command === "help" || command === "--help" || command === "-h") {
    printHelp();
    return;
  }

  if (command === "install") {
    install(flags);
    return;
  }

  if (command === "start") {
    startInstalled();
    return;
  }

  if (command === "selftest") {
    selftest(flags);
    return;
  }

  if (command === "path") {
    console.log(INSTALLED_EXE);
    return;
  }

  if (command === "uninstall") {
    uninstall(flags);
    return;
  }

  fail(`Unknown command: ${command}`);
}

function parseFlags(args) {
  const flags = {
    enable: false,
    helper: false,
    launch: true,
    shortcuts: true,
    desktopShortcut: true,
    startMenuShortcut: true,
    bundled: false,
    dryRun: false,
    installDir: INSTALL_DIR
  };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === "--enable") flags.enable = true;
    else if (arg === "--helper" || arg === "--install-helper") flags.helper = true;
    else if (arg === "--no-launch") flags.launch = false;
    else if (arg === "--no-shortcuts") flags.shortcuts = false;
    else if (arg === "--no-desktop" || arg === "--no-desktop-shortcut") flags.desktopShortcut = false;
    else if (arg === "--no-start-menu" || arg === "--no-start-menu-shortcut") flags.startMenuShortcut = false;
    else if (arg === "--bundled") flags.bundled = true;
    else if (arg === "--dry-run") flags.dryRun = true;
    else if ((arg === "--dir" || arg === "--install-dir") && i + 1 < args.length) flags.installDir = path.resolve(args[++i]);
    else fail(`Unknown flag: ${arg}`);
  }

  return flags;
}

function install(flags) {
  ensureBundledExe();
  const targetExe = path.join(flags.installDir, "VibeCodingAlwaysOnTray.exe");

  if (flags.dryRun) {
    console.log(`[vcao] Would copy ${BUNDLED_EXE}`);
    console.log(`[vcao] To ${targetExe}`);
    if (flags.shortcuts) {
      if (flags.startMenuShortcut) console.log("[vcao] Would create Start Menu shortcut.");
      if (flags.desktopShortcut) console.log("[vcao] Would create desktop shortcut.");
    }
    if (flags.helper) console.log("[vcao] Would install no-prompt helper with one Windows permission prompt.");
    if (flags.enable) console.log("[vcao] Would enable always-on mode.");
    if (flags.launch) console.log("[vcao] Would start tray app.");
    return;
  }

  fs.mkdirSync(flags.installDir, { recursive: true });
  fs.copyFileSync(BUNDLED_EXE, targetExe);
  copyIfExists(path.join(ROOT, "README.md"), path.join(flags.installDir, "README.md"));
  copyIfExists(path.join(ROOT, "README.zh-CN.md"), path.join(flags.installDir, "README.zh-CN.md"));
  copyIfExists(path.join(ROOT, "RELEASE_NOTES.md"), path.join(flags.installDir, "RELEASE_NOTES.md"));
  copyIfExists(path.join(ROOT, "LICENSE"), path.join(flags.installDir, "LICENSE"));

  console.log(`[vcao] Installed to ${targetExe}`);
  installShortcuts(targetExe, flags);

  if (flags.helper) {
    console.log("[vcao] Installing no-prompt helper. Windows will ask for administrator permission once.");
    runElevated(targetExe, "install-task");
  }

  if (flags.enable) {
    if (isHelperInstalled()) {
      console.log("[vcao] Enabling always-on mode through no-prompt helper.");
      runHelper("enable");
    } else {
      console.log("[vcao] Enabling always-on mode. Windows may ask for administrator permission.");
      runElevated(targetExe, "enable");
    }
  }

  if (flags.launch) {
    start(targetExe);
    console.log("[vcao] Started tray app.");
  }
}

function uninstall(flags) {
  if (flags.dryRun) {
    console.log(`[vcao] Would remove ${flags.installDir}`);
    return;
  }

  if (fs.existsSync(flags.installDir)) {
    fs.rmSync(flags.installDir, { recursive: true, force: true });
  }
  removeShortcuts();
  console.log(`[vcao] Removed ${flags.installDir}`);
}

function startInstalled() {
  if (!fs.existsSync(INSTALLED_EXE)) {
    fail(`Installed app not found. Run: npx vibe-coding-always-on install`);
  }
  start(INSTALLED_EXE);
  console.log("[vcao] Started tray app.");
}

function selftest(flags) {
  const exe = flags.bundled ? BUNDLED_EXE : INSTALLED_EXE;
  if (!fs.existsSync(exe)) {
    fail(`Executable not found: ${exe}`);
  }

  const result = path.join(os.tmpdir(), `vcao-selftest-${Date.now()}.txt`);
  const run = spawnSync(exe, ["--selftest", "--result", result], { windowsHide: true, encoding: "utf8" });
  if (run.error) fail(run.error.message);
  if (fs.existsSync(result)) {
    process.stdout.write(fs.readFileSync(result, "utf8"));
    fs.rmSync(result, { force: true });
  }
  process.exitCode = run.status || 0;
}

function runElevated(exe, action) {
  const appDir = path.join(process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"), "VibeCodingAlwaysOn");
  const sidScript = "[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value";
  const sid = spawnSync("powershell.exe", ["-NoProfile", "-Command", sidScript], { encoding: "utf8", windowsHide: true }).stdout.trim();
  const args = `--${action} --appdir "${appDir}" --usersid "${sid}"`;
  const ps = `Start-Process -FilePath "${escapePowerShell(exe)}" -ArgumentList '${args.replace(/'/g, "''")}' -Verb RunAs -WindowStyle Hidden -Wait`;
  const result = spawnSync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps], { stdio: "inherit", windowsHide: false });
  if (result.status !== 0) {
    fail(`Elevated action failed: ${action}`);
  }
}

function isHelperInstalled() {
  const result = spawnSync("schtasks.exe", ["/Query", "/TN", "VibeCodingAlwaysOnTrayHelper"], {
    encoding: "utf8",
    windowsHide: true,
    stdio: "ignore"
  });
  return result.status === 0;
}

function runHelper(action) {
  const appDir = path.join(process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"), "VibeCodingAlwaysOn");
  fs.mkdirSync(appDir, { recursive: true });

  const requestId = randomId();
  const requestPath = path.join(appDir, "helper-request.txt");
  const resultPath = path.join(appDir, "helper-result.txt");

  fs.writeFileSync(requestPath, `${requestId}\n${action}`, "utf8");
  if (fs.existsSync(resultPath)) fs.rmSync(resultPath, { force: true });

  const run = spawnSync("schtasks.exe", ["/Run", "/TN", "VibeCodingAlwaysOnTrayHelper"], {
    encoding: "utf8",
    windowsHide: true
  });
  if (run.status !== 0) {
    fail(`No-prompt helper task failed to start.\n${run.stdout || ""}${run.stderr || ""}`);
  }

  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (fs.existsSync(resultPath)) {
      const lines = fs.readFileSync(resultPath, "utf8").split(/\r?\n/);
      if (lines.length >= 2 && lines[0].trim().toLowerCase() === requestId.toLowerCase()) {
        const ok = lines[1].trim().toUpperCase() === "OK";
        const message = lines.slice(2).join("\n").trim();
        if (!ok) {
          fail(message || "No-prompt helper action failed.");
        }
        return;
      }
    }

    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 200);
  }

  fail("No-prompt helper did not return in time.");
}

function start(exe) {
  const child = spawn(exe, [], {
    detached: true,
    stdio: "ignore",
    windowsHide: true
  });
  child.unref();
}

function installShortcuts(exe, flags) {
  if (!flags.shortcuts) return;

  const createStartMenu = flags.startMenuShortcut;
  const createDesktop = flags.desktopShortcut;
  if (!createStartMenu && !createDesktop) return;

  const script = `
$ErrorActionPreference = "Stop"
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
$exePath = ${psQuote(exe)}
$description = "Keep Windows awake for long-running coding and agent sessions."
if (${createStartMenu ? "$true" : "$false"}) {
  $programs = [Environment]::GetFolderPath("Programs")
  New-AppShortcut -ShortcutPath (Join-Path $programs "Vibe Coding Always-On\\Vibe Coding Always-On.lnk") -TargetPath $exePath -Description $description
}
if (${createDesktop ? "$true" : "$false"}) {
  $desktop = [Environment]::GetFolderPath("DesktopDirectory")
  New-AppShortcut -ShortcutPath (Join-Path $desktop "Vibe Coding Always-On.lnk") -TargetPath $exePath -Description $description
}
`;

  const result = runPowerShell(script);
  if (result.status === 0) {
    if (createStartMenu) console.log("[vcao] Created Start Menu shortcut.");
    if (createDesktop) console.log("[vcao] Created desktop shortcut.");
  } else {
    console.warn("[vcao] Installed, but shortcut creation failed.");
    if (result.stderr) process.stderr.write(result.stderr);
  }
}

function removeShortcuts() {
  const script = `
$paths = @(
  (Join-Path ([Environment]::GetFolderPath("Programs")) "Vibe Coding Always-On\\Vibe Coding Always-On.lnk"),
  (Join-Path ([Environment]::GetFolderPath("Programs")) "Vibe Coding Always-On"),
  (Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Vibe Coding Always-On.lnk")
)
foreach ($path in $paths) {
  Remove-Item -LiteralPath $path -Force -Recurse -ErrorAction SilentlyContinue
}
`;
  runPowerShell(script);
}

function runPowerShell(script) {
  const encoded = Buffer.from(script, "utf16le").toString("base64");
  return spawnSync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], {
    encoding: "utf8",
    windowsHide: true
  });
}

function copyIfExists(from, to) {
  if (fs.existsSync(from)) {
    fs.copyFileSync(from, to);
  }
}

function ensureBundledExe() {
  if (!fs.existsSync(BUNDLED_EXE)) {
    fail(`Bundled executable not found: ${BUNDLED_EXE}\nRun npm run build before packing/publishing.`);
  }
}

function escapePowerShell(value) {
  return value.replace(/"/g, "`\"");
}

function psQuote(value) {
  return `'${value.replace(/'/g, "''")}'`;
}

function randomId() {
  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`;
}

function fail(message) {
  console.error(`[vcao] ${message}`);
  process.exit(1);
}

function printHelp() {
  console.log(`Vibe Coding Always-On

Usage:
  vcao install [--enable] [--helper] [--no-launch] [--no-shortcuts] [--no-desktop] [--dir <path>]
  vcao start
  vcao selftest [--bundled]
  vcao path
  vcao uninstall

Examples:
  npx vibe-coding-always-on install
  npx vibe-coding-always-on install --enable
  npx vibe-coding-always-on install --helper --enable

Notes:
  install creates Desktop and Start Menu shortcuts by default.
  --enable may show a Windows administrator permission prompt.
  --helper installs the no-prompt scheduled task after one administrator confirmation.
`);
}

main();
