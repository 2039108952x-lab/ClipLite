using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipLite
{
    internal static class UiTheme
    {
        public static readonly Color Surface = Color.FromArgb(249, 249, 249);
        public static readonly Color SurfaceContainerLow = Color.FromArgb(243, 243, 243);
        public static readonly Color SurfaceContainer = Color.FromArgb(238, 238, 238);
        public static readonly Color SurfaceContainerHigh = Color.FromArgb(232, 232, 232);
        public static readonly Color OnSurface = Color.FromArgb(26, 28, 28);
        public static readonly Color OnSurfaceVariant = Color.FromArgb(65, 71, 82);
        public static readonly Color Outline = Color.FromArgb(193, 198, 212);
        public static readonly Color OutlineSoft = Color.FromArgb(226, 229, 235);
        public static readonly Color Primary = Color.FromArgb(0, 79, 150);
        public static readonly Color PrimarySoft = Color.FromArgb(225, 237, 250);
        public static readonly Color Tertiary = Color.FromArgb(168, 76, 0);
        public static readonly Color Error = Color.FromArgb(186, 26, 26);
        public static readonly Color InverseSurface = Color.FromArgb(47, 49, 49);
        public static readonly Color InverseOnSurface = Color.FromArgb(241, 241, 241);

        public static readonly string FontFamilyName = ResolveFontFamily();
        public static readonly Font HeaderFont = new Font(FontFamilyName, 10.5f, FontStyle.Bold);
        public static readonly Font ItemFont = new Font(FontFamilyName, 9.5f, FontStyle.Regular);
        public static readonly Font MetaFont = new Font(FontFamilyName, 8.0f, FontStyle.Regular);
        public static readonly Font ChipFont = new Font(FontFamilyName, 7.5f, FontStyle.Bold);
        public static readonly Font IconFont = new Font("Segoe UI Symbol", 9.0f, FontStyle.Regular);

        private static string ResolveFontFamily()
        {
            try
            {
                using (var font = new Font("Segoe UI Variable Text", 9.0f))
                {
                    if (font.Name.IndexOf("Segoe UI Variable", StringComparison.OrdinalIgnoreCase) >= 0)
                        return font.Name;
                }
            }
            catch { }
            return "Segoe UI";
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;

            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            using (var path = RoundedRect(bounds, radius))
                graphics.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int radius)
        {
            using (var path = RoundedRect(bounds, radius))
                graphics.DrawPath(pen, path);
        }
    }

    internal static class NativeWindowEffects
    {
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaSystemBackdropType = 38;
        private const int DwmWindowCornerRound = 2;
        private const int DwmSystemBackdropMica = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int valueSize);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(
            int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        public static void Apply(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;

            try
            {
                int corner = DwmWindowCornerRound;
                DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference,
                    ref corner, Marshal.SizeOf(typeof(int)));

                int backdrop = DwmSystemBackdropMica;
                DwmSetWindowAttribute(form.Handle, DwmwaSystemBackdropType,
                    ref backdrop, Marshal.SizeOf(typeof(int)));
            }
            catch
            {
                // Windows versions without these attributes use the solid surface fallback.
            }

            ApplyRoundedRegion(form, 8);
        }

        public static void ApplyRoundedRegion(Form form, int radius)
        {
            if (form == null || form.Width <= 0 || form.Height <= 0) return;
            IntPtr region = IntPtr.Zero;
            try
            {
                int diameter = radius * 2;
                region = CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1,
                    diameter, diameter);
                if (region != IntPtr.Zero)
                {
                    var oldRegion = form.Region;
                    form.Region = Region.FromHrgn(region);
                    if (oldRegion != null) oldRegion.Dispose();
                }
            }
            catch { }
            finally
            {
                if (region != IntPtr.Zero) DeleteObject(region);
            }
        }
    }
}
