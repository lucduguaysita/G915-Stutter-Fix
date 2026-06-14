using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardRepeatFilter
{
    /// <summary>
    /// A small, self-dismissing notification drawn in the bottom-right corner.
    /// Unlike a tray balloon it does not depend on Windows notification settings
    /// or Focus Assist, and it is built to never steal focus from the active
    /// window (WS_EX_NOACTIVATE + ShowWithoutActivation).
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;

        private readonly Timer _life = new Timer();
        private readonly Font _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _bodyFont = new Font("Segoe UI", 9f, FontStyle.Regular);

        public ToastForm(string title, string message, int durationMs)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(58, 52, 40); // thin border colour
            Width = 330;
            Height = 96;

            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(38, 34, 27) };
            var accent = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Color.FromArgb(250, 199, 117) };
            var titleLbl = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.FromArgb(250, 199, 117),
                Font = _titleFont,
                Padding = new Padding(14, 9, 12, 0)
            };
            var bodyLbl = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(226, 223, 231),
                Font = _bodyFont,
                Padding = new Padding(14, 2, 12, 10)
            };

            panel.Controls.Add(bodyLbl);
            panel.Controls.Add(titleLbl);
            panel.Controls.Add(accent);
            Controls.Add(panel);
            Padding = new Padding(1); // lets the BackColor show as a 1px border

            // Click anywhere to dismiss early.
            EventHandler dismiss = (_, __) => Close();
            Click += dismiss;
            panel.Click += dismiss;
            titleLbl.Click += dismiss;
            bodyLbl.Click += dismiss;

            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);

            _life.Interval = Math.Max(1000, durationMs);
            _life.Tick += (_, __) => Close();
        }

        // Prevents the form from activating (stealing focus) when shown.
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _life.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _life.Stop();
            _life.Dispose();
            _titleFont.Dispose();
            _bodyFont.Dispose();
            base.OnFormClosed(e);
        }
    }
}
