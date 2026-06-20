using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipLite
{
    public class HistoryForm : Form
    {
        // ── Cached GDI+ resources (avoid creation/disposal per OnDrawItem) ──
        private static readonly Brush _bgNormal = new SolidBrush(Color.FromArgb(250, 250, 250));
        private static readonly Brush _bgSelected = new SolidBrush(Color.FromArgb(200, 220, 240));
        private static readonly Brush _bgHover = new SolidBrush(Color.FromArgb(235, 245, 252));
        private static readonly Pen _sepPen = new Pen(Color.FromArgb(235, 235, 235));
        private static readonly Brush _pinBrush = new SolidBrush(Color.FromArgb(255, 160, 0));
        private static readonly Brush _textBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        private static readonly Brush _infoBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        private static readonly Brush _copyBtnBg = new SolidBrush(Color.FromArgb(245, 245, 245));
        private static readonly Pen _copyBtnBorder = new Pen(Color.FromArgb(200, 200, 200));
        private static readonly Brush _copyBtnText = new SolidBrush(Color.FromArgb(0, 100, 200));
        private static readonly Brush _delBtnText = new SolidBrush(Color.FromArgb(200, 60, 60));
        private static readonly Brush _pinBtnText = new SolidBrush(Color.FromArgb(230, 130, 0));
        private static readonly Brush _pinBtnActiveBg = new SolidBrush(Color.FromArgb(255, 180, 60));
        private static readonly Pen _pinBtnActiveBorder = new Pen(Color.FromArgb(220, 150, 40));
        private static readonly Font _pinFont = new Font("Segoe UI", 7, FontStyle.Bold);
        private static readonly Font _textFont = new Font("Segoe UI", 10);
        private static readonly Font _infoFont = new Font("Segoe UI", 8);
        private static readonly Font _btnFont = new Font("Segoe UI", 8);

        private TextBox _searchBox;
        private BufferedListBox _listBox;
        private Label _closeBtn;
        private List<ClipboardEntry> _allEntries = new List<ClipboardEntry>();
        private List<ClipboardEntry> _filteredEntries = new List<ClipboardEntry>();
        private ThumbnailCache _thumbCache;
        private SafeStorage _storage;
        private Dictionary<string, Image> _thumbCacheLocal = new Dictionary<string, Image>();
        private int _hoveredIndex = -1;

        public event Action<string> ItemSelected;
#pragma warning disable 67  // EntryCopied is subscribed externally
        public event Action<ClipboardEntry> EntryCopied;

#pragma warning restore 67

        public HistoryForm(SafeStorage storage, ThumbnailCache thumbCache)
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            _storage = storage;

            _thumbCache = thumbCache;
            UpdateFilter();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private void InitializeComponent()
        {
            this.Text = "ClipLite";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Width = 620;
            this.Height = 520;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(250, 250, 250);

            var titleBar = new Panel
            {
                Height = 36,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(240, 240, 240)
            };

            var titleLabel = new Label
            {
                Text = "剪贴板历史",
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(60, 60, 60),
                Left = 12,
                Top = 8,
                AutoSize = true
            };
            titleBar.Controls.Add(titleLabel);

            _closeBtn = new Label
            {
                Text = "X",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(28, 28),
                Location = new Point(this.Width - 36, 4),
                Cursor = Cursors.Hand
            };
            _closeBtn.MouseEnter += (s, e) => _closeBtn.ForeColor = Color.FromArgb(0, 0, 0);
            _closeBtn.MouseLeave += (s, e) => _closeBtn.ForeColor = Color.FromArgb(120, 120, 120);
            _closeBtn.Click += (s, e) => Hide();
            titleBar.Controls.Add(_closeBtn);

            _searchBox = new TextBox
            {
                Location = new Point(6, 44),
                Width = 608,
                Height = 30,
                Font = new Font("Segoe UI", 11),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            _searchBox.TextChanged += (s, e) => UpdateFilter();
            _searchBox.KeyDown += OnSearchKeyDown;

            _listBox = new BufferedListBox
            {
                Location = new Point(6, 82),
                Width = 608,
                Height = 428,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawVariable,
                IntegralHeight = false,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(250, 250, 250)
            };
            _listBox.DrawItem += OnDrawItem;
            _listBox.MeasureItem += OnMeasureItem;
            _listBox.MouseDoubleClick += (s, e) => { var idx = _listBox.IndexFromPoint(e.Location); if (idx >= 0) SelectItem(idx); };
            _listBox.MouseClick += OnListMouseClick;
            _listBox.KeyDown += OnListKeyDown;
            _listBox.MouseMove += OnListMouseMove;
            _listBox.MouseLeave += (s, e) => { _hoveredIndex = -1; _listBox.Invalidate(); };

            this.Controls.Add(titleBar);
            this.Controls.Add(_searchBox);
            this.Controls.Add(_listBox);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
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
            int idx = _allEntries.FindIndex(e => e.Id == entryId);
            if (idx > 0)
            {
                var entry = _allEntries[idx];
                _allEntries.RemoveAt(idx);
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
            {
                var entry = _filteredEntries[_listBox.SelectedIndex];
                RemoveEntry(entry.Id);
            }
        }

        private void UpdateFilter()
        {
            string searchText = _searchBox.Text;
            string query = (searchText != null) ? searchText.Trim() : "";
            if (string.IsNullOrEmpty(query))
            {
                _filteredEntries = _allEntries
                    .OrderByDescending(e => e.IsPinned)
                    .ThenByDescending(e => e.Timestamp)
                    .ToList();
            }
            else
            {
                _filteredEntries = _allEntries
                    .Where(e => e.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(e => e.IsPinned)
                    .ThenByDescending(e => e.Timestamp)
                    .ToList();
            }

            _listBox.BeginUpdate();
            _hoveredIndex = -1; // reset stale hover index to avoid crash on GetItemRectangle
            _listBox.Items.Clear();
            foreach (var entry in _filteredEntries)
                _listBox.Items.Add(entry);
            _listBox.EndUpdate();
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
                if (_listBox.Items.Count > 0)
                    SelectItem(0);
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
            int idx = _listBox.IndexFromPoint(e.Location);
            
            // Only track hover for button display — no auto-select (avoids flickering)
            if (idx != _hoveredIndex)
            {
                int oldIdx = _hoveredIndex;
                _hoveredIndex = idx;
                
                // Guard: InvalidateItem only if the index is within current bounds
                if (oldIdx >= 0 && oldIdx < _listBox.Items.Count)
                    _listBox.Invalidate(_listBox.GetItemRectangle(oldIdx));
                if (idx >= 0 && idx < _listBox.Items.Count)
                    _listBox.Invalidate(_listBox.GetItemRectangle(idx));
            }
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;
            var entry = _filteredEntries[index];
            if (ItemSelected != null) ItemSelected(entry.Text);
            if (EntryCopied != null) EntryCopied(entry);
            Hide();
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;

            var entry = (ClipboardEntry)_listBox.Items[e.Index];
            var g = e.Graphics;
            var bounds = e.Bounds;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected ||
                            (_listBox.SelectedIndex == e.Index);

            bool hovered = (e.Index == _hoveredIndex);

            // Background: selected → blue, hovered → light blue, normal → white
            Brush bg = selected ? _bgSelected : (hovered ? _bgHover : _bgNormal);
            g.FillRectangle(bg, bounds);

            // Separator line
            g.DrawLine(_sepPen, bounds.X + 8, bounds.Bottom - 1, bounds.Right - 8, bounds.Bottom - 1);

            int x = bounds.X + 6;
            int y = bounds.Y + 5;
            int maxW = bounds.Width - 12;

            if (entry.IsPinned)
            {
                g.DrawString("置顶", _pinFont, _pinBrush, x, y);
                y += 14;
            }

            // Preview text
            string preview = entry.PreviewText;
            var textRect = new Rectangle(x, y, maxW, 22);
            TextRenderer.DrawText(g, preview, _textFont, textRect, Color.FromArgb(40, 40, 40),
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Type + timestamp (bottom line)
            string typeText = entry.TypeDisplay;
            string timeText = entry.TimeDisplay;
            TextRenderer.DrawText(g, typeText, _infoFont,
                new Rectangle(x, bounds.Bottom - 17, 80, 16), Color.FromArgb(150, 150, 150),
                TextFormatFlags.NoPrefix);
            Size timeSz = TextRenderer.MeasureText(timeText, _infoFont);
            TextRenderer.DrawText(g, timeText, _infoFont,
                new Rectangle(bounds.Right - timeSz.Width - 8, bounds.Bottom - 17, timeSz.Width, 16),
                Color.FromArgb(150, 150, 150), TextFormatFlags.NoPrefix);

            // Hover buttons (pin + copy + delete, shown only on hovered item)
            if (hovered)
            {
                var pinRect  = GetPinButtonRect(e.Index);
                var copyRect = GetCopyButtonRect(e.Index);
                var delRect  = GetDeleteButtonRect(e.Index);

                // Pin button — orange bg when already pinned
                if (entry.IsPinned)
                {
                    g.FillRectangle(_pinBtnActiveBg, pinRect);
                    g.DrawRectangle(_pinBtnActiveBorder, pinRect);
                    TextRenderer.DrawText(g, "置顶", _btnFont, pinRect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    g.FillRectangle(_copyBtnBg, pinRect);
                    g.DrawRectangle(_copyBtnBorder, pinRect);
                    TextRenderer.DrawText(g, "置顶", _btnFont, pinRect, Color.FromArgb(230, 130, 0),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                // Copy button
                g.FillRectangle(_copyBtnBg, copyRect);
                g.DrawRectangle(_copyBtnBorder, copyRect);
                TextRenderer.DrawText(g, "复制", _btnFont, copyRect, Color.FromArgb(0, 100, 200),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Delete button
                g.FillRectangle(_copyBtnBg, delRect);
                g.DrawRectangle(_copyBtnBorder, delRect);
                TextRenderer.DrawText(g, "删除", _btnFont, delRect, Color.FromArgb(200, 60, 60),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private Rectangle GetPinButtonRect(int index)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            var bounds = _listBox.GetItemRectangle(index);
            return new Rectangle(bounds.Right - 118, bounds.Y + 4, 36, 20);
        }

        private Rectangle GetCopyButtonRect(int index)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            var bounds = _listBox.GetItemRectangle(index);
            return new Rectangle(bounds.Right - 78, bounds.Y + 4, 36, 20);
        }

        private Rectangle GetDeleteButtonRect(int index)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            var bounds = _listBox.GetItemRectangle(index);
            return new Rectangle(bounds.Right - 38, bounds.Y + 4, 36, 20);
        }

        private void OnListMouseClick(object sender, MouseEventArgs e)
        {
            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _listBox.Items.Count) return;

            var pinRect  = GetPinButtonRect(idx);
            var copyRect = GetCopyButtonRect(idx);
            var delRect  = GetDeleteButtonRect(idx);

            if (delRect.Contains(e.Location))
            {
                var entry = _filteredEntries[idx];
                RemoveEntry(entry.Id);
                return;
            }
            if (pinRect.Contains(e.Location))
            {
                var entry = _filteredEntries[idx];
                TogglePin(entry.Id);
                return;
            }
            // Click on "复制" button or item background → copy + hide
            SelectItem(idx);
        }

        private void OnMeasureItem(object sender, MeasureItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;
            var entry = (ClipboardEntry)_listBox.Items[e.Index];
            e.ItemHeight = entry.IsPinned ? 56 : 42;
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
    }

    /// <summary>
    /// Double-buffered ListBox to eliminate flicker during owner-draw repaints.
    /// </summary>
    internal class BufferedListBox : ListBox
    {
        public BufferedListBox()
        {
            DoubleBuffered = true;
        }
    }

    internal class ToastForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNA = 8; // Show without activating

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 2;
        private const uint SWP_NOSIZE = 1;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private Timer _timer;
        private Label _label;
        private static bool _toastEnabled = true;

        public static bool ToastEnabled
        {
            get { return _toastEnabled; }
            set { _toastEnabled = value; }
        }

        public static void ShowToast(string typeLabel)
        {
            if (!ToastEnabled) return;

            try
            {
                string text = "✔ 已复制";
                if (!string.IsNullOrEmpty(typeLabel)) text += " (" + typeLabel + ")";

                var toast = new ToastForm();
                toast._label.Text = text;

                // Force handle creation BEFORE positioning/showing
                IntPtr h = toast.Handle;

                int sw = Screen.PrimaryScreen.WorkingArea.Width;
                int sh = Screen.PrimaryScreen.WorkingArea.Height;

                // Force window to topmost and visible via P/Invoke
                SetWindowPos(h, HWND_TOPMOST,
                    (sw - toast.Width) / 2, sh - toast.Height - 50,
                    toast.Width, toast.Height,
                    SWP_SHOWWINDOW);

                // Show without activating (so focus stays on previous window)
                ShowWindow(h, SW_SHOWNA);
            }
            catch
            {
                // Toast is cosmetic only
            }
        }

        private ToastForm()
        {
            Width = 220;
            Height = 46;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(45, 45, 45);

            _label = new Label
            {
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            _label.Click += (s, e) => Close();
            Controls.Add(_label);
            Click += (s, e) => Close();

            _timer = new Timer { Interval = 1500 };
            _timer.Tick += (s, e) => { _timer.Stop(); Close(); };
        }

        protected override void SetVisibleCore(bool value)
        {
            // Force visible — prevent WinForms from suppressing the show
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            base.Dispose(disposing);
        }
    }
}
