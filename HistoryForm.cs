using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipLite
{
    public class HistoryForm : Form
    {
        private const int WindowWidth = 380;
        private const int WindowHeight = 560;
        private const int ItemHeight = 64;

        private TextBox _searchBox;
        private RoundedPanel _searchContainer;
        private BufferedListBox _listBox;
        private Button _settingsButton;
        private Button _closeButton;
        private Label _emptyLabel;
        private StatusPill _statusPill;
        private Timer _statusTimer;
        private ToolTip _toolTip;

        private List<ClipboardEntry> _allEntries = new List<ClipboardEntry>();
        private List<ClipboardEntry> _filteredEntries = new List<ClipboardEntry>();
        private int _hoveredIndex = -1;
        private string _lastToolTip = "";

        public event Action<string> ItemSelected;
        public event Action<ClipboardEntry> EntryCopied;
        public event Action SettingsRequested;

        public HistoryForm(SafeStorage storage, ThumbnailCache thumbCache)
        {
            InitializeComponent();
            UpdateFilter();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "ClipLite";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(WindowWidth, WindowHeight);
            MinimumSize = new Size(WindowWidth, WindowHeight);
            MaximumSize = new Size(WindowWidth, WindowHeight);
            StartPosition = FormStartPosition.Manual;
            BackColor = UiTheme.Surface;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            var header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(WindowWidth, 48),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var title = new Label
            {
                Text = "剪贴板历史",
                Font = UiTheme.HeaderFont,
                ForeColor = UiTheme.OnSurface,
                Location = new Point(16, 14),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            header.Controls.Add(title);

            _settingsButton = CreateHeaderButton("⚙", "设置");
            _settingsButton.Location = new Point(WindowWidth - 70, 9);
            _settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _settingsButton.Click += (s, e) =>
            {
                Hide();
                if (SettingsRequested != null) SettingsRequested();
            };
            header.Controls.Add(_settingsButton);

            _closeButton = CreateHeaderButton("×", "关闭");
            _closeButton.Location = new Point(WindowWidth - 38, 9);
            _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _closeButton.Click += (s, e) => Hide();
            header.Controls.Add(_closeButton);

            _searchContainer = new RoundedPanel
            {
                Location = new Point(12, 52),
                Size = new Size(WindowWidth - 24, 36),
                Radius = 5,
                FillColor = Color.FromArgb(252, 252, 252),
                BorderColor = UiTheme.OutlineSoft,
                FocusBorderColor = UiTheme.Primary,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var searchIcon = new SearchIconControl
            {
                Location = new Point(10, 9),
                Size = new Size(18, 18),
                BackColor = Color.Transparent
            };
            _searchContainer.Controls.Add(searchIcon);

            _searchBox = new TextBox
            {
                Location = new Point(34, 8),
                Width = WindowWidth - 72,
                Height = 22,
                BorderStyle = BorderStyle.None,
                Font = new Font(UiTheme.FontFamilyName, 9.0f),
                ForeColor = UiTheme.OnSurface,
                BackColor = Color.FromArgb(252, 252, 252),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _searchBox.TextChanged += (s, e) => UpdateFilter();
            _searchBox.KeyDown += OnSearchKeyDown;
            _searchBox.HandleCreated += (s, e) => SetCueBanner(_searchBox, "搜索剪贴板内容...");
            _searchBox.Enter += (s, e) => { _searchContainer.HasInputFocus = true; };
            _searchBox.Leave += (s, e) => { _searchContainer.HasInputFocus = false; };
            _searchContainer.Controls.Add(_searchBox);

            _listBox = new BufferedListBox
            {
                Location = new Point(12, 96),
                Size = new Size(WindowWidth - 24, WindowHeight - 108),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = ItemHeight,
                IntegralHeight = false,
                Font = UiTheme.ItemFont,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.OnSurface,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                         AnchorStyles.Left | AnchorStyles.Right
            };
            _listBox.DrawItem += OnDrawItem;
            _listBox.MouseClick += OnListMouseClick;
            _listBox.KeyDown += OnListKeyDown;
            _listBox.MouseMove += OnListMouseMove;
            _listBox.MouseLeave += (s, e) =>
            {
                int previous = _hoveredIndex;
                _hoveredIndex = -1;
                InvalidateItem(previous);
                SetListToolTip("");
            };

            _emptyLabel = new Label
            {
                Text = "暂无剪贴板记录",
                Font = new Font(UiTheme.FontFamilyName, 9.0f),
                ForeColor = Color.FromArgb(125, 130, 138),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = _listBox.Location,
                Size = _listBox.Size,
                BackColor = UiTheme.Surface,
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                         AnchorStyles.Left | AnchorStyles.Right
            };

            _statusPill = new StatusPill
            {
                Size = new Size(160, 32),
                Visible = false,
                Anchor = AnchorStyles.Bottom
            };

            _statusTimer = new Timer { Interval = 1800 };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                _statusPill.Visible = false;
            };

            _toolTip = new ToolTip
            {
                InitialDelay = 450,
                ReshowDelay = 100,
                AutoPopDelay = 2500,
                ShowAlways = true
            };
            _toolTip.SetToolTip(_settingsButton, "设置");
            _toolTip.SetToolTip(_closeButton, "关闭");

            Controls.Add(header);
            Controls.Add(_searchContainer);
            Controls.Add(_listBox);
            Controls.Add(_emptyLabel);
            Controls.Add(_statusPill);

            Layout += (s, e) => PositionStatusPill();
            ResumeLayout(false);
        }

        private Button CreateHeaderButton(string text, string accessibleName)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Size = new Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", text == "×" ? 14.0f : 10.0f),
                ForeColor = UiTheme.OnSurfaceVariant,
                BackColor = UiTheme.Surface,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiTheme.SurfaceContainerHigh;
            button.FlatAppearance.MouseDownBackColor = UiTheme.SurfaceContainer;
            return button;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWindowEffects.Apply(this);
            SetCueBanner(_searchBox, "搜索剪贴板内容...");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            NativeWindowEffects.ApplyRoundedRegion(this, 8);
            PositionStatusPill();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(205, 209, 218)))
            {
                var bounds = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                UiTheme.DrawRoundedRectangle(e.Graphics, pen, bounds, 8);
            }
        }

        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox textBox, string text)
        {
            if (textBox == null || !textBox.IsHandleCreated) return;
            try { SendMessage(textBox.Handle, EmSetCueBanner, (IntPtr)1, text); }
            catch { }
        }

        public void SetEntries(List<ClipboardEntry> entries)
        {
            _allEntries = entries ?? new List<ClipboardEntry>();
            UpdateFilter();
        }

        public List<ClipboardEntry> GetAllEntries()
        {
            return _allEntries;
        }

        public void AddEntry(ClipboardEntry entry)
        {
            _allEntries.Insert(0, entry);
            if (_allEntries.Count > SafeStorage.MaxEntries)
                _allEntries.RemoveAt(_allEntries.Count - 1);
            UpdateFilter();
        }

        public void MoveToTop(string entryId)
        {
            int index = _allEntries.FindIndex(e => e.Id == entryId);
            if (index > 0)
            {
                var entry = _allEntries[index];
                _allEntries.RemoveAt(index);
                _allEntries.Insert(0, entry);
                UpdateFilter();
            }
        }

        public void FocusSearch()
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public void ClearEntries()
        {
            _allEntries.Clear();
            UpdateFilter();
        }

        public void RemoveEntry(string entryId)
        {
            _allEntries.RemoveAll(e => e.Id == entryId);
            UpdateFilter();
        }

        public void TogglePin(string entryId)
        {
            var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null)
            {
                entry.IsPinned = !entry.IsPinned;
                UpdateFilter();
            }
        }

        public void DeleteSelected()
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _filteredEntries.Count)
                RemoveEntry(_filteredEntries[_listBox.SelectedIndex].Id);
        }

        private void UpdateFilter()
        {
            string query = (_searchBox == null ? "" : _searchBox.Text ?? "").Trim();
            IEnumerable<ClipboardEntry> entries = _allEntries;

            if (!string.IsNullOrEmpty(query))
            {
                entries = entries.Where(e =>
                    (e.Text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.PreviewText ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _filteredEntries = entries
                .OrderByDescending(e => e.IsPinned)
                .ThenByDescending(e => e.Timestamp)
                .ToList();

            if (_listBox == null) return;
            _listBox.BeginUpdate();
            _hoveredIndex = -1;
            _listBox.Items.Clear();
            foreach (var entry in _filteredEntries)
                _listBox.Items.Add(entry);
            _listBox.EndUpdate();

            bool empty = _filteredEntries.Count == 0;
            _emptyLabel.Text = string.IsNullOrEmpty(query)
                ? "暂无剪贴板记录"
                : "没有匹配的记录";
            _emptyLabel.Visible = empty;
            if (empty) _emptyLabel.BringToFront();
            _statusPill.BringToFront();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (_listBox.Items.Count > 0)
                {
                    _listBox.SelectedIndex = 0;
                    _listBox.Focus();
                }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_listBox.Items.Count > 0) SelectItem(0);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.SuppressKeyPress = true;
            }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _listBox.SelectedIndex >= 0)
            {
                SelectItem(_listBox.SelectedIndex);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete && _listBox.SelectedIndex >= 0)
            {
                DeleteSelected();
                e.SuppressKeyPress = true;
            }
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            int index = _listBox.IndexFromPoint(e.Location);
            if (index != _hoveredIndex)
            {
                int previous = _hoveredIndex;
                _hoveredIndex = index;
                InvalidateItem(previous);
                InvalidateItem(index);
            }

            string tip = "";
            if (index >= 0 && index < _listBox.Items.Count)
            {
                if (GetPinButtonRect(index).Contains(e.Location)) tip = "置顶/取消置顶";
                else if (GetCopyButtonRect(index).Contains(e.Location)) tip = "复制";
                else if (GetDeleteButtonRect(index).Contains(e.Location)) tip = "删除";
            }
            SetListToolTip(tip);
        }

        private void SetListToolTip(string text)
        {
            if (_lastToolTip == text) return;
            _lastToolTip = text;
            _toolTip.SetToolTip(_listBox, text);
        }

        private void InvalidateItem(int index)
        {
            if (index >= 0 && index < _listBox.Items.Count)
                _listBox.Invalidate(_listBox.GetItemRectangle(index));
        }

        public void ShowStatus(string text)
        {
            if (!Visible) return;
            _statusPill.Text = text;
            int width = TextRenderer.MeasureText(text, UiTheme.MetaFont).Width + 34;
            _statusPill.Width = Math.Max(128, Math.Min(230, width));
            PositionStatusPill();
            _statusPill.Visible = true;
            _statusPill.BringToFront();
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void PositionStatusPill()
        {
            if (_statusPill == null) return;
            _statusPill.Left = Math.Max(0, (ClientSize.Width - _statusPill.Width) / 2);
            _statusPill.Top = Math.Max(0, ClientSize.Height - _statusPill.Height - 20);
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;
            var entry = _filteredEntries[index];
            if (ItemSelected != null) ItemSelected(entry.Text);

            // Hide first so copy feedback uses the non-activating floating pill.
            Hide();
            if (EntryCopied != null) EntryCopied(entry);
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;

            var entry = (ClipboardEntry)_listBox.Items[e.Index];
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = e.Bounds;
            using (var clearBrush = new SolidBrush(UiTheme.Surface))
                graphics.FillRectangle(clearBrush, bounds);

            var card = new Rectangle(bounds.X + 1, bounds.Y + 2,
                Math.Max(1, bounds.Width - 3), Math.Max(1, bounds.Height - 4));
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected ||
                            _listBox.SelectedIndex == e.Index;
            bool hovered = e.Index == _hoveredIndex;

            if (selected || hovered)
            {
                Color fill = selected ? UiTheme.PrimarySoft : UiTheme.SurfaceContainerLow;
                using (var brush = new SolidBrush(fill))
                    UiTheme.FillRoundedRectangle(graphics, brush, card, 5);
            }

            if (selected)
            {
                var indicator = new Rectangle(card.X + 2, card.Y + 10, 3, card.Height - 20);
                using (var brush = new SolidBrush(UiTheme.Primary))
                    UiTheme.FillRoundedRectangle(graphics, brush, indicator, 2);
            }

            int contentX = card.X + 12;
            int actionWidth = hovered ? 82 : 0;
            int textWidth = Math.Max(40, card.Width - 24 - actionWidth);
            var textRect = new Rectangle(contentX, card.Y + 7, textWidth, 31);
            TextRenderer.DrawText(graphics, entry.PreviewText ?? "", UiTheme.ItemFont,
                textRect, UiTheme.OnSurface,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);

            int metaY = card.Bottom - 19;
            int nextX = contentX;
            nextX += DrawChip(graphics, entry.TypeDisplay, nextX, metaY,
                UiTheme.OnSurfaceVariant, UiTheme.OutlineSoft) + 6;
            if (entry.IsPinned)
                DrawChip(graphics, "置顶", nextX, metaY, UiTheme.Tertiary,
                    Color.FromArgb(230, 181, 145));

            string time = entry.TimeDisplay;
            Size timeSize = TextRenderer.MeasureText(time, UiTheme.MetaFont,
                new Size(120, 18), TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, time, UiTheme.MetaFont,
                new Rectangle(card.Right - timeSize.Width - 10, metaY + 1,
                    timeSize.Width, 16),
                Color.FromArgb(122, 128, 137),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (hovered)
            {
                DrawActionButton(graphics, GetPinButtonRect(e.Index), ActionKind.Pin,
                    entry.IsPinned ? UiTheme.Tertiary : UiTheme.OnSurfaceVariant);
                DrawActionButton(graphics, GetCopyButtonRect(e.Index), ActionKind.Copy,
                    UiTheme.Primary);
                DrawActionButton(graphics, GetDeleteButtonRect(e.Index), ActionKind.Delete,
                    UiTheme.Error);
            }
        }

        private int DrawChip(Graphics graphics, string text, int x, int y,
            Color textColor, Color borderColor)
        {
            Size measured = TextRenderer.MeasureText(text, UiTheme.ChipFont,
                new Size(80, 16), TextFormatFlags.NoPadding);
            int width = measured.Width + 10;
            var rect = new Rectangle(x, y, width, 16);
            using (var pen = new Pen(borderColor))
                UiTheme.DrawRoundedRectangle(graphics, pen, rect, 3);
            TextRenderer.DrawText(graphics, text, UiTheme.ChipFont,
                new Rectangle(rect.X + 5, rect.Y + 1, rect.Width - 10, rect.Height - 2),
                textColor, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.VerticalCenter);
            return width;
        }

        private enum ActionKind { Pin, Copy, Delete }

        private void DrawActionButton(Graphics graphics, Rectangle rect,
            ActionKind kind, Color iconColor)
        {
            using (var brush = new SolidBrush(Color.FromArgb(248, 248, 248)))
                UiTheme.FillRoundedRectangle(graphics, brush, rect, 4);
            using (var pen = new Pen(UiTheme.OutlineSoft))
                UiTheme.DrawRoundedRectangle(graphics, pen, rect, 4);

            using (var pen = new Pen(iconColor, 1.35f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = rect.X + rect.Width / 2;
                int cy = rect.Y + rect.Height / 2;

                if (kind == ActionKind.Copy)
                {
                    graphics.DrawRectangle(pen, cx - 5, cy - 4, 8, 8);
                    graphics.DrawRectangle(pen, cx - 2, cy - 7, 8, 8);
                }
                else if (kind == ActionKind.Delete)
                {
                    graphics.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    graphics.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
                }
                else
                {
                    graphics.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy - 4);
                    graphics.DrawLine(pen, cx - 3, cy - 4, cx - 1, cy + 1);
                    graphics.DrawLine(pen, cx + 3, cy - 4, cx + 1, cy + 1);
                    graphics.DrawLine(pen, cx - 1, cy + 1, cx + 1, cy + 1);
                    graphics.DrawLine(pen, cx, cy + 1, cx, cy + 6);
                }
            }
        }

        private Rectangle GetPinButtonRect(int index)
        {
            return GetActionButtonRect(index, 2);
        }

        private Rectangle GetCopyButtonRect(int index)
        {
            return GetActionButtonRect(index, 1);
        }

        private Rectangle GetDeleteButtonRect(int index)
        {
            return GetActionButtonRect(index, 0);
        }

        private Rectangle GetActionButtonRect(int index, int slotFromRight)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            Rectangle bounds = _listBox.GetItemRectangle(index);
            int x = bounds.Right - 28 - slotFromRight * 27;
            return new Rectangle(x, bounds.Y + 7, 24, 24);
        }

        private void OnListMouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                int index = _listBox.IndexFromPoint(e.Location);
                if (index < 0 || index >= _filteredEntries.Count) return;

                var entry = _filteredEntries[index];
                bool actionButtonsVisible = index == _hoveredIndex;
                if (actionButtonsVisible && GetDeleteButtonRect(index).Contains(e.Location))
                {
                    RemoveEntry(entry.Id);
                    return;
                }
                if (actionButtonsVisible && GetPinButtonRect(index).Contains(e.Location))
                {
                    TogglePin(entry.Id);
                    return;
                }

                // The copy icon and the rest of the item share the fast copy action.
                SelectItem(index);
            }
            catch
            {
                // Rapid list changes must never close the application.
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_statusTimer != null) _statusTimer.Dispose();
                if (_toolTip != null) _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class BufferedListBox : ListBox
    {
        public BufferedListBox()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    internal class RoundedPanel : Panel
    {
        private bool _hasInputFocus;

        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color FocusBorderColor { get; set; }

        public bool HasInputFocus
        {
            get { return _hasInputFocus; }
            set
            {
                if (_hasInputFocus == value) return;
                _hasInputFocus = value;
                Invalidate();
            }
        }

        public RoundedPanel()
        {
            DoubleBuffered = true;
            Radius = 5;
            FillColor = Color.White;
            BorderColor = UiTheme.OutlineSoft;
            FocusBorderColor = UiTheme.Primary;
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(FillColor))
                UiTheme.FillRoundedRectangle(e.Graphics, brush, bounds, Radius);
            using (var pen = new Pen(HasInputFocus ? FocusBorderColor : BorderColor))
                UiTheme.DrawRoundedRectangle(e.Graphics, pen, bounds, Radius);
        }
    }

    internal class SearchIconControl : Control
    {
        public SearchIconControl()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(145, 150, 160), 1.4f))
            {
                e.Graphics.DrawEllipse(pen, 3, 3, 8, 8);
                e.Graphics.DrawLine(pen, 10, 10, 15, 15);
            }
        }
    }

    internal class StatusPill : Control
    {
        public StatusPill()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            Font = UiTheme.MetaFont;
            ForeColor = UiTheme.InverseOnSurface;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(UiTheme.InverseSurface))
                UiTheme.FillRoundedRectangle(e.Graphics, brush,
                    new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }

}
