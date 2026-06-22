using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyTitle("Vibe Coding Always-On Tray")]
[assembly: AssemblyDescription("Tray utility for Windows laptop always-on coding sessions.")]
[assembly: AssemblyCompany("Codex")]
[assembly: AssemblyProduct("Vibe Coding Always-On")]
[assembly: AssemblyVersion("1.3.4.0")]
[assembly: AssemblyFileVersion("1.3.4.0")]

namespace VibeCodingAlwaysOnTray
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            DpiHelper.EnableHighDpi();
            CommandLine options = CommandLine.Parse(args);

            if (!string.IsNullOrEmpty(options.Action) || options.InstallTask || options.TaskWorker)
            {
                return RunCommand(options);
            }

            bool createdNew;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "VibeCodingAlwaysOnTray.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Vibe Coding Always-On 已经在右下角状态栏运行。", "Vibe Coding Always-On", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayAppContext());
            }

            return 0;
        }

        private static int RunCommand(CommandLine options)
        {
            try
            {
                PowerManager manager = new PowerManager(options.AppDir, options.UserSid);
                string message;

                if (options.InstallTask)
                {
                    ScheduledTaskManager taskManager = new ScheduledTaskManager(manager.AppDir, options.UserSid);
                    message = taskManager.InstallOrUpdate();
                }
                else if (options.TaskWorker)
                {
                    ScheduledTaskManager taskManager = new ScheduledTaskManager(manager.AppDir, options.UserSid);
                    message = taskManager.RunWorker(manager);
                }
                else if (string.Equals(options.Action, "enable", StringComparison.OrdinalIgnoreCase))
                {
                    message = manager.EnableAlwaysOn();
                }
                else if (string.Equals(options.Action, "restore", StringComparison.OrdinalIgnoreCase))
                {
                    message = manager.RestorePreviousSettings();
                }
                else if (string.Equals(options.Action, "selftest", StringComparison.OrdinalIgnoreCase))
                {
                    message = manager.BuildSelfTestReport();
                }
                else
                {
                    throw new InvalidOperationException("Unknown action: " + options.Action);
                }

                ResultFile.Write(options.ResultPath, true, message);
                return 0;
            }
            catch (Exception ex)
            {
                ResultFile.Write(options.ResultPath, false, ex.Message);
                return 1;
            }
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem enableItem;
        private readonly ToolStripMenuItem restoreItem;
        private readonly ToolStripMenuItem installTaskItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly PowerManager manager;
        private readonly ScheduledTaskManager taskManager;
        private readonly Timer startupTimer;

        public TrayAppContext()
        {
            manager = new PowerManager(PowerManager.DefaultAppDir, CurrentUserSid());
            taskManager = new ScheduledTaskManager(manager.AppDir, CurrentUserSid());

            menu = new ContextMenuStrip();
            menu.Font = new Font("Segoe UI", 9F);
            enableItem = new ToolStripMenuItem("开启全天候在线");
            restoreItem = new ToolStripMenuItem("关闭并恢复原设置");
            installTaskItem = new ToolStripMenuItem("安装免确认模式（一次）");
            exitItem = new ToolStripMenuItem("退出");

            enableItem.Click += delegate { RunPowerAction("enable", false); };
            restoreItem.Click += delegate { RunPowerAction("restore", false); };
            installTaskItem.Click += delegate { InstallNoPromptMode(); };
            exitItem.Click += delegate { ExitThread(); };

            menu.Items.Add(enableItem);
            menu.Items.Add(restoreItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(installTaskItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            menu.Opening += delegate { RefreshMenuState(); };

            notifyIcon = new NotifyIcon();
            notifyIcon.Text = "Vibe Coding Always-On";
            notifyIcon.Icon = TrayIconFactory.Create(false);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += delegate { menu.Show(Cursor.Position); };

            RefreshMenuState();
            ToastNotifier.Show("Vibe Coding Always-On", "已在右下角状态栏运行。右键图标可开启、恢复或退出。", ToastKind.Info);

            startupTimer = new Timer();
            startupTimer.Interval = 700;
            startupTimer.Tick += delegate
            {
                startupTimer.Stop();
                AutoEnableOnLaunchIfReady();
            };
            startupTimer.Start();
        }

        private void RefreshMenuState()
        {
            bool backupExists = manager.BackupExists();
            bool looksOn = manager.AlwaysOnLooksEnabled();
            bool taskInstalled = taskManager.IsInstalled();

            enableItem.Enabled = !looksOn;
            restoreItem.Enabled = backupExists;
            installTaskItem.Visible = !taskInstalled;
            installTaskItem.Enabled = !taskInstalled;
            notifyIcon.Icon = TrayIconFactory.Create(looksOn);
            notifyIcon.Text = looksOn ? "Vibe Coding Always-On: 已开启" : "Vibe Coding Always-On: 未开启";
        }

        private void AutoEnableOnLaunchIfReady()
        {
            if (manager.AlwaysOnLooksEnabled())
            {
                return;
            }

            if (taskManager.IsInstalled())
            {
                RunPowerAction("enable", true);
            }
        }

        private void RunPowerAction(string action, bool automatic)
        {
            enableItem.Enabled = false;
            restoreItem.Enabled = false;
            installTaskItem.Enabled = false;

            string title = action == "enable" ? "正在开启" : "正在恢复";
            bool taskInstalled = taskManager.IsInstalled();
            ToastNotifier.Show("Vibe Coding Always-On", taskInstalled ? title + "。" : title + "，请确认 Windows 权限提示。", ToastKind.Info);

            try
            {
                ActionResult result = taskInstalled
                    ? taskManager.Run(action)
                    : ElevatedCommand.Run(action, manager.AppDir, CurrentUserSid());
                RefreshMenuState();

                if (result.Success)
                {
                    ToastNotifier.Show("Vibe Coding Always-On", automatic ? "已自动开启全天候在线。" : result.Message, ToastKind.Success);
                }
                else
                {
                    ToastNotifier.Show("Vibe Coding Always-On", result.Message, ToastKind.Error);
                }
            }
            catch (Win32Exception ex)
            {
                RefreshMenuState();
                if ((uint)ex.NativeErrorCode == 1223)
                {
                    ToastNotifier.Show("Vibe Coding Always-On", "已取消 Windows 权限确认。", ToastKind.Warning);
                }
                else
                {
                    ToastNotifier.Show("Vibe Coding Always-On", ex.Message, ToastKind.Error);
                }
            }
            catch (Exception ex)
            {
                RefreshMenuState();
                ToastNotifier.Show("Vibe Coding Always-On", ex.Message, ToastKind.Error);
            }
        }

        private void InstallNoPromptMode()
        {
            enableItem.Enabled = false;
            restoreItem.Enabled = false;
            installTaskItem.Enabled = false;
            ToastNotifier.Show("Vibe Coding Always-On", "正在安装免确认模式。此步骤只需要确认一次 Windows 权限。", ToastKind.Info);

            try
            {
                ActionResult result = ElevatedCommand.Run("install-task", manager.AppDir, CurrentUserSid());
                RefreshMenuState();

                if (result.Success)
                {
                    ToastNotifier.Show("Vibe Coding Always-On", "免确认模式已安装。正在开启全天候在线。", ToastKind.Success);
                    RunPowerAction("enable", true);
                }
                else
                {
                    ToastNotifier.Show("Vibe Coding Always-On", result.Message, ToastKind.Error);
                }
            }
            catch (Win32Exception ex)
            {
                RefreshMenuState();
                if ((uint)ex.NativeErrorCode == 1223)
                {
                    ToastNotifier.Show("Vibe Coding Always-On", "已取消 Windows 权限确认。", ToastKind.Warning);
                }
                else
                {
                    ToastNotifier.Show("Vibe Coding Always-On", ex.Message, ToastKind.Error);
                }
            }
            catch (Exception ex)
            {
                RefreshMenuState();
                ToastNotifier.Show("Vibe Coding Always-On", ex.Message, ToastKind.Error);
            }
        }

        protected override void ExitThreadCore()
        {
            startupTimer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            menu.Dispose();
            base.ExitThreadCore();
        }

        private static string CurrentUserSid()
        {
            return WindowsIdentity.GetCurrent().User.Value;
        }
    }

    internal static class ElevatedCommand
    {
        public static ActionResult Run(string action, string appDir, string userSid)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string resultPath = Path.Combine(Path.GetTempPath(), "VibeCodingAlwaysOn-" + Guid.NewGuid().ToString("N") + ".txt");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.Arguments = "--" + action + " --appdir " + Quote(appDir) + " --usersid " + Quote(userSid) + " --result " + Quote(resultPath);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit();
            }

            ActionResult result = ResultFile.Read(resultPath);
            TryDelete(resultPath);
            return result;
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class ScheduledTaskManager
    {
        private const string TaskName = "VibeCodingAlwaysOnTrayHelper";
        private readonly string appDir;
        private readonly string userSid;
        private readonly string requestPath;
        private readonly string resultPath;

        public ScheduledTaskManager(string appDir, string userSid)
        {
            this.appDir = string.IsNullOrEmpty(appDir) ? PowerManager.DefaultAppDir : appDir;
            this.userSid = userSid;
            requestPath = Path.Combine(this.appDir, "helper-request.txt");
            resultPath = Path.Combine(this.appDir, "helper-result.txt");
        }

        public bool IsInstalled()
        {
            ProcessResult result = RunProcess("schtasks.exe", "/Query /TN " + Quote(TaskName));
            return result.ExitCode == 0;
        }

        public string InstallOrUpdate()
        {
            Directory.CreateDirectory(appDir);

            string exePath = Assembly.GetExecutingAssembly().Location;
            string workerArguments = "--task-worker --appdir " + Quote(appDir) + " --usersid " + Quote(userSid);
            string taskCommand = Quote(exePath) + " " + workerArguments;
            string arguments = "/Create /TN " + Quote(TaskName) + " /SC ONCE /ST 00:00 /RL HIGHEST /F /TR " + Quote(taskCommand);

            ProcessResult result = RunProcess("schtasks.exe", arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("免确认模式安装失败。" + Environment.NewLine + result.Output.Trim());
            }

            return "免确认模式已安装。之后开启/恢复不再反复弹权限确认。";
        }

        public ActionResult Run(string action)
        {
            Directory.CreateDirectory(appDir);

            string requestId = Guid.NewGuid().ToString("N");
            File.WriteAllText(requestPath, requestId + Environment.NewLine + action, Encoding.UTF8);
            TryDelete(resultPath);

            ProcessResult runResult = RunProcess("schtasks.exe", "/Run /TN " + Quote(TaskName));
            if (runResult.ExitCode != 0)
            {
                ActionResult failed = new ActionResult();
                failed.Success = false;
                failed.Message = "免确认任务启动失败。" + Environment.NewLine + runResult.Output.Trim();
                return failed;
            }

            DateTime deadline = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < deadline)
            {
                ActionResult result = TryReadTaskResult(requestId);
                if (result != null)
                {
                    return result;
                }

                System.Threading.Thread.Sleep(200);
            }

            ActionResult timeout = new ActionResult();
            timeout.Success = false;
            timeout.Message = "免确认任务没有及时返回结果。可以右键重新安装免确认模式。";
            return timeout;
        }

        public string RunWorker(PowerManager manager)
        {
            TaskRequest request = ReadRequest();
            string message;

            try
            {
                if (string.Equals(request.Action, "enable", StringComparison.OrdinalIgnoreCase))
                {
                    message = manager.EnableAlwaysOn();
                    WriteTaskResult(request.Id, true, message);
                }
                else if (string.Equals(request.Action, "restore", StringComparison.OrdinalIgnoreCase))
                {
                    message = manager.RestorePreviousSettings();
                    WriteTaskResult(request.Id, true, message);
                }
                else
                {
                    throw new InvalidOperationException("Unknown scheduled action: " + request.Action);
                }
            }
            catch (Exception ex)
            {
                WriteTaskResult(request.Id, false, ex.Message);
                throw;
            }

            return message;
        }

        private TaskRequest ReadRequest()
        {
            if (!File.Exists(requestPath))
            {
                throw new InvalidOperationException("No helper request found.");
            }

            string[] lines = File.ReadAllLines(requestPath, Encoding.UTF8);
            if (lines.Length < 2 || string.IsNullOrWhiteSpace(lines[0]) || string.IsNullOrWhiteSpace(lines[1]))
            {
                throw new InvalidOperationException("Helper request is invalid.");
            }

            TaskRequest request = new TaskRequest();
            request.Id = lines[0].Trim();
            request.Action = lines[1].Trim();
            return request;
        }

        private void WriteTaskResult(string requestId, bool success, string message)
        {
            File.WriteAllText(resultPath, requestId + Environment.NewLine + (success ? "OK" : "ERROR") + Environment.NewLine + (message ?? string.Empty), Encoding.UTF8);
        }

        private ActionResult TryReadTaskResult(string requestId)
        {
            try
            {
                if (!File.Exists(resultPath))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(resultPath, Encoding.UTF8);
                if (lines.Length < 2 || !string.Equals(lines[0].Trim(), requestId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                ActionResult result = new ActionResult();
                result.Success = string.Equals(lines[1].Trim(), "OK", StringComparison.OrdinalIgnoreCase);
                result.Message = lines.Length > 2 ? string.Join(Environment.NewLine, lines, 2, lines.Length - 2).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    result.Message = result.Success ? "操作已完成。" : "操作失败。";
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static ProcessResult RunProcess(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.Default;
            psi.StandardErrorEncoding = Encoding.Default;

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                ProcessResult result = new ProcessResult();
                result.ExitCode = process.ExitCode;
                result.Output = (output ?? string.Empty) + (string.IsNullOrEmpty(error) ? string.Empty : Environment.NewLine + error);
                return result;
            }
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class TaskRequest
    {
        public string Id;
        public string Action;
    }

    internal sealed class PowerManager
    {
        public static readonly string DefaultAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeCodingAlwaysOn");

        private readonly string userSid;
        private readonly string backupPath;
        private readonly string historyDir;
        private readonly List<PowerSettingDefinition> settings;

        public string AppDir { get; private set; }

        public PowerManager(string appDir, string userSid)
        {
            AppDir = string.IsNullOrEmpty(appDir) ? DefaultAppDir : appDir;
            this.userSid = userSid;
            backupPath = Path.Combine(AppDir, "power-settings-backup.json");
            historyDir = Path.Combine(AppDir, "history");
            settings = BuildSettings();
        }

        public bool BackupExists()
        {
            return File.Exists(backupPath) && BackupFileUsable(backupPath);
        }

        public bool AlwaysOnLooksEnabled()
        {
            try
            {
                string scheme = GetActiveSchemeGuid();
                foreach (PowerSettingDefinition setting in settings)
                {
                    if (!setting.Required)
                    {
                        continue;
                    }

                    PowerSettingState state = GetPowerSettingState(scheme, setting);
                    if (!state.Exists || state.AC.GetValueOrDefault(-1) != setting.AcTarget || state.DC.GetValueOrDefault(-1) != setting.DcTarget)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public string EnableAlwaysOn()
        {
            bool backupCreated = SaveBackupIfNeeded();
            string scheme = GetActiveSchemeGuid();
            List<string> warnings = new List<string>();

            foreach (PowerSettingDefinition setting in settings)
            {
                try
                {
                    PowerSettingState state = GetPowerSettingState(scheme, setting);
                    if (!state.Exists)
                    {
                        if (setting.Required)
                        {
                            throw new InvalidOperationException(setting.Label + " is unavailable on this device.");
                        }

                        warnings.Add(setting.Label + " skipped.");
                        continue;
                    }

                    SetPowerSettingPair(scheme, setting, setting.AcTarget, setting.DcTarget);
                }
                catch
                {
                    if (setting.Required)
                    {
                        throw;
                    }

                    warnings.Add(setting.Label + " skipped.");
                }
            }

            DisableScreenSaverLock();
            SetActiveScheme(scheme);

            if (warnings.Count > 0)
            {
                return backupCreated ? "已开启全天候在线。已保存原设置；部分可选项已跳过。" : "已开启全天候在线。部分可选项已跳过。";
            }

            return backupCreated ? "已开启全天候在线。已保存原设置。" : "已开启全天候在线。沿用已有备份。";
        }

        public string RestorePreviousSettings()
        {
            if (!File.Exists(backupPath))
            {
                throw new InvalidOperationException("没有找到可恢复的备份。请先开启一次全天候在线。");
            }

            if (!BackupFileUsable(backupPath))
            {
                throw new InvalidOperationException("备份文件已损坏。请重新开启一次以创建新备份。");
            }

            PowerBackup backup = ReadBackup(backupPath);
            string scheme = backup.ActiveSchemeGuid;
            List<string> warnings = new List<string>();

            foreach (PowerSettingState saved in backup.PowerSettings)
            {
                PowerSettingDefinition setting = FindSetting(saved.Key);
                if (setting == null || !saved.Exists)
                {
                    continue;
                }

                try
                {
                    SetPowerSettingPair(scheme, setting, saved.AC, saved.DC);
                }
                catch
                {
                    if (setting.Required)
                    {
                        throw;
                    }

                    warnings.Add(setting.Label + " restore skipped.");
                }
            }

            if (backup.ScreenSaver != null)
            {
                RestoreScreenSaverBackup(backup.ScreenSaver);
            }

            SetActiveScheme(scheme);
            ArchiveBackup("restored");

            return warnings.Count > 0 ? "已恢复原设置，部分可选项已跳过。" : "已恢复原设置。";
        }

        public string BuildSelfTestReport()
        {
            string scheme = GetActiveSchemeGuid();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("scheme=" + scheme);

            foreach (PowerSettingDefinition setting in settings)
            {
                PowerSettingState state = GetPowerSettingState(scheme, setting);
                builder.AppendLine(setting.Key + ": exists=" + state.Exists + ", AC=" + NullableLongToText(state.AC) + ", DC=" + NullableLongToText(state.DC));
            }

            return builder.ToString();
        }

        private bool SaveBackupIfNeeded()
        {
            EnsureDirs();
            if (File.Exists(backupPath))
            {
                if (BackupFileUsable(backupPath))
                {
                    return false;
                }

                ArchiveBackup("corrupt");
            }

            string scheme = GetActiveSchemeGuid();
            PowerBackup backup = new PowerBackup();
            backup.Version = 2;
            backup.CreatedAt = DateTimeOffset.Now.ToString("o");
            backup.ActiveSchemeGuid = scheme;
            backup.PowerSettings = new List<PowerSettingState>();
            backup.ScreenSaver = GetScreenSaverBackup();

            foreach (PowerSettingDefinition setting in settings)
            {
                backup.PowerSettings.Add(GetPowerSettingState(scheme, setting));
            }

            WriteBackup(backupPath, backup);
            return true;
        }

        private void EnsureDirs()
        {
            Directory.CreateDirectory(AppDir);
            Directory.CreateDirectory(historyDir);
        }

        private void ArchiveBackup(string reason)
        {
            if (!File.Exists(backupPath))
            {
                return;
            }

            EnsureDirs();
            string archivePath = Path.Combine(historyDir, reason + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.Move(backupPath, archivePath);
        }

        private bool BackupFileUsable(string path)
        {
            try
            {
                PowerBackup backup = ReadBackup(path);
                return backup != null &&
                    backup.Version > 0 &&
                    !string.IsNullOrWhiteSpace(backup.ActiveSchemeGuid) &&
                    backup.PowerSettings != null;
            }
            catch
            {
                return false;
            }
        }

        private PowerSettingDefinition FindSetting(string key)
        {
            foreach (PowerSettingDefinition setting in settings)
            {
                if (string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return setting;
                }
            }

            return null;
        }

        private static List<PowerSettingDefinition> BuildSettings()
        {
            List<PowerSettingDefinition> list = new List<PowerSettingDefinition>();
            list.Add(new PowerSettingDefinition("DisplayTimeout", "Turn off display after", "7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", 0, 0, true));
            list.Add(new PowerSettingDefinition("SleepTimeout", "Sleep after", "238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0, 0, true));
            list.Add(new PowerSettingDefinition("HibernateTimeout", "Hibernate after", "238c9fa8-0aad-41ed-83f4-97be242c8f20", "9d7815a6-7ee4-497e-8888-515a05f02364", 0, 0, true));
            list.Add(new PowerSettingDefinition("LidCloseAction", "Lid close action", "4f971e89-eebd-4455-a8de-9e59040e7347", "5ca83367-6e45-459f-a27b-476b1d01c936", 0, 0, true));
            list.Add(new PowerSettingDefinition("HybridSleep", "Allow hybrid sleep", "238c9fa8-0aad-41ed-83f4-97be242c8f20", "94ac6d29-73ce-41a6-809f-6363ba21b47e", 0, 0, false));
            list.Add(new PowerSettingDefinition("UnattendedSleepTimeout", "System unattended sleep timeout", "238c9fa8-0aad-41ed-83f4-97be242c8f20", "7bc4a2f9-d8fc-4469-b07b-33eb785aaca0", 0, 0, false));
            list.Add(new PowerSettingDefinition("PasswordOnWake", "Require password on wake", "fea3413e-7e05-4911-9a71-700331f1c294", "0e796bdb-100d-47d6-a2d5-f7d2daa51f51", 0, 0, false));
            list.Add(new PowerSettingDefinition("ConsoleLockDisplayTimeout", "Console lock display timeout", "7516b95f-f776-4464-8c53-06167f40cc99", "8ec4b3a5-6868-48c2-be75-4f3044be88a7", 0, 0, false));
            return list;
        }

        private string GetActiveSchemeGuid()
        {
            ProcessResult result = RunProcess("powercfg.exe", "/getactivescheme");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("Could not read active power scheme. " + result.Output);
            }

            Match match = Regex.Match(result.Output, "([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            if (!match.Success)
            {
                throw new InvalidOperationException("Could not parse active power scheme.");
            }

            return match.Groups[1].Value.ToLowerInvariant();
        }

        private PowerSettingState GetPowerSettingState(string schemeGuid, PowerSettingDefinition setting)
        {
            PowerSettingState state = new PowerSettingState();
            state.Key = setting.Key;
            state.Label = setting.Label;
            state.Exists = false;

            TryReadViaWmi(schemeGuid, setting, state);
            TryReadViaPowerCfgQuery(schemeGuid, setting, state);
            TryReadViaRegistry(schemeGuid, setting, state);

            return state;
        }

        private static void TryReadViaWmi(string schemeGuid, PowerSettingDefinition setting, PowerSettingState state)
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\cimv2\power");
                scope.Connect();

                ObjectQuery query = new ObjectQuery("SELECT InstanceID, SettingIndexValue FROM Win32_PowerSettingDataIndex");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject item in collection)
                    {
                        string instanceId = Convert.ToString(item["InstanceID"]);
                        if (instanceId == null)
                        {
                            continue;
                        }

                        if (instanceId.IndexOf(schemeGuid, StringComparison.OrdinalIgnoreCase) < 0 ||
                            instanceId.IndexOf(setting.SettingGuid, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        long value = Convert.ToInt64(item["SettingIndexValue"]);
                        state.Exists = true;

                        if (instanceId.IndexOf(@"\AC\", StringComparison.OrdinalIgnoreCase) >= 0 && !state.AC.HasValue)
                        {
                            state.AC = value;
                        }
                        else if (instanceId.IndexOf(@"\DC\", StringComparison.OrdinalIgnoreCase) >= 0 && !state.DC.HasValue)
                        {
                            state.DC = value;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryReadViaPowerCfgQuery(string schemeGuid, PowerSettingDefinition setting, PowerSettingState state)
        {
            try
            {
                ProcessResult result = RunProcess("powercfg.exe", "/qh " + schemeGuid + " " + setting.SubgroupGuid + " " + setting.SettingGuid);
                if (result.ExitCode != 0 || result.Output.IndexOf(setting.SettingGuid, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                state.Exists = true;
                if (!state.AC.HasValue)
                {
                    state.AC = ParsePowerCfgIndex(result.Output, true);
                }
                if (!state.DC.HasValue)
                {
                    state.DC = ParsePowerCfgIndex(result.Output, false);
                }
            }
            catch
            {
            }
        }

        private static void TryReadViaRegistry(string schemeGuid, PowerSettingDefinition setting, PowerSettingState state)
        {
            try
            {
                string path = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\" + schemeGuid + @"\" + setting.SubgroupGuid + @"\" + setting.SettingGuid;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, false))
                {
                    if (key == null)
                    {
                        return;
                    }

                    state.Exists = true;
                    if (!state.AC.HasValue)
                    {
                        object ac = key.GetValue("ACSettingIndex");
                        if (ac != null)
                        {
                            state.AC = Convert.ToInt64(ac);
                        }
                    }
                    if (!state.DC.HasValue)
                    {
                        object dc = key.GetValue("DCSettingIndex");
                        if (dc != null)
                        {
                            state.DC = Convert.ToInt64(dc);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static long? ParsePowerCfgIndex(string text, bool ac)
        {
            string[] patterns = ac
                ? new string[]
                {
                    @"Current\s+AC\s+Power\s+Setting\s+Index\s*:\s*(0x[0-9a-fA-F]+|\d+)",
                    @"当前交流电源设置索引\s*:\s*(0x[0-9a-fA-F]+|\d+)"
                }
                : new string[]
                {
                    @"Current\s+DC\s+Power\s+Setting\s+Index\s*:\s*(0x[0-9a-fA-F]+|\d+)",
                    @"当前直流电源设置索引\s*:\s*(0x[0-9a-fA-F]+|\d+)"
                };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        return Convert.ToInt64(value.Substring(2), 16);
                    }

                    return Convert.ToInt64(value);
                }
            }

            return null;
        }

        private static void SetPowerSettingPair(string schemeGuid, PowerSettingDefinition setting, long? ac, long? dc)
        {
            if (ac.HasValue)
            {
                RunPowerCfgSet("/setacvalueindex " + schemeGuid + " " + setting.SubgroupGuid + " " + setting.SettingGuid + " " + ac.Value, setting.Label + " AC");
            }
            if (dc.HasValue)
            {
                RunPowerCfgSet("/setdcvalueindex " + schemeGuid + " " + setting.SubgroupGuid + " " + setting.SettingGuid + " " + dc.Value, setting.Label + " DC");
            }
        }

        private static void SetActiveScheme(string schemeGuid)
        {
            RunPowerCfgSet("/setactive " + schemeGuid, "activate power scheme");
        }

        private static void RunPowerCfgSet(string arguments, string label)
        {
            ProcessResult result = RunProcess("powercfg.exe", arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(label + " failed. " + result.Output.Trim());
            }
        }

        private List<RegistryValueBackup> GetScreenSaverBackup()
        {
            List<RegistryValueBackup> list = new List<RegistryValueBackup>();
            string[] names = new string[] { "ScreenSaveActive", "ScreenSaverIsSecure", "ScreenSaveTimeOut" };

            using (RegistryKey key = OpenDesktopKey(false))
            {
                foreach (string name in names)
                {
                    RegistryValueBackup item = new RegistryValueBackup();
                    item.Name = name;
                    item.Exists = false;
                    item.Value = null;

                    if (key != null)
                    {
                        object value = key.GetValue(name, null);
                        if (value != null)
                        {
                            item.Exists = true;
                            item.Value = Convert.ToString(value);
                        }
                    }

                    list.Add(item);
                }
            }

            return list;
        }

        private void DisableScreenSaverLock()
        {
            using (RegistryKey key = OpenDesktopKey(true))
            {
                if (key == null)
                {
                    return;
                }

                key.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                key.SetValue("ScreenSaverIsSecure", "0", RegistryValueKind.String);
                key.SetValue("ScreenSaveTimeOut", "0", RegistryValueKind.String);
            }
        }

        private void RestoreScreenSaverBackup(List<RegistryValueBackup> backup)
        {
            using (RegistryKey key = OpenDesktopKey(true))
            {
                if (key == null)
                {
                    return;
                }

                foreach (RegistryValueBackup item in backup)
                {
                    if (item.Exists)
                    {
                        key.SetValue(item.Name, item.Value ?? string.Empty, RegistryValueKind.String);
                    }
                    else
                    {
                        try
                        {
                            key.DeleteValue(item.Name, false);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private RegistryKey OpenDesktopKey(bool writable)
        {
            if (!string.IsNullOrEmpty(userSid))
            {
                try
                {
                    return Registry.Users.OpenSubKey(userSid + @"\Control Panel\Desktop", writable);
                }
                catch
                {
                }
            }

            try
            {
                return Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable);
            }
            catch
            {
                return null;
            }
        }

        private static ProcessResult RunProcess(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.Default;
            psi.StandardErrorEncoding = Encoding.Default;

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                ProcessResult result = new ProcessResult();
                result.ExitCode = process.ExitCode;
                result.Output = (output ?? string.Empty) + (string.IsNullOrEmpty(error) ? string.Empty : Environment.NewLine + error);
                return result;
            }
        }

        private static void WriteBackup(string path, PowerBackup backup)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PowerBackup));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, backup);
            }
        }

        private static PowerBackup ReadBackup(string path)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PowerBackup));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (PowerBackup)serializer.ReadObject(stream);
            }
        }

        private static string NullableLongToText(long? value)
        {
            return value.HasValue ? value.Value.ToString() : "null";
        }
    }

    internal sealed class PowerSettingDefinition
    {
        public string Key;
        public string Label;
        public string SubgroupGuid;
        public string SettingGuid;
        public long AcTarget;
        public long DcTarget;
        public bool Required;

        public PowerSettingDefinition(string key, string label, string subgroupGuid, string settingGuid, long acTarget, long dcTarget, bool required)
        {
            Key = key;
            Label = label;
            SubgroupGuid = subgroupGuid;
            SettingGuid = settingGuid;
            AcTarget = acTarget;
            DcTarget = dcTarget;
            Required = required;
        }
    }

    [DataContract]
    internal sealed class PowerBackup
    {
        [DataMember]
        public int Version { get; set; }

        [DataMember]
        public string CreatedAt { get; set; }

        [DataMember]
        public string ActiveSchemeGuid { get; set; }

        [DataMember]
        public List<PowerSettingState> PowerSettings { get; set; }

        [DataMember]
        public List<RegistryValueBackup> ScreenSaver { get; set; }
    }

    [DataContract]
    internal sealed class PowerSettingState
    {
        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Label { get; set; }

        [DataMember]
        public bool Exists { get; set; }

        [DataMember]
        public long? AC { get; set; }

        [DataMember]
        public long? DC { get; set; }
    }

    [DataContract]
    internal sealed class RegistryValueBackup
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public bool Exists { get; set; }

        [DataMember]
        public string Value { get; set; }
    }

    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string Output;
    }

    internal sealed class ActionResult
    {
        public bool Success;
        public string Message;
    }

    internal static class ResultFile
    {
        public static void Write(string path, bool success, string message)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, (success ? "OK" : "ERROR") + Environment.NewLine + (message ?? string.Empty), Encoding.UTF8);
        }

        public static ActionResult Read(string path)
        {
            ActionResult result = new ActionResult();

            if (!File.Exists(path))
            {
                result.Success = false;
                result.Message = "操作没有返回结果。";
                return result;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            using (StringReader reader = new StringReader(text))
            {
                string first = reader.ReadLine();
                string rest = reader.ReadToEnd();
                result.Success = string.Equals(first, "OK", StringComparison.OrdinalIgnoreCase);
                result.Message = string.IsNullOrWhiteSpace(rest) ? (result.Success ? "操作已完成。" : "操作失败。") : rest.Trim();
                return result;
            }
        }
    }

    internal sealed class CommandLine
    {
        public string Action;
        public string AppDir;
        public string UserSid;
        public string ResultPath;
        public bool InstallTask;
        public bool TaskWorker;

        public static CommandLine Parse(string[] args)
        {
            CommandLine options = new CommandLine();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--enable", StringComparison.OrdinalIgnoreCase))
                {
                    options.Action = "enable";
                }
                else if (string.Equals(arg, "--restore", StringComparison.OrdinalIgnoreCase))
                {
                    options.Action = "restore";
                }
                else if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    options.Action = "selftest";
                }
                else if (string.Equals(arg, "--install-task", StringComparison.OrdinalIgnoreCase))
                {
                    options.InstallTask = true;
                }
                else if (string.Equals(arg, "--task-worker", StringComparison.OrdinalIgnoreCase))
                {
                    options.TaskWorker = true;
                }
                else if (string.Equals(arg, "--appdir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.AppDir = args[++i];
                }
                else if (string.Equals(arg, "--usersid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.UserSid = args[++i];
                }
                else if (string.Equals(arg, "--result", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.ResultPath = args[++i];
                }
            }

            return options;
        }
    }

    internal static class DpiHelper
    {
        private static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new IntPtr(-4);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void EnableHighDpi()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorV2))
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }
    }

    internal enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    internal static class ToastNotifier
    {
        private static ToastWindow current;

        public static void Show(string title, string message, ToastKind kind)
        {
            try
            {
                if (current != null && !current.IsDisposed)
                {
                    current.Close();
                    current.Dispose();
                }

                current = new ToastWindow(title, message, kind);
                current.FormClosed += delegate(object sender, FormClosedEventArgs e)
                {
                    if (object.ReferenceEquals(current, sender))
                    {
                        current = null;
                    }
                };
                current.Show();
            }
            catch
            {
            }
        }
    }

    internal sealed class ToastWindow : Form
    {
        private readonly string title;
        private readonly string message;
        private readonly ToastKind kind;
        private readonly Timer closeTimer;

        public ToastWindow(string title, string message, ToastKind kind)
        {
            this.title = title ?? "Vibe Coding Always-On";
            this.message = message ?? string.Empty;
            this.kind = kind;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = ScaleSize(new Size(360, 96));
            Padding = ScalePadding(new Padding(18, 14, 18, 14));

            closeTimer = new Timer();
            closeTimer.Interval = 3200;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                Close();
            };

            PositionNearTray();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            closeTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (SolidBrush background = new SolidBrush(Color.FromArgb(250, 251, 252)))
            {
                g.FillRectangle(background, rect);
            }

            Color accent = AccentColor();
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                g.FillRectangle(accentBrush, 0, 0, Scale(5), Height);
            }

            using (Pen border = new Pen(Color.FromArgb(220, 224, 230)))
            {
                g.DrawRectangle(border, rect);
            }

            Rectangle content = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Left - Padding.Right, Height - Padding.Top - Padding.Bottom);
            using (Font titleFont = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(20, 24, 33)))
            using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(76, 86, 106)))
            {
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(content.Left, content.Top, content.Width, Scale(24)), titleBrush.Color, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, message, Font, new Rectangle(content.Left, content.Top + Scale(30), content.Width, content.Height - Scale(30)), bodyBrush.Color, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closeTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void PositionNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Right - Width - Scale(16), area.Bottom - Height - Scale(16));
        }

        private Color AccentColor()
        {
            if (kind == ToastKind.Success)
            {
                return Color.FromArgb(32, 153, 102);
            }
            if (kind == ToastKind.Warning)
            {
                return Color.FromArgb(230, 126, 34);
            }
            if (kind == ToastKind.Error)
            {
                return Color.FromArgb(214, 48, 49);
            }

            return Color.FromArgb(42, 130, 218);
        }

        private Size ScaleSize(Size size)
        {
            return new Size(Scale(size.Width), Scale(size.Height));
        }

        private Padding ScalePadding(Padding padding)
        {
            return new Padding(Scale(padding.Left), Scale(padding.Top), Scale(padding.Right), Scale(padding.Bottom));
        }

        private int Scale(int value)
        {
            using (Graphics g = CreateGraphics())
            {
                return (int)Math.Round(value * (g.DpiX / 96.0));
            }
        }
    }

    internal static class TrayIconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Create(bool enabled)
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color bg = enabled ? Color.FromArgb(32, 153, 102) : Color.FromArgb(42, 130, 218);
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }
                using (Pen pen = new Pen(Color.White, 3))
                {
                    if (enabled)
                    {
                        g.DrawLines(pen, new Point[] { new Point(9, 17), new Point(14, 22), new Point(23, 10) });
                    }
                    else
                    {
                        g.DrawLine(pen, 10, 9, 22, 23);
                        g.DrawLine(pen, 22, 9, 10, 23);
                    }
                }
            }

            IntPtr handle = bitmap.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            bitmap.Dispose();
            return icon;
        }
    }
}
