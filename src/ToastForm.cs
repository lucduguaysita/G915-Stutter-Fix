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

        public ToastForm(string title, string message, int durationMs, Action onClick = null)
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

            // Title row as its own container: the title fills the left, the close
            // button docks to the right. No overlapping siblings, so the button
            // always paints (an overlay over the fill panel did not render reliably).
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(38, 34, 27) };
            var titleLbl = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(250, 199, 117),
                Font = _titleFont,
                Padding = new Padding(14, 9, 0, 0)
            };
            // Explicit dismiss: closes the toast only, never runs the click action,
            // so the notice can always be cleared without triggering an elevation
            // relaunch (or waiting out the timer).
            var closeLbl = new Label
            {
                Text = "×",
                Dock = DockStyle.Right,
                Width = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(150, 145, 135),
                BackColor = Color.FromArgb(38, 34, 27),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            closeLbl.Click += (s, e) => Close();
            closeLbl.MouseEnter += (s, e) => closeLbl.ForeColor = Color.FromArgb(250, 199, 117);
            closeLbl.MouseLeave += (s, e) => closeLbl.ForeColor = Color.FromArgb(150, 145, 135);
            titleBar.Controls.Add(titleLbl); // Dock.Fill, added first so it takes only the leftover
            titleBar.Controls.Add(closeLbl); // Dock.Right, added last so it reserves its edge

            var bodyLbl = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(226, 223, 231),
                Font = _bodyFont,
                Padding = new Padding(14, 2, 12, 10)
            };

            panel.Controls.Add(bodyLbl);  // Dock.Fill
            panel.Controls.Add(titleBar); // Dock.Top
            panel.Controls.Add(accent);   // Dock.Left
            Controls.Add(panel);
            Padding = new Padding(1); // lets the BackColor show as a 1px border

            // Click anywhere to act (when an action is supplied) and dismiss. With no
            // action it simply dismisses early.
            EventHandler dismiss = (_, __) =>
            {
                if (onClick != null)
                {
                    try { onClick(); }
                    catch { /* never let a toast click crash the tray app */ }
                }
                Close();
            };
            Click += dismiss;
            panel.Click += dismiss;
            titleBar.Click += dismiss;
            titleLbl.Click += dismiss;
            bodyLbl.Click += dismiss;
            if (onClick != null)
            {
                Cursor = panel.Cursor = titleBar.Cursor = titleLbl.Cursor = bodyLbl.Cursor = Cursors.Hand;
            }

            // Keep the toast up while the user is reading or about to click it: pause
            // the auto-dismiss countdown on hover and restart it (full duration) once
            // the pointer leaves, so it can never vanish out from under the cursor.
            EventHandler pause = (_, __) => _life.Stop();
            EventHandler resume = (_, __) =>
            {
                if (!Bounds.Contains(Cursor.Position))
                {
                    _life.Stop();
                    _life.Start();
                }
            };
            foreach (Control c in new Control[] { this, panel, accent, titleBar, titleLbl, bodyLbl, closeLbl })
            {
                c.MouseEnter += pause;
                c.MouseLeave += resume;
            }

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
