using System;
using System.Collections.Generic;
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
using Newtonsoft.Json.Linq;

namespace KeyboardRepeatFilter
{
    internal static class Program
    {
        private static KeyboardHookFilter _filter;
        private static MouseHookFilter _mouseFilter;
        private static NotifyIcon _notifyIcon;
        private static FilterConfig _config;
        // The config file currently in effect; saves are written back here so tray
        // toggles persist into whichever profile is active. Defaults to config.json.
        private static string _activeConfigPath;
        private static DateTime _startedAtUtc;
        private static bool _shutdownLogged;
        private static Mutex _mutex;

        private static Icon _normalIcon;
        private static Icon _warningIcon;
        private static System.Windows.Forms.Timer _bypassTimer;
        private static bool _bypassActive;
        private static uint _lastToastPid;
        private static ToastForm _toast;

        // Result of the startup update check, set on a background thread and read on
        // the UI thread (volatile so the read sees the write). Null until the check
        // finishes; afterwards it carries an explicit status (up to date, newer
        // available, or could-not-check). _updateCheckComplete flips true once the
        // background check finishes, whatever the outcome. The UI only ever reads
        // these cached fields, so it never blocks on the network, even in an airtight
        // (offline) environment where the check runs until its timeout.
        private static volatile UpdateChecker.Result _updateResult;
        private static volatile bool _updateCheckComplete;
        private static bool _updateToastShown;
        private static System.Windows.Forms.Timer _updateToastTimer;

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
            _activeConfigPath = ConfigFilePath;
            _startedAtUtc = DateTime.UtcNow;

            // Honor a configured default profile before anything observes the config:
            // this runs ahead of the elevation check and the hook creation so the
            // profile's RunAsAdmin and filter settings take effect from launch, and
            // the tray menu builds against the profile that is actually live.
            ApplyDefaultProfileAtStartup();
            RegisterGlobalExceptionHandlers();
            Application.ApplicationExit += (_, __) => LogShutdown("ApplicationExit");

