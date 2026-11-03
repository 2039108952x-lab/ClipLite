using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipLite
{
    /// <summary>Shared palette and small drawing helpers for the WinForms UI.</summary>
    internal static class UiTheme
    {
        public static readonly Color Canvas = Color.FromArgb(247, 249, 252);
        public static readonly Color Surface = Color.White;
        public static readonly Color TextPrimary = Color.FromArgb(31, 41, 55);
        public static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
        public static readonly Color TextMuted = Color.FromArgb(152, 162, 179);
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color BorderSoft = Color.FromArgb(240, 242, 245);
        public static readonly Color SearchFill = Color.FromArgb(247, 248, 250);
        public static readonly Color Accent = Color.FromArgb(18, 100, 214);
        public static readonly Color AccentSoft = Color.FromArgb(234, 242, 254);
        public static readonly Color AccentBar = Color.FromArgb(45, 127, 249);
        public static readonly Color Danger = Color.FromArgb(190, 45, 45);
        public static readonly Color WarningBg = Color.FromArgb(254, 240, 199);
        public static readonly Color WarningText = Color.FromArgb(181, 71, 8);
        public static readonly Color Highlight = Color.FromArgb(255, 232, 153);

        public static Font CreateFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;

            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void RoundControl(Control control, int radius)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0) return;
            using (var path = RoundedRect(new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                var old = control.Region;
                control.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public static Button FlatButton(string text, int width, int height, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                Font = CreateFont(9F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Surface,
                ForeColor = primary ? Color.White : TextPrimary
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(12, 82, 178) : Color.FromArgb(246, 248, 251);
            button.Resize += delegate { RoundControl(button, 6); };
            RoundControl(button, 6);
            return button;
        }
    }

    /// <summary>Panel with a rounded fill, border and optional soft drop edge.</summary>
    internal class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }
        public bool DrawBorder { get; set; }

        public RoundedPanel()
        {
            Radius = 10;
            BorderColor = UiTheme.Border;
            DrawBorder = true;
            BackColor = UiTheme.Surface;
            DoubleBuffered = true;
            Resize += delegate { UiTheme.RoundControl(this, Radius); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!DrawBorder) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Single-line text box with a native cue banner placeholder.</summary>
    internal class CueTextBox : TextBox
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string CueText { get; set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!string.IsNullOrEmpty(CueText))
                SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, CueText);
        }
    }
}
