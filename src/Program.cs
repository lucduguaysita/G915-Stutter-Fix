using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
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

        private static Icon _normalIcon;
        private static Icon _warningIcon;
        private static System.Windows.Forms.Timer _bypassTimer;
        private static bool _bypassActive;
        private static uint _lastToastPid;
        private static ToastForm _toast;

        private const string TooltipNormal = "Keyboard Repeat Filter";
        private const string TooltipBypassed = "Keyboard Repeat Filter - paused for this admin window";

        [STAThread]
        private static void Main()
        {
            const string appName = "KeyboardRepeatFilter";

            _mutex = new Mutex(false, appName);

            bool acquired;
            try
            {
                // Wait briefly rather than bailing immediately. During a "Restart as
                // administrator" handoff the old instance is still releasing the mutex
                // as the elevated one starts; this lets the new instance wait that out
                // instead of dying. A genuine second launch simply waits the timeout
                // and then exits below.
                acquired = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing; we now hold it.
                acquired = true;
            }

            if (!acquired)
            {
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

            // --- Restart as administrator ---
            // Only offered while running unelevated: a medium-integrity hook is
            // bypassed (UIPI) for elevated windows, so relaunching elevated is the
            // only way to filter their keystrokes. Hidden once already elevated.
            if (!IsElevated())
            {
                contextMenu.MenuItems.Add(new MenuItem("Restart as administrator", (s, e) => RestartAsAdmin()));
            }

            // --- Filter mode (radio choice, persisted to config.json) ---
            bool isReleaseMode = string.Equals(_config.FilterMode, "BlockRelease", StringComparison.OrdinalIgnoreCase);
            var filterModeMenu = new MenuItem("Filter mode");
            var repressItem = new MenuItem("Block double presses (default)") { RadioCheck = true, Checked = !isReleaseMode };
            var releaseItem = new MenuItem("Protect held keys (Ctrl, Shift)") { RadioCheck = true, Checked = isReleaseMode };
            repressItem.Click += (s, e) => ApplyFilterMode("BlockRepress", repressItem, releaseItem);
            releaseItem.Click += (s, e) => ApplyFilterMode("BlockRelease", repressItem, releaseItem);
            filterModeMenu.MenuItems.Add(repressItem);
            filterModeMenu.MenuItems.Add(releaseItem);
            contextMenu.MenuItems.Add(filterModeMenu);

            // --- Toggle the elevated-window popup (persisted to config.json) ---
            // Labelled as a "disable" action, so a checkmark means popups are off.
            var noticeItem = new MenuItem("Disable nag popups")
            {
                Checked = !_config.ShowElevatedWindowNotice
            };
            noticeItem.Click += (s, e) =>
            {
                _config.ShowElevatedWindowNotice = !_config.ShowElevatedWindowNotice;
                noticeItem.Checked = !_config.ShowElevatedWindowNotice;
                SaveConfig();
            };
            contextMenu.MenuItems.Add(noticeItem);

            // --- Keyboard Heatmap launchers ---
            var heatmapMenu = new MenuItem("Keyboard Heatmap");
            heatmapMenu.MenuItems.Add(new MenuItem("Generate report", (s, e) => LaunchHeatmap(verbose: false)));
            heatmapMenu.MenuItems.Add(new MenuItem("Generate report (verbose)", (s, e) => LaunchHeatmap(verbose: true)));
            contextMenu.MenuItems.Add(heatmapMenu);

            // --- About item ---
            var aboutMenuItem = new MenuItem("About...", OnAbout);
            contextMenu.MenuItems.Add(aboutMenuItem);

            // --- Exit item ---
            var exitMenuItem = new MenuItem("Exit", OnExit);
            contextMenu.MenuItems.Add(exitMenuItem);

            // Create tray icon
            _normalIcon = new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyboardRepeatFilter.app.ico"));
            _warningIcon = BuildWarningIcon(_normalIcon);

            _notifyIcon = new NotifyIcon
            {
                Icon = _normalIcon,
                Text = TooltipNormal,
                ContextMenu = contextMenu,
                Visible = true
            };

            // Ambient detection: while an elevated window is focused, our hook is
            // bypassed for it. Reflect that silently in the tray icon and tooltip.
            _bypassTimer = new System.Windows.Forms.Timer { Interval = 750 };
            _bypassTimer.Tick += (_, __) => UpdateBypassIndicator();
            _bypassTimer.Start();
            UpdateBypassIndicator();

            Application.Run();

            // Release the mutex when the application exits so a relaunch (e.g. the
            // elevated handoff) can acquire it promptly.
            try { _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
        }


        // True when this process is already running with administrator rights, in
        // which case the hook is not bypassed and no elevation is needed.
        private static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        // Relaunches the app elevated (UAC) so the keyboard hook can filter input
        // for administrator windows, then stands the current instance down so the
        // new one is not blocked by the single-instance mutex.
        private static void RestartAsAdmin()
        {
            if (IsElevated())
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true, // required to invoke a shell verb
                Verb = "runas"          // triggers the UAC consent prompt
            };

            try
            {
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User declined the UAC prompt (ERROR_CANCELLED, 1223). Keep running
                // unelevated rather than exiting.
                return;
            }
            catch (Exception ex)
            {
                LogLifecycle("ElevationError", ex.Message);
                return;
            }

            // The elevated instance is launching; release our hold so it can take
            // over without tripping the "already running" guard.
            LogLifecycle("Elevating", "relaunching with administrator rights at user request");
            _bypassTimer?.Stop();
            try { _toast?.Close(); } catch { /* ignore */ }
            _notifyIcon.Visible = false;
            _filter?.Stop();
            // The mutex is released by the tail of Main once Application.Run returns;
            // the elevated instance waits (see Main) for that release before starting.
            Application.Exit();
        }

        private static void OnExit(object sender, EventArgs e)
        {
            _bypassTimer?.Stop();
            try { _toast?.Close(); } catch { /* ignore */ }
            _notifyIcon.Visible = false;
            _filter.Stop();
            LogShutdown("UserExit");
            _warningIcon?.Dispose();
            _normalIcon?.Dispose();
            Application.Exit();
        }

        // Switches the active filter mode, updates the radio checkmarks, persists
        // the choice, and restarts the hook so the change takes effect at once.
        private static void ApplyFilterMode(string mode, MenuItem repressItem, MenuItem releaseItem)
        {
            if (string.Equals(_config.FilterMode, mode, StringComparison.OrdinalIgnoreCase))
            {
                return; // already in this mode
            }

            _config.FilterMode = mode;
            bool isRelease = string.Equals(mode, "BlockRelease", StringComparison.OrdinalIgnoreCase);
            releaseItem.Checked = isRelease;
            repressItem.Checked = !isRelease;

            SaveConfig();
            RestartFilter();
        }

        private static void RestartFilter()
        {
            try
            {
                _filter?.Stop();
                _filter = new KeyboardHookFilter(_config);
                _filter.Start();
            }
            catch (Exception ex)
            {
                LogLifecycle("FilterRestartError", ex.Message);
            }
        }

        private static void UpdateBypassIndicator()
        {
            bool outranks = ElevationDetector.ForegroundOutranksSelf(out string processName, out uint processId);

            if (outranks)
            {
                // Toast on each focus into an elevated window: the first time we
                // enter the bypass state, or when focus moves to a *different*
                // elevated process while still bypassed.
                bool newElevatedFocus = !_bypassActive || processId != _lastToastPid;

                if (!_bypassActive)
                {
                    _notifyIcon.Icon = _warningIcon;
                    _notifyIcon.Text = TooltipBypassed;
                    LogLifecycle("HookBypass",
                        $"foreground process '{processName}' runs with higher privileges (administrator); " +
                        "its keystrokes bypass the filter until a normal window is focused");
                    _bypassActive = true;
                }

                if (newElevatedFocus)
                {
                    _lastToastPid = processId;
                    ShowBypassToast();
                }
            }
            else if (_bypassActive)
            {
                _notifyIcon.Icon = _normalIcon;
                _notifyIcon.Text = TooltipNormal;
                LogLifecycle("HookActive", "foreground window no longer outranks the filter; filtering is active");
                _bypassActive = false;
                _lastToastPid = 0;
            }
        }

        // Brief, focus-safe corner toast shown each time focus moves to an elevated
        // window (where filtering is inactive). Suppressed when disabled in config.
        private static void ShowBypassToast()
        {
            if (_config != null && !_config.ShowElevatedWindowNotice)
            {
                return;
            }

            try { _toast?.Close(); }
            catch { /* a previous toast may already be gone */ }

            // Long-lived because it is actionable: the user needs time to read it and
            // click to elevate. Hovering pauses this countdown (see ToastForm), and
            // moving the pointer away restarts it, so it stays as long as it is needed.
            _toast = new ToastForm(
                "Keyboard Repeat Filter",
                "Paused while an administrator window is active. Click here to run as administrator, or switch back to resume.",
                15000,
                RestartAsAdmin);
            _toast.FormClosed += (_, __) =>
            {
                _toast?.Dispose();
                _toast = null;
            };
            _toast.Show(); // non-activating; does not take focus
        }

        // Builds a yellow-tinted copy of the tray icon used to flag the bypass
        // state. The tint keeps the icon's shape (transparency is preserved) while
        // shifting its colours toward amber.
        private static Icon BuildWarningIcon(Icon baseIcon)
        {
            using (var src = baseIcon.ToBitmap())
            using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var attrs = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var matrix = new ColorMatrix(new[]
                    {
                        new[] { 1f, 0f, 0f, 0f, 0f },
                        new[] { 0f, 1f, 0f, 0f, 0f },
                        new[] { 0f, 0f, 0.12f, 0f, 0f }, // strip most blue
                        new[] { 0f, 0f, 0f, 1f, 0f },     // keep alpha (shape)
                        new[] { 0.30f, 0.22f, 0f, 0f, 1f } // lift red/green toward amber
                    });
                    attrs.SetColorMatrix(matrix);
                    g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                }

                IntPtr hicon = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(hicon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hicon);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static void LaunchHeatmap(bool verbose)
        {
            // KeyboardHeatmap.exe lives next to this app and reads config.json from
            // its working directory to resolve the log path, so launch it from here.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Path.Combine(baseDir, "KeyboardHeatmap.exe");

            if (!File.Exists(exePath))
            {
                MessageBox.Show(
                    "KeyboardHeatmap.exe was not found next to the application.\r\n" +
                    "Make sure it is in the same folder as KeyboardRepeatFilter.exe.",
                    "Keyboard Repeat Filter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // The heatmap is built from the filter log. If it does not exist yet,
            // KeyboardHeatmap would fail silently, so guide the user instead.
            var logPath = _config?.LogFilePath;
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                MessageBox.Show(
                    "The filter log was not found:\r\n" +
                    (string.IsNullOrWhiteSpace(logPath) ? "(no LogFilePath configured)" : logPath) + "\r\n\r\n" +
                    "The heatmap is generated from this log. To produce one:\r\n\r\n" +
                    "  1. Set \"LogLevel\": \"Trace\" in config.json so filtered keys are recorded.\r\n" +
                    "  2. Check that \"LogFilePath\" in config.json points to a valid, writable path.\r\n" +
                    "  3. Restart the app, type for a while so events are logged, then try again.",
                    "Keyboard Heatmap",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = verbose ? "-v" : string.Empty,
                    WorkingDirectory = baseDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                // KeyboardHeatmap generates the report and opens it in the browser itself.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to launch KeyboardHeatmap:\r\n" + ex.Message,
                    "Keyboard Repeat Filter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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

        private static string ConfigFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        private static FilterConfig LoadConfig()
        {
            var configPath = ConfigFilePath;

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

        private static void SaveConfig()
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };
                File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(_config, settings));
            }
            catch (Exception ex)
            {
                LogLifecycle("ConfigSaveError", ex.Message);
            }
        }
    }
}
