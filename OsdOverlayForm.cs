using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    public sealed class OsdOverlayForm : Form
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        const int SWP_NOACTIVATE = 0x0010;
        const int SWP_SHOWWINDOW = 0x0040;

        static readonly IntPtr HWND_TOPMOST =
            new IntPtr(-1);

        readonly Label _titleLabel;
        readonly Label _valueLabel;
        readonly Timer _timer;

        DateTime _hideAtUtc = DateTime.MinValue;
        bool _fading;

        public OsdOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;

            Width = 360;
            Height = 78;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Padding = new Padding(16, 9, 16, 8);
            Opacity = 0;

            _titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 29,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(
                    "Segoe UI Semibold",
                    11.0f,
                    FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Font = new Font(
                    "Segoe UI Semibold",
                    16.0f,
                    FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(_valueLabel);
            Controls.Add(_titleLabel);

            _timer = new Timer { Interval = 30 };
            _timer.Tick += Timer_Tick;

            _ = Handle;
            UpdateRegion();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            using var path = new GraphicsPath();
            const int radius = 12;
            var rect = new Rectangle(0, 0, Width, Height);

            path.AddArc(
                rect.Left,
                rect.Top,
                radius,
                radius,
                180,
                90);

            path.AddArc(
                rect.Right - radius,
                rect.Top,
                radius,
                radius,
                270,
                90);

            path.AddArc(
                rect.Right - radius,
                rect.Bottom - radius,
                radius,
                radius,
                0,
                90);

            path.AddArc(
                rect.Left,
                rect.Bottom - radius,
                radius,
                radius,
                90,
                90);

            path.CloseFigure();
            Region = new Region(path);
        }

        public void ShowValue(
            int sliderIndex,
            string displayName,
            float db)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                        ShowValue(
                            sliderIndex,
                            displayName,
                            db)));
                }
                catch
                {
                }

                return;
            }

            string name =
                string.IsNullOrWhiteSpace(displayName)
                    ? $"Слайдер {sliderIndex + 1}"
                    : displayName.Trim();

            _titleLabel.Text =
                $"Вход  •  {name}";

            _valueLabel.Text =
                $"{db.ToString(
                    "0.0",
                    CultureInfo.CurrentCulture)} dB";

            PositionOnForegroundMonitor();

            _fading = false;
            Opacity = 0.94;
            _hideAtUtc =
                DateTime.UtcNow.AddMilliseconds(950);

            _timer.Start();

            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                Left,
                Top,
                Width,
                Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        void PositionOnForegroundMonitor()
        {
            IntPtr foreground = GetForegroundWindow();
            Screen screen;

            try
            {
                screen = foreground == IntPtr.Zero
                    ? Screen.PrimaryScreen
                    : Screen.FromHandle(foreground);
            }
            catch
            {
                screen = Screen.PrimaryScreen;
            }

            if (screen == null) return;

            Rectangle area = screen.WorkingArea;

            int x =
                area.Left +
                (area.Width - Width) / 2;

            int y =
                area.Top +
                Math.Max(20, area.Height / 14);

            Location = new Point(x, y);
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            if (!Visible) return;

            if (!_fading &&
                DateTime.UtcNow >= _hideAtUtc)
            {
                _fading = true;
            }

            if (!_fading) return;

            Opacity -= 0.08;

            if (Opacity <= 0.02)
            {
                Opacity = 0;
                Hide();
                _timer.Stop();
                _fading = false;
            }
        }

        public void HideOverlay()
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try { BeginInvoke(
                    new Action(HideOverlay)); }
                catch { }

                return;
            }

            _timer.Stop();
            _fading = false;
            Opacity = 0;
            Hide();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result =
                    new IntPtr(HTTRANSPARENT);
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            int uFlags);
    }
}
