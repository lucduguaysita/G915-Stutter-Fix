using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace KeyboardRepeatFilter
{
    internal static class Program
    {
        private static KeyboardHookFilter _filter;
        private static NotifyIcon _notifyIcon;
        private static FilterConfig _config;
        private static DateTime _startedAtUtc;
        private static bool _shutdownLogged;
        private static Mutex _mutex;

        [STAThread]
        private static void Main()
        {
            const string appName = "KeyboardRepeatFilter";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running.
                MessageBox.Show("Another instance of Keyboard Repeat Filter is already running.", "Keyboard Repeat Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _config = LoadConfig();
            _startedAtUtc = DateTime.UtcNow;
            RegisterGlobalExceptionHandlers();
            Application.ApplicationExit += (_, __) => LogShutdown("ApplicationExit");

            LogLifecycle("Startup",
                $"version={Assembly.GetExecutingAssembly().GetName().Version}, pid={Process.GetCurrentProcess().Id}, minRepeatIntervalMs={_config.MinRepeatIntervalMs}");

            _filter = new KeyboardHookFilter(_config);
            _filter.Start();

            // Build tray menu
            var contextMenu = new ContextMenu();

            // --- Start with Windows toggle ---
            var startupItem = new MenuItem("Autostart")
            {
                Checked = StartupManager.IsInStartup()
            };
            startupItem.Click += (s, e) =>
            {
                StartupManager.ToggleStartup();
                startupItem.Checked = StartupManager.IsInStartup();
            };
            contextMenu.MenuItems.Add(startupItem);

            // --- About item ---
            var aboutMenuItem = new MenuItem("About...", OnAbout);
            contextMenu.MenuItems.Add(aboutMenuItem);

            // --- Exit item ---
            var exitMenuItem = new MenuItem("Exit", OnExit);
            contextMenu.MenuItems.Add(exitMenuItem);

            // Create tray icon
            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyboardRepeatFilter.app.ico")),
                Text = "Keyboard Repeat Filter",
                ContextMenu = contextMenu,
                Visible = true
            };
 
            Application.Run();

            // Release the mutex when the application exits.
            _mutex.ReleaseMutex();
        }


        private static void OnExit(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _filter.Stop();
            LogShutdown("UserExit");
            Application.Exit();
        }

        private static void OnAbout(object sender, EventArgs e)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "unknown";
            var repoUrl = "https://github.com/lucduguaysita/G915-Stutter-Fix";

            var aboutText =
                "G915 Stutter Fix\r\n" +
                $"Version: {version}\r\n\r\n" +
                "User-mode keyboard event filter for invalid HID repeats.\r\n\r\n" +
                $"Project: {repoUrl}\r\n" +
                "License: MIT\r\n\r\n" +
                "Open the GitHub project page?";

            var result = MessageBox.Show(
                aboutText,
                "About G915 Stutter Fix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = repoUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Keep tray app resilient if shell launch is unavailable.
                }
            }
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            Application.ThreadException += (sender, args) =>
            {
                LogShutdown("UnhandledThreadException", args.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogShutdown(args.IsTerminating ? "UnhandledExceptionTerminating" : "UnhandledException", ex);
            };
        }

        private static void LogShutdown(string reason, Exception ex = null)
        {
            if (_shutdownLogged)
            {
                return;
            }

            _shutdownLogged = true;
            var uptime = DateTime.UtcNow - _startedAtUtc;
            var message =
                $"reason={reason}, uptimeSec={uptime.TotalSeconds:F1}, pid={Process.GetCurrentProcess().Id}";

            if (ex != null)
            {
                message += $", exception={ex.GetType().Name}: {ex.Message}";
            }

            LogLifecycle("Shutdown", message);
        }

        private static void LogLifecycle(string phase, string details)
        {
            try
            {
                var logPath = _config?.LogFilePath;
                if (string.IsNullOrWhiteSpace(logPath))
                {
                    return;
                }

                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {phase}: {details}{Environment.NewLine}");
            }
            catch
            {
                // Keep tray app resilient; logging failures must not crash startup or shutdown.
            }
        }

        private static FilterConfig LoadConfig()
        {
            var configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "config.json");

            if (!File.Exists(configPath))
            {
                return new FilterConfig();
            }

            try
            {
                var json = File.ReadAllText(configPath);
                return JsonConvert.DeserializeObject<FilterConfig>(json) ?? new FilterConfig();
            }
            catch (Exception)
            {
                // Silently fail and use defaults for a tray application.
                // Consider adding logging here for diagnostics.
                return new FilterConfig();
            }
        }
    }
}
