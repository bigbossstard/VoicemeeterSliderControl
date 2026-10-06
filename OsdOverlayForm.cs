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

        const int VisibleDurationMs = 700;
        const int TimerIntervalMs = 25;
        const int HorizontalPadding = 20;
        const int MinimumWidth = 150;
        const int MaximumWidth = 250;
        const int OverlayHeight = 46;

        static readonly IntPtr HWND_TOPMOST =
            new IntPtr(-1);

        readonly Timer _timer;

        readonly Font _titleFont =
            new Font(
                "Segoe UI Semibold",
                9.0f,
                FontStyle.Bold);

        readonly Font _valueFont =
            new Font(
                "Segoe UI Semibold",
                13.0f,
                FontStyle.Bold);

        string _title = "";
        string _value = "";

        DateTime _hideAtUtc =
            DateTime.MinValue;

        public OsdOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            DoubleBuffered = true;

            // The overlay is intentionally fitted to its text.
            // It should not occupy a large empty rectangle.
            Width = MinimumWidth;
            Height = OverlayHeight;

            BackColor =
                Color.FromArgb(
                    30,
                    30,
                    30);

            ForeColor = Color.White;

            Padding =
                new Padding(
                    HorizontalPadding / 2,
                    4,
                    HorizontalPadding / 2,
                    4);

            Opacity = 0;

            _timer =
                new Timer
                {
                    Interval = TimerIntervalMs
                };

            _timer.Tick += Timer_Tick;

            // Create the HWND on the UI thread in advance so
            // cross-thread ShowValue calls can marshal reliably.
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
                    2,
                    textWidth,
                    17);

            var valueRect =
                new Rectangle(
                    textLeft,
                    17,
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

            const int radius = 9;

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

            _title =
                GetOsdName(
                    sliderIndex,
                    displayName,
                    voicemeeterValue);

            _value =
                db.ToString(
                    "0.0",
                    CultureInfo.CurrentCulture) +
                " dB";

            ResizeToContent();

            PositionOnForegroundMonitor();

            Opacity = 0.94;

            _hideAtUtc =
                DateTime.UtcNow.AddMilliseconds(
                    VisibleDurationMs);

            if (!Visible)
                Show();

            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                Left,
                Top,
                Width,
                Height,
                SWP_NOACTIVATE |
                SWP_SHOWWINDOW);

            _timer.Start();
            Invalidate();
        }

        // OSD-only aliases. They intentionally use the same
        // user-facing Strip numbering shown in Settings, rather
        // than guessing from zero-based Voicemeeter API indices.
        internal static string GetOsdName(
            int sliderIndex,
            string displayName,
            string voicemeeterValue)
        {
            string name =
                (displayName ?? "").Trim();

            if (name.Equals(
                    "Strip 5",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Voicemeeter Input";
            }

            if (name.Equals(
                    "Strip 6",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Voicemeeter VAIO3";
            }

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return
                $"Слайдер {sliderIndex + 1}";
        }

        void ResizeToContent()
        {
            Size titleSize =
                TextRenderer.MeasureText(
                    _title,
                    _titleFont,
                    new Size(
                        MaximumWidth -
                        HorizontalPadding,
                        OverlayHeight),
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine);

            Size valueSize =
                TextRenderer.MeasureText(
                    _value,
                    _valueFont,
                    new Size(
                        MaximumWidth -
                        HorizontalPadding,
                        OverlayHeight),
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine);

            int width =
                Math.Max(
                    titleSize.Width,
                    valueSize.Width) +
                HorizontalPadding;

            width =
                Math.Clamp(
                    width,
                    MinimumWidth,
                    MaximumWidth);

            if (Width != width)
                Width = width;

            if (Height != OverlayHeight)
                Height = OverlayHeight;
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

            const int margin = 16;

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
            {
                _timer.Stop();
                return;
            }

            if (DateTime.UtcNow <
                _hideAtUtc)
            {
                return;
            }

            HideOverlayCore();
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

            HideOverlayCore();
        }

        void HideOverlayCore()
        {
            _timer.Stop();
            _hideAtUtc = DateTime.MinValue;
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