            // Sticky elevation: if the user asked to always run as administrator and
            // we are not elevated yet, relaunch elevated before doing any work. The
            // elevated instance re-enters Main, finds itself elevated, and continues
            // normally. Done here (before any UI/hook is created) so there is nothing
            // to tear down on the unelevated instance.
            if (_config.RunAsAdmin && !IsElevated())
            {
                LogLifecycle("Elevating", "RunAsAdmin is set; relaunching with administrator rights at startup");
                if (TryStartElevatedProcess())
                {
                    // Release the singleton so the elevated instance (which waits for
                    // the mutex) can take over, then stand down.
                    try { _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
                    return;
                }

                // UAC declined or failed: run unelevated this session; the preference
                // stays set and will prompt again next launch.
                LogLifecycle("ElevationDeclined", "continuing unelevated; RunAsAdmin remains set for next launch");
            }

            LogLifecycle("Startup",
                $"version={Assembly.GetExecutingAssembly().GetName().Version}, pid={Process.GetCurrentProcess().Id}, minRepeatIntervalMs={_config.MinRepeatIntervalMs}");

            _filter = new KeyboardHookFilter(_config);
            _filter.Start();

            StartMouseFilter();

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

            // --- Run as administrator (sticky preference, persisted to config.json) ---
            // A medium-integrity hook is bypassed (UIPI) for elevated windows, so
            // running elevated is the only way to filter their keystrokes. When
            // checked, the app relaunches elevated now (if not already) and
            // auto-elevates on every future launch. Shown in both states so it can be
            // switched back off while elevated.
            var adminItem = new MenuItem("Always run as administrator")
            {
                Checked = _config.RunAsAdmin
            };
            adminItem.Click += (s, e) =>
            {
                _config.RunAsAdmin = !_config.RunAsAdmin;
                adminItem.Checked = _config.RunAsAdmin;
                SaveConfig();

                // Honor it immediately: if just turned on while unelevated, relaunch
                // now (a UAC prompt appears). Turning it off takes effect next launch;
                // we do not drop privileges mid-session.
                if (_config.RunAsAdmin && !IsElevated())
                {
                    RestartAsAdmin();
                }
            };
            contextMenu.MenuItems.Add(adminItem);

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

            // --- Mouse-button debouncing toggle (persisted to config.json) ---
            var mouseItem = new MenuItem("Enable mouse click debounce")
            {
                Checked = _config.FilterMouseButtons
            };
            mouseItem.Click += (s, e) =>
            {
                _config.FilterMouseButtons = !_config.FilterMouseButtons;
                mouseItem.Checked = _config.FilterMouseButtons;
                SaveConfig();
                if (_config.FilterMouseButtons)
                {
                    StartMouseFilter();
                }
                else
                {
                    StopMouseFilter();
                }
            };
            contextMenu.MenuItems.Add(mouseItem);

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

            // --- Profiles: activate another matching config file from this folder ---
            // Any *.json next to the app that looks like one of our config files is
            // offered as a profile. Selecting one loads it as the live config and
            // applies it immediately; later tray toggles save back into whichever
            // profile is active. The list is built at startup.
            var profileMenu = new MenuItem("Profile");
            var profileItems = new List<MenuItem>();
            foreach (var pf in FindProfileFiles())
            {
                string filePath = pf.Path;
                // config.json is the base config; selecting it clears any startup
                // default. Flag it in the menu so that role is clear.
                string displayName = PathsEqual(filePath, ConfigFilePath) ? "(default) " + pf.Name : pf.Name;
                var item = new MenuItem(displayName) { RadioCheck = true, Checked = PathsEqual(filePath, _activeConfigPath) };
                item.Click += (s, e) =>
                {
                    if (!ActivateProfile(filePath)) return;

                    foreach (var mi in profileItems) mi.Checked = false;
                    item.Checked = true;

                    // Selecting a profile also makes it the startup default: record it
                    // in config.json so the next launch (including Windows sign-in via
                    // Autostart) comes up on it. Selecting config.json clears the
                    // default so the app starts on config.json again.
                    PersistDefaultProfile(PathsEqual(filePath, ConfigFilePath) ? null : Path.GetFileName(filePath));

                    // Reflect the freshly loaded settings in the other toggles.
                    bool rel = string.Equals(_config.FilterMode, "BlockRelease", StringComparison.OrdinalIgnoreCase);
                    repressItem.Checked = !rel;
                    releaseItem.Checked = rel;
                    mouseItem.Checked = _config.FilterMouseButtons;
                    noticeItem.Checked = !_config.ShowElevatedWindowNotice;
                    adminItem.Checked = _config.RunAsAdmin;
                };
                profileItems.Add(item);
                profileMenu.MenuItems.Add(item);
            }
            profileMenu.Enabled = profileItems.Count > 0;
            contextMenu.MenuItems.Add(profileMenu);

            // --- Heatmap launchers ---
            var heatmapMenu = new MenuItem("Heatmap");
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

            StartUpdateCheck();

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

        // Launches an elevated copy of this app via the UAC "runas" verb. Returns
        // true if the elevated process was started (the caller should stand down so
        // the singleton mutex is freed), false if the user declined the prompt or it
        // failed (the caller keeps running unelevated).
        private static bool TryStartElevatedProcess()
        {
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
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User declined the UAC prompt (ERROR_CANCELLED, 1223).
                return false;
            }
            catch (Exception ex)
            {
                LogLifecycle("ElevationError", ex.Message);
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

            if (!TryStartElevatedProcess())
            {
                // User declined the UAC prompt or it failed. Keep running unelevated
                // rather than exiting.
                return;
            }

            // The elevated instance is launching; release our hold so it can take
            // over without tripping the "already running" guard.
            LogLifecycle("Elevating", "relaunching with administrator rights at user request");
            _bypassTimer?.Stop();
            _updateToastTimer?.Stop();
            try { _toast?.Close(); } catch { /* ignore */ }
            _notifyIcon.Visible = false;
            _filter?.Stop();
            StopMouseFilter();
            // The mutex is released by the tail of Main once Application.Run returns;
            // the elevated instance waits (see Main) for that release before starting.
            Application.Exit();
        }

        private static void OnExit(object sender, EventArgs e)
        {
            _bypassTimer?.Stop();
            _updateToastTimer?.Stop();
            try { _toast?.Close(); } catch { /* ignore */ }
            _notifyIcon.Visible = false;
            _filter.Stop();
            StopMouseFilter();
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

        // Starts the low-level mouse hook when mouse debouncing is enabled. A no-op
        // when disabled or already running, so it is safe to call from both startup
        // and the tray toggle.
        private static void StartMouseFilter()
        {
            if (!_config.FilterMouseButtons || _mouseFilter != null)
            {
                return;
            }

            try
            {
                _mouseFilter = new MouseHookFilter(_config);
                _mouseFilter.Start();
            }
            catch (Exception ex)
            {
                _mouseFilter = null;
                LogLifecycle("MouseFilterStartError", ex.Message);
            }
        }

        private static void StopMouseFilter()
        {
            try
            {
                _mouseFilter?.Stop();
            }
            catch (Exception ex)
            {
                LogLifecycle("MouseFilterStopError", ex.Message);
            }
            finally
            {
                _mouseFilter = null;
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
                "Paused while an administrator window is active. Click here to run as administrator, or click the close button.",
                10000,
                RestartAsAdmin);
            _toast.FormClosed += (_, __) =>
            {
                _toast?.Dispose();
                _toast = null;
            };
            _toast.Show(); // non-activating; does not take focus
        }

        // Starts the best-effort "newer version available" check. The network call
        // runs on a background thread so it never delays the tray icon or blocks the
        // UI; a short UI timer then surfaces the result (a toast) once it completes.
        private static void StartUpdateCheck()
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;

            var thread = new Thread(() =>
            {
                var result = UpdateChecker.CheckForNewer(current);
                _updateResult = result;
                _updateCheckComplete = true;
                if (result.Status == UpdateChecker.Status.UpdateAvailable)
                {
                    LogLifecycle("UpdateAvailable", $"latest={result.Tag}, current={current}");
                }
            })
            {
                IsBackground = true,
                Name = "KeyboardRepeatFilter.UpdateCheck"
            };
            thread.Start();

            // Poll the result on the UI thread, then stop. Polling a volatile flag
            // keeps the check entirely off the UI thread with no cross-thread UI
            // marshalling, matching how _bypassTimer already drives tray updates.
            _updateToastTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _updateToastTimer.Tick += (_, __) =>
            {
                if (!_updateCheckComplete)
                {
                    return;
                }

                _updateToastTimer.Stop();
                MaybeShowUpdateToast();
            };
            _updateToastTimer.Start();
        }

        // Shows a one-time toast when a newer release was found, but only for users
        // who have not disabled nag popups. Those who have stay undisturbed; the
        // About box still tells them a new version is available.
        private static void MaybeShowUpdateToast()
        {
            var update = _updateResult;
            if (update == null || update.Status != UpdateChecker.Status.UpdateAvailable || _updateToastShown)
            {
                return;
            }

            if (_config != null && !_config.ShowElevatedWindowNotice)
            {
                return; // "do not nag" is set: no toast; the About box carries the news.
            }

            _updateToastShown = true;

            try { _toast?.Close(); }
            catch { /* a previous toast may already be gone */ }

            _toast = new ToastForm(
                "Keyboard Repeat Filter",
                $"A newer version ({update.Tag}) is available. Click to open the releases page.",
                10000,
                () => OpenUrl(update.Url));
            _toast.FormClosed += (_, __) =>
            {
                _toast?.Dispose();
                _toast = null;
            };
            _toast.Show(); // non-activating; does not take focus
        }

        // Opens a URL in the default browser, staying resilient if the shell launch
        // is unavailable (the tray app must never crash on a failed launch).
        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                // Keep tray app resilient if shell launch is unavailable.
            }
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
                    "Heatmap",
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

