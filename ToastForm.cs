using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipLite
{
    /// <summary>
    /// Lightweight in-app copy toast. Unlike NotifyIcon balloons it is not filtered by
    /// Windows notification/focus-assist settings, so it always appears when enabled.
    /// </summary>
    public class ToastForm : Form
    {
        private const int ToastWidth = 320;
        private const int LifeMs = 1800;
        private const int FadeOutMs = 260;

        private readonly Timer _timer;
        private readonly Font _titleFont;
        private readonly Font _detailFont;
        private DateTime _shownAt;
        private string _detail;
        private string _title;
        private bool _closing;

        /// <summary>"cursor" (default) or "bottom".</summary>
        public string PositionMode { get; set; }

        public ToastForm()
        {
            PositionMode = "cursor";
            _title = "已复制到剪贴板";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = UiTheme.Surface;
            this.Size = new Size(ToastWidth, 66);
            this.AllowTransparency = true;
            this.Opacity = 0;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);

            _titleFont = UiTheme.CreateFont(9.5F, FontStyle.Bold);
            _detailFont = UiTheme.CreateFont(8F, FontStyle.Regular);

            _timer = new Timer { Interval = 15 };
            _timer.Tick += OnTimerTick;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;

                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ShowToast(string preview)
        {
            ShowToast(preview, "已复制到剪贴板");
        }

        public void ShowToast(string preview, string title)
        {
            _title = string.IsNullOrEmpty(title) ? "已复制到剪贴板" : title;
            _detail = preview ?? "";
            this.Height = _detail.Length > 0 ? 66 : 54;
            this.Width = ToastWidth;

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (string.Equals(PositionMode, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                int bottomMargin = 56;
                int centeredX = area.Left + (area.Width - Width) / 2;
                int bottomY = area.Bottom - Height - bottomMargin;
                if (bottomY < area.Top + 12) bottomY = area.Top + 12;
                this.Location = new Point(centeredX, bottomY);
            }
            else
            {
                int x = Cursor.Position.X + 16;
                int y = Cursor.Position.Y + 16;
                if (x + Width > area.Right - 12) x = Cursor.Position.X - Width - 16;
                if (y + Height > area.Bottom - 12) y = Cursor.Position.Y - Height - 16;
                if (x < area.Left + 12) x = area.Left + 12;
                if (y < area.Top + 12) y = area.Top + 12;
                this.Location = new Point(x, y);
            }

            _closing = false;
            _shownAt = DateTime.Now;
            this.Opacity = 0.94; // Visible immediately; never start a toast at opacity 0.
            this.Show();
            this.BringToFront();
            _timer.Stop();
            _timer.Start();
            this.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiTheme.RoundControl(this, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var pen = new Pen(UiTheme.Border))
            {
                g.DrawPath(pen, path);
            }

            int iconX = 16;
            int iconY = (Height - 28) / 2;
            using (var fill = new SolidBrush(Color.FromArgb(232, 248, 240)))
                g.FillEllipse(fill, iconX, iconY, 28, 28);
            using (var pen = new Pen(Color.FromArgb(18, 183, 106), 2.2F))
            {
                g.DrawLines(pen, new Point[]
                {
                    new Point(iconX + 7, iconY + 14),
                    new Point(iconX + 12, iconY + 19),
                    new Point(iconX + 21, iconY + 9)
                });
            }

            int textX = iconX + 40;
            int textWidth = Width - textX - 12;
            int titleY = _detail.Length > 0 ? 11 : (Height - 18) / 2;
            TextRenderer.DrawText(g, _title, _titleFont,
                new Rectangle(textX, titleY, textWidth, 18), UiTheme.TextPrimary,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            if (_detail.Length > 0)
            {
                TextRenderer.DrawText(g, _detail, _detailFont,
                    new Rectangle(textX, 31, textWidth, 17), UiTheme.TextSecondary,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            int elapsed = (int)(DateTime.Now - _shownAt).TotalMilliseconds;
            if (elapsed >= LifeMs)
            {
                CloseToast();
                return;
            }

            if (elapsed > LifeMs - FadeOutMs)
            {
                this.Opacity = 0.94 * (LifeMs - elapsed) / FadeOutMs;
            }
            else
            {
                this.Opacity = 0.94;
            }
        }

        private void CloseToast()
        {
            if (_closing) return;
            _closing = true;
            _timer.Stop();
            this.Hide();
            this.Opacity = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_titleFont != null) _titleFont.Dispose();
                if (_detailFont != null) _detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
