# Vibe Coding Always-On

一个极简 Windows 托盘开关：让 Codex、Claude Code、Cursor 等长任务跑着时，电脑不锁屏、不睡眠、合盖不断线。

开启前会备份原电源设置。需要恢复时，右键托盘图标一键恢复。

## 安装

推荐这一行，不需要 npm 登录，也不需要 Git：

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --enable
```

如果想以后不反复弹 Windows 权限确认，用 helper 版本：

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --helper --enable
```

只用 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/KujalaLucien/vibe-coding-always-on/main/install.ps1 -OutFile $env:TEMP\vcao-install.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\vcao-install.ps1 -Repo KujalaLucien/vibe-coding-always-on -Enable"
```

## 下次怎么打开

默认安装到：

```text
%LOCALAPPDATA%\Programs\VibeCodingAlwaysOn\VibeCodingAlwaysOnTray.exe
```

安装后会创建：

- 桌面快捷方式：`Vibe Coding Always-On`
- 开始菜单快捷方式：`Vibe Coding Always-On`

不想要桌面图标：

```powershell
npx --yes --package="https://github.com/KujalaLucien/vibe-coding-always-on/releases/latest/download/vibe-coding-always-on.tgz?v=1.3.4" vcao install --enable --no-desktop
```

## 开启后做什么

对当前 Windows 电源方案同时修改插电和电池模式：

- 关闭显示器：永不
- 睡眠：永不
- 休眠：永不
- 合盖动作：不执行任何操作
- 混合睡眠：关闭，如果系统支持
- 无人值守睡眠：永不，如果系统支持
- 唤醒密码 / 锁屏显示器超时：系统暴露该项时尽量关闭

备份位置：

```text
%APPDATA%\VibeCodingAlwaysOn\power-settings-backup.json
```

恢复后的历史备份：

```text
%APPDATA%\VibeCodingAlwaysOn\history\
```

## 常用命令

```powershell
vcao install --enable
vcao install --helper --enable
vcao start
vcao path
vcao uninstall
```

`--helper` 不是绕过 Windows 安全机制。它会在你确认一次管理员权限后创建计划任务，之后开启和恢复就不用每次确认。

## 构建

```powershell
.\scripts\build.ps1
.\scripts\release.ps1 -NpmPack
```

用户不需要安装 .NET SDK。

## 注意

企业策略、OEM 电源工具、BIOS 设置、Modern Standby 固件策略可能覆盖 Windows 电源方案。

当前程序未签名，首次运行可能触发 SmartScreen 提示。

## License

MIT