            // Reflect the startup update check here for everyone, including users who
            // disabled the toast. This only reads the cached result, so it never waits
            // on the network: if the background check has not finished (or is still
            // timing out in an airtight environment), it simply says so.
            var update = _updateResult;
            string updateLine;
            string targetUrl = repoUrl;

            if (!_updateCheckComplete || update == null)
            {
                updateLine = "Checking for updates...\r\n\r\n";
            }
            else if (update.Status == UpdateChecker.Status.UpdateAvailable)
            {
                updateLine = $"A newer version ({update.Tag}) is available.\r\n\r\n";
                targetUrl = update.Url;
            }
            else if (update.Status == UpdateChecker.Status.UpToDate)
            {
                updateLine = "You are running the latest version.\r\n\r\n";
            }
            else
            {
                updateLine = "Could not check for updates (no connection?).\r\n\r\n";
            }

            bool updateAvailable = update != null && update.Status == UpdateChecker.Status.UpdateAvailable;
            string prompt = updateAvailable ? "Open the GitHub releases page?" : "Open the GitHub project page?";

            var aboutText =
                "G915 Stutter Fix\r\n" +
                $"Version: {version}\r\n\r\n" +
                updateLine +
                "User-mode keyboard event filter for invalid HID repeats.\r\n\r\n" +
                $"Project: {repoUrl}\r\n" +
                "License: MIT\r\n\r\n" +
                prompt;

