using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace KeyboardRepeatFilter
{
    internal static class Program
    {
        private static KeyboardHookFilter _filter;
        private static NotifyIcon _notifyIcon;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = LoadConfig();

            _filter = new KeyboardHookFilter(config);
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
        }


        private static void OnExit(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _filter.Stop();
            Application.Exit();
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
