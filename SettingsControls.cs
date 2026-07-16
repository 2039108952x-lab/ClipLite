using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipLite
{
    internal class ModernCheckBox : CheckBox
    {
        public ModernCheckBox()
        {
            AutoSize = false;
            Height = 26;
            Font = UiTheme.ItemFont;
            ForeColor = UiTheme.OnSurface;
            BackColor = UiTheme.Surface;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var box = new Rectangle(1, (Height - 18) / 2, 18, 18);

            if (Checked)
            {
                using (var brush = new SolidBrush(UiTheme.Primary))
                    UiTheme.FillRoundedRectangle(e.Graphics, brush, box, 3);
                using (var pen = new Pen(Color.White, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, box.X + 4, box.Y + 9, box.X + 8, box.Y + 13);
                    e.Graphics.DrawLine(pen, box.X + 8, box.Y + 13, box.X + 15, box.Y + 5);
                }
            }
            else
            {
                using (var brush = new SolidBrush(Color.FromArgb(253, 253, 253)))
                    UiTheme.FillRoundedRectangle(e.Graphics, brush, box, 3);
                using (var pen = new Pen(UiTheme.Outline))
                    UiTheme.DrawRoundedRectangle(e.Graphics, pen, box, 3);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(28, 0, Width - 28, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal class ModernRadioButton : RadioButton
    {
        public ModernRadioButton()
        {
            AutoSize = false;
            Height = 26;
            Font = UiTheme.ItemFont;
            ForeColor = UiTheme.OnSurface;
            BackColor = UiTheme.Surface;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var circle = new Rectangle(1, (Height - 18) / 2, 18, 18);
            using (var brush = new SolidBrush(Color.FromArgb(253, 253, 253)))
                e.Graphics.FillEllipse(brush, circle);
            using (var pen = new Pen(Checked ? UiTheme.Primary : UiTheme.Outline))
                e.Graphics.DrawEllipse(pen, circle);
            if (Checked)
            {
                using (var brush = new SolidBrush(UiTheme.Primary))
                    e.Graphics.FillEllipse(brush, circle.X + 5, circle.Y + 5, 8, 8);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(28, 0, Width - 28, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            AutoSize = false;
            Size = new Size(48, 24);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var track = new Rectangle(0, 2, Width - 1, Height - 5);
            using (var brush = new SolidBrush(Checked ? UiTheme.Primary : UiTheme.Outline))
                UiTheme.FillRoundedRectangle(e.Graphics, brush, track, track.Height / 2);

            int knobSize = track.Height - 4;
            int knobX = Checked ? track.Right - knobSize - 2 : track.X + 2;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, knobX, track.Y + 2, knobSize, knobSize);
        }
    }

    internal class ModernButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public Color TextColor { get; set; }
        public int Radius { get; set; }

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = UiTheme.ItemFont;
            Cursor = Cursors.Hand;
            TabStop = true;
            FillColor = UiTheme.Primary;
            HoverColor = Color.FromArgb(0, 92, 174);
            PressedColor = Color.FromArgb(0, 66, 126);
            BorderColor = Color.Transparent;
            TextColor = Color.White;
            Radius = 6;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = _pressed ? PressedColor : (_hovered ? HoverColor : FillColor);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(fill))
                UiTheme.FillRoundedRectangle(e.Graphics, brush, bounds, Radius);
            if (BorderColor.A > 0)
            {
                using (var pen = new Pen(BorderColor))
                    UiTheme.DrawRoundedRectangle(e.Graphics, pen, bounds, Radius);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }

    internal class ExcludedAppsList : ListBox
    {
        private int _hoveredIndex = -1;

        public event Action<int> RemoveRequested;

        public ExcludedAppsList()
        {
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 32;
            IntegralHeight = false;
            Font = UiTheme.ItemFont;
            BackColor = Color.FromArgb(253, 253, 253);
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexFromPoint(e.Location);
            if (index != _hoveredIndex)
            {
                int previous = _hoveredIndex;
                _hoveredIndex = index;
                InvalidateItem(previous);
                InvalidateItem(index);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            int previous = _hoveredIndex;
            _hoveredIndex = -1;
            InvalidateItem(previous);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int index = IndexFromPoint(e.Location);
            if (index >= 0 && GetRemoveRect(index).Contains(e.Location))
            {
                if (RemoveRequested != null) RemoveRequested(index);
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && SelectedIndex >= 0)
            {
                if (RemoveRequested != null) RemoveRequested(SelectedIndex);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = e.Index == _hoveredIndex
                ? UiTheme.SurfaceContainerLow : BackColor;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);

            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 78, e.Bounds.Height),
                UiTheme.OnSurface, TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (e.Index == _hoveredIndex)
            {
                TextRenderer.DrawText(e.Graphics, "移除", UiTheme.MetaFont,
                    GetRemoveRect(e.Index), UiTheme.Error,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);
            }
        }

        private Rectangle GetRemoveRect(int index)
        {
            Rectangle bounds = GetItemRectangle(index);
            return new Rectangle(bounds.Right - 58, bounds.Y, 52, bounds.Height);
        }

        private void InvalidateItem(int index)
        {
            if (index >= 0 && index < Items.Count)
                Invalidate(GetItemRectangle(index));
        }
    }

    internal class StorageInfoCard : Control
    {
        public long CurrentBytes { get; set; }
        public long MaximumBytes { get; set; }
        public int EntryCount { get; set; }

        public StorageInfoCard()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Height = 84;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(UiTheme.SurfaceContainer))
                UiTheme.FillRoundedRectangle(e.Graphics, brush, bounds, 6);

            DrawPair(e.Graphics, "存储占用：", FormatBytes(CurrentBytes) + " / " +
                FormatBytes(MaximumBytes), 12);

            var progressBack = new Rectangle(14, 32, Width - 28, 4);
            using (var brush = new SolidBrush(Color.FromArgb(218, 222, 229)))
                UiTheme.FillRoundedRectangle(e.Graphics, brush, progressBack, 3);
            double ratio = MaximumBytes <= 0 ? 0 : Math.Min(1.0, CurrentBytes / (double)MaximumBytes);
            int progressWidth = (int)Math.Round(progressBack.Width * ratio);
            if (progressWidth > 0)
            {
                using (var brush = new SolidBrush(UiTheme.Primary))
                    UiTheme.FillRoundedRectangle(e.Graphics, brush,
                        new Rectangle(progressBack.X, progressBack.Y, progressWidth, progressBack.Height), 3);
            }

            DrawPair(e.Graphics, "记录条数：", EntryCount.ToString(), 42);
            DrawPair(e.Graphics, "位置：", "cliplite_data/", 61);
        }

        private void DrawPair(Graphics graphics, string label, string value, int y)
        {
            TextRenderer.DrawText(graphics, label, UiTheme.MetaFont,
                new Rectangle(14, y, 110, 18), UiTheme.OnSurfaceVariant,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, value, UiTheme.MetaFont,
                new Rectangle(120, y, Width - 134, 18), UiTheme.OnSurface,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return Math.Round(bytes / 1024.0, 1) + " KB";
            return Math.Round(bytes / (1024.0 * 1024.0), 1) + " MB";
        }
    }
}