            var result = MessageBox.Show(
                aboutText,
                "About G915 Stutter Fix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenUrl(targetUrl);
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

        // Records which profile should load at startup by writing DefaultProfile
        // into config.json. It always targets config.json, because that is the only
        // file consulted for the startup default at launch. Pass a profile file name
        // to set it, or null to clear it so the app starts on config.json again.
        private static void PersistDefaultProfile(string profileFileName)
        {
            string value = string.IsNullOrWhiteSpace(profileFileName) ? null : profileFileName;

            // When config.json is itself the active save target, fold the change into
            // the normal save so the in-memory config and the file stay in step (a
            // later SaveConfig would otherwise rewrite config.json from _config and
            // clobber a direct edit here).
            if (PathsEqual(_activeConfigPath, ConfigFilePath))
            {
                _config.DefaultProfile = value;
                SaveConfig();
                LogLifecycle("DefaultProfileSaved", value ?? "cleared (start on config.json)");
                return;
            }

            // A profile is active, so config.json is not the save target. Edit only
            // its DefaultProfile on disk, preserving config.json's own settings and
            // leaving the active profile file untouched.
            try
            {
                var path = ConfigFilePath;
                var obj = File.Exists(path)
                    ? JToken.Parse(File.ReadAllText(path)) as JObject ?? new JObject()
                    : new JObject();

                if (value == null)
                {
                    obj.Remove("DefaultProfile");
                }
                else
                {
                    obj["DefaultProfile"] = value;
                }

                File.WriteAllText(path, obj.ToString(Formatting.Indented));
                LogLifecycle("DefaultProfileSaved", value ?? "cleared (start on config.json)");
            }
            catch (Exception ex)
            {
                LogLifecycle("DefaultProfileSaveError", ex.Message);
            }
        }

        // Loads the profile named in config.json's DefaultProfile (if any) as the
        // live configuration at startup, so the app comes up on that profile instead
        // of config.json. No-op when DefaultProfile is unset, points back at
        // config.json, or names a file that is missing/unreadable; in those cases the
        // app stays on the already-loaded config.json. Runs before the hook is
        // created, so no filter restart is needed here.
        private static void ApplyDefaultProfileAtStartup()
        {
            var name = _config?.DefaultProfile;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string path = ResolveProfilePath(name);
            if (path == null)
            {
                LogLifecycle("DefaultProfileMissing", $"DefaultProfile='{name}' was not found; staying on config.json");
                return;
            }

            if (PathsEqual(path, ConfigFilePath))
            {
                return; // DefaultProfile points at config.json: nothing to switch.
            }

            try
            {
                var cfg = JsonConvert.DeserializeObject<FilterConfig>(File.ReadAllText(path));
                if (cfg == null)
                {
                    LogLifecycle("DefaultProfileLoadError", $"file={Path.GetFileName(path)} deserialized to null; staying on config.json");
                    return;
                }

                _config = cfg;
                _activeConfigPath = path;
                LogLifecycle("DefaultProfileActivated", $"file={Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                LogLifecycle("DefaultProfileLoadError", $"file={Path.GetFileName(path)}, {ex.Message}; staying on config.json");
            }
        }

        // Resolves a DefaultProfile value to a full path in the app folder, accepting
        // the name with or without the ".json" extension (e.g. "WoW" or "WoW.json").
        // Returns null when no matching file exists.
        private static string ResolveProfilePath(string name)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string direct = Path.Combine(baseDir, name);
            if (File.Exists(direct))
            {
                return direct;
            }

            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string withExt = Path.Combine(baseDir, name + ".json");
                if (File.Exists(withExt))
                {
                    return withExt;
                }
            }

            return null;
        }

