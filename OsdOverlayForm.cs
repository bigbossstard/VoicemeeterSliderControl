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

        readonly Timer _timer;

        readonly Font _titleFont =
            new Font(
                "Segoe UI Semibold",
                9.5f,
                FontStyle.Bold);

        readonly Font _valueFont =
            new Font(
                "Segoe UI Semibold",
                14.0f,
                FontStyle.Bold);

        string _title = "";
        string _value = "";

        DateTime _hideAtUtc =
            DateTime.MinValue;

        bool _fading;

        public OsdOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            DoubleBuffered = true;

            // Compact by design: it should identify the slider
            // without becoming a permanent HUD element.
            Width = 300;
            Height = 54;

            BackColor =
                Color.FromArgb(
                    30,
                    30,
                    30);

            ForeColor = Color.White;

            Padding =
                new Padding(
                    12,
                    5,
                    12,
                    5);

            Opacity = 0;

            _timer =
                new Timer
                {
                    Interval = 30
                };

            _timer.Tick += Timer_Tick;

            // Create the HWND on the UI thread in advance.
            _ = Handle;

            UpdateRegion();
        }

        protected override bool ShowWithoutActivation =>
            true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;

                cp.ExStyle |=
                    0x00000020; // WS_EX_TRANSPARENT

                cp.ExStyle |=
                    0x00000080; // WS_EX_TOOLWINDOW

                cp.ExStyle |=
                    0x00080000; // WS_EX_LAYERED

                cp.ExStyle |=
                    0x08000000; // WS_EX_NOACTIVATE

                return cp;
            }
        }

        protected override void OnPaintBackground(
            PaintEventArgs e)
        {
            using var brush =
                new SolidBrush(BackColor);

            e.Graphics.FillRectangle(
                brush,
                ClientRectangle);
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            e.Graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle client =
                ClientRectangle;

            int textLeft =
                Padding.Left;

            int textWidth =
                client.Width -
                Padding.Left -
                Padding.Right;

            using var titleBrush =
                new SolidBrush(Color.White);

            using var valueBrush =
                new SolidBrush(Color.Gainsboro);

            var titleRect =
                new Rectangle(
                    textLeft,
                    3,
                    textWidth,
                    19);

            var valueRect =
                new Rectangle(
                    textLeft,
                    21,
                    textWidth,
                    25);

            using var titleFormat =
                new StringFormat
                {
                    Alignment =
                        StringAlignment.Near,

                    LineAlignment =
                        StringAlignment.Center,

                    Trimming =
                        StringTrimming.EllipsisCharacter,

                    FormatFlags =
                        StringFormatFlags.NoWrap
                };

            using var valueFormat =
                new StringFormat
                {
                    Alignment =
                        StringAlignment.Near,

                    LineAlignment =
                        StringAlignment.Center,

                    Trimming =
                        StringTrimming.EllipsisCharacter,

                    FormatFlags =
                        StringFormatFlags.NoWrap
                };

            e.Graphics.DrawString(
                _title,
                _titleFont,
                titleBrush,
                titleRect,
                titleFormat);

            e.Graphics.DrawString(
                _value,
                _valueFont,
                valueBrush,
                valueRect,
                valueFormat);
        }

        protected override void OnResize(
            EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        void UpdateRegion()
        {
            if (Width <= 0 ||
                Height <= 0)
            {
                return;
            }

            using var path =
                new GraphicsPath();

            const int radius = 10;

            var rect =
                new Rectangle(
                    0,
                    0,
                    Width,
                    Height);

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

            Region =
                new Region(path);
        }

        public void ShowValue(
            int sliderIndex,
            string displayName,
            string voicemeeterValue,
            float db)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(
                        new Action(() =>
                            ShowValue(
                                sliderIndex,
                                displayName,
                                voicemeeterValue,
                                db)));
                }
                catch
                {
                }

                return;
            }

            string name =
                GetOsdName(
                    sliderIndex,
                    displayName,
                    voicemeeterValue);

            _title = name;

            _value =
                db.ToString(
                    "0.0",
                    CultureInfo.CurrentCulture) +
                " dB";

            PositionOnForegroundMonitor();

            _fading = false;

            Opacity = 0.92;

            _hideAtUtc =
                DateTime.UtcNow.AddMilliseconds(800);

            _timer.Start();

            Invalidate();

            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                Left,
                Top,
                Width,
                Height,
                SWP_NOACTIVATE |
                SWP_SHOWWINDOW);
        }

        static string GetOsdName(
            int sliderIndex,
            string displayName,
            string voicemeeterValue)
        {
            // OSD-only aliases for Voicemeeter Potato
            // virtual inputs. The stored mapping and
            // Settings UI remain untouched.
            switch (
                (voicemeeterValue ?? "")
                    .Trim()
                    .ToUpperInvariant())
            {
                case "STRIP[4].GAIN":
                    return "Voicemeeter Input";

                case "STRIP[5].GAIN":
                    return "Voicemeeter AUX 1";

                case "STRIP[6].GAIN":
                    return "Voicemeeter VAIO3";
            }

            if (!string.IsNullOrWhiteSpace(
                    displayName))
            {
                return displayName.Trim();
            }

            return
                $"Слайдер {sliderIndex + 1}";
        }

        void PositionOnForegroundMonitor()
        {
            IntPtr foreground =
                GetForegroundWindow();

            Screen screen;

            try
            {
                screen =
                    foreground == IntPtr.Zero
                        ? Screen.PrimaryScreen
                        : Screen.FromHandle(foreground);
            }
            catch
            {
                screen =
                    Screen.PrimaryScreen;
            }

            if (screen == null)
                return;

            Rectangle area =
                screen.WorkingArea;

            const int margin = 18;

            int x =
                area.Right -
                Width -
                margin;

            int y =
                area.Bottom -
                Height -
                margin;

            Location =
                new Point(
                    Math.Max(area.Left + margin, x),
                    Math.Max(area.Top + margin, y));
        }

        void Timer_Tick(
            object sender,
            EventArgs e)
        {
            if (!Visible)
                return;

            if (!_fading &&
                DateTime.UtcNow >=
                _hideAtUtc)
            {
                _fading = true;
            }

            if (!_fading)
                return;

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
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(
                        new Action(
                            HideOverlay));
                }
                catch
                {
                }

                return;
            }

            _timer.Stop();

            _fading = false;

            Opacity = 0;

            Hide();
        }

        protected override void WndProc(
            ref Message m)
        {
            if (m.Msg ==
                WM_NCHITTEST)
            {
                m.Result =
                    new IntPtr(
                        HTTRANSPARENT);

                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                _titleFont.Dispose();
                _valueFont.Dispose();
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        static extern IntPtr
            GetForegroundWindow();

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