        // Property names that identify a KeyboardRepeatFilter config file. A *.json
        // in the app folder is treated as a profile when it shares at least two.
        private static readonly HashSet<string> ConfigSignatureKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LogLevel", "LogFilePath", "FilterMode", "ShowElevatedWindowNotice", "RunAsAdmin",
                "MinRepeatIntervalMs", "ExcludedKeys", "ExcludedVkCodes", "PerKeyMinRepeatIntervalMs",
                "FilterMouseButtons", "MouseMinRepeatIntervalMs", "ExcludedMouseButtons", "HeatmapDays"
            };

        // Every *.json in the app folder that looks like one of our config files,
        // as (full path, display name) pairs.
        private static List<(string Path, string Name)> FindProfileFiles()
        {
            var list = new List<(string, string)>();
            try
            {
                foreach (var path in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.json"))
                {
                    if (LooksLikeConfig(path))
                        list.Add((path, Path.GetFileNameWithoutExtension(path)));
                }
            }
            catch (Exception ex)
            {
                LogLifecycle("ProfileScanError", ex.Message);
            }
            return list;
        }

        private static bool LooksLikeConfig(string path)
        {
            try
            {
                if (!(JToken.Parse(File.ReadAllText(path)) is JObject obj))
                    return false;

                int matches = 0;
                foreach (var prop in obj.Properties())
                    if (ConfigSignatureKeys.Contains(prop.Name)) matches++;

                return matches >= 2;
            }
            catch
            {
                return false; // unreadable or not JSON => not a profile
            }
        }

        // Loads the given config file as the live configuration, makes it the target
        // for subsequent saves, and restarts the filters. Returns false on failure.
        private static bool ActivateProfile(string path)
        {
            FilterConfig cfg;
            try
            {
                cfg = JsonConvert.DeserializeObject<FilterConfig>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                LogLifecycle("ProfileLoadError", $"file={Path.GetFileName(path)}, {ex.Message}");
                MessageBox.Show(
                    "Could not load profile:\r\n" + Path.GetFileName(path) + "\r\n\r\n" + ex.Message,
                    "Keyboard Repeat Filter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (cfg == null)
                return false;

            _config = cfg;
            _activeConfigPath = path;
            RestartFilter();
            // Re-evaluate the mouse hook against the new profile's settings.
            StopMouseFilter();
            StartMouseFilter();
            LogLifecycle("ProfileActivated", $"file={Path.GetFileName(path)}");
            return true;
        }

        private static bool PathsEqual(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
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
                File.WriteAllText(_activeConfigPath ?? ConfigFilePath, JsonConvert.SerializeObject(_config, settings));
            }
            catch (Exception ex)
            {
                LogLifecycle("ConfigSaveError", ex.Message);
            }
        }
    }
}
