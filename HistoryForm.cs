using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClipLite
{
    public class HistoryForm : Form
    {
        private const int HeaderHeight = 50;

        private Panel _titleBar;
        private Label _titleLabel;
        private Label _settingsButton;
        private Label _closeButton;
        private RoundedPanel _searchPanel;
        private Label _searchIcon;
        private CueTextBox _searchBox;
        private Button _filterButton;
        private ListBox _listBox;
        private Label _emptyLabel;
        private ContextMenuStrip _filterMenu;
        private ContextMenuStrip _itemMenu;
        private ToolStripMenuItem _miCopy;
        private ToolStripMenuItem _miPin;
        private ToolStripMenuItem _miDelete;
        private ToolTip _toolTip;

        private List<ClipboardEntry> _allEntries = new List<ClipboardEntry>();
        private List<ClipboardEntry> _filteredEntries = new List<ClipboardEntry>();
        private ThumbnailCache _thumbCache;
        private SafeStorage _storage;
        private Dictionary<string, Image> _thumbnails = new Dictionary<string, Image>();
        private HashSet<string> _thumbRequests = new HashSet<string>();
        private int _hoveredIndex = -1;
        private string _typeFilter = "all";
        private ClipboardEntry _contextEntry;
        private bool _dragging;
        private Point _dragOffset;

        private Font _titleFont;
        private Font _previewFont;
        private Font _metaFont;
        private Font _badgeFont;
        private Font _emptyFont;

        public event Action<ClipboardEntry> EntryCopied;
        public event Action<ClipboardEntry> EntryRemoved;
        public event Action<ClipboardEntry> EntryEvicted;
        public event Action HistoryChanged;
        public event Action SettingsRequested;

        public int MaxEntries { get; set; }

        public HistoryForm(SafeStorage storage, ThumbnailCache thumbCache)
        {
            _storage = storage;
            _thumbCache = thumbCache;
            MaxEntries = SafeStorage.MaxEntries;
            InitializeComponent();
            UpdateFilter();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiTheme.RoundControl(this, 14);
            ApplyLayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (var pen = new Pen(UiTheme.Border))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void InitializeComponent()
        {
            this.Text = "ClipLite";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Width = 520;
            this.Height = 600;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = UiTheme.Surface;
            this.Font = UiTheme.CreateFont(9F, FontStyle.Regular);
            this.DoubleBuffered = true;

            _titleFont = UiTheme.CreateFont(10.5F, FontStyle.Bold);
            _previewFont = UiTheme.CreateFont(9.5F, FontStyle.Regular);
            _metaFont = UiTheme.CreateFont(8F, FontStyle.Regular);
            _badgeFont = UiTheme.CreateFont(7.5F, FontStyle.Regular);
            _emptyFont = UiTheme.CreateFont(9F, FontStyle.Regular);

            BuildTitleBar();
            BuildSearchBar();
            BuildList();
            BuildMenus();

            _toolTip = new ToolTip();
            _toolTip.ShowAlways = true;
            _toolTip.SetToolTip(_settingsButton, "设置");
            _toolTip.SetToolTip(_closeButton, "关闭");

            this.Controls.Add(_titleBar);
            this.Controls.Add(_searchPanel);
            this.Controls.Add(_listBox);
            this.Controls.Add(_emptyLabel);
            ApplyLayout();
        }

        private void BuildTitleBar()
        {
            _titleBar = new Panel
            {
                Left = 0,
                Top = 0,
                Width = this.Width,
                Height = HeaderHeight,
                BackColor = UiTheme.Surface
            };

            _titleLabel = new Label
            {
                Text = "剪贴板历史",
                Font = _titleFont,
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(16, 15),
                AutoSize = true
            };
            _titleBar.Controls.Add(_titleLabel);

            _titleBar.MouseDown += OnTitleMouseDown;
            _titleBar.MouseMove += OnTitleMouseMove;
            _titleBar.MouseUp += OnTitleMouseUp;
            _titleLabel.MouseDown += OnTitleMouseDown;
            _titleLabel.MouseMove += OnTitleMouseMove;
            _titleLabel.MouseUp += OnTitleMouseUp;

            _settingsButton = new Label
            {
                Text = "⚙",
                Font = UiTheme.CreateFont(11F, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(30, 30),
                Cursor = Cursors.Hand
            };
            _settingsButton.MouseEnter += delegate { _settingsButton.ForeColor = UiTheme.TextPrimary; };
            _settingsButton.MouseLeave += delegate { _settingsButton.ForeColor = UiTheme.TextMuted; };
            _settingsButton.Click += delegate
            {
                Hide();
                if (SettingsRequested != null) SettingsRequested();
            };
            _titleBar.Controls.Add(_settingsButton);

            _closeButton = new Label
            {
                Text = "✕",
                Font = UiTheme.CreateFont(9F, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(30, 30),
                Cursor = Cursors.Hand
            };
            _closeButton.MouseEnter += delegate { _closeButton.ForeColor = UiTheme.TextPrimary; };
            _closeButton.MouseLeave += delegate { _closeButton.ForeColor = UiTheme.TextMuted; };
            _closeButton.Click += delegate { Hide(); };
            _titleBar.Controls.Add(_closeButton);

            var line = new Panel
            {
                Left = 0,
                Top = HeaderHeight - 1,
                Width = this.Width,
                Height = 1,
                BackColor = UiTheme.BorderSoft
            };
            _titleBar.Controls.Add(line);
        }

        private void BuildSearchBar()
        {
            _searchPanel = new RoundedPanel
            {
                Location = new Point(14, 56),
                Size = new Size(this.Width - 28, 40),
                Radius = 10,
                BackColor = UiTheme.SearchFill,
                BorderColor = UiTheme.Border
            };

            _searchIcon = new Label
            {
                Text = "⌕",
                Font = UiTheme.CreateFont(13F, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(22, 22),
                Location = new Point(9, 9),
                BackColor = Color.Transparent
            };
            _searchPanel.Controls.Add(_searchIcon);

            _searchBox = new CueTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.SearchFill,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(9.5F, FontStyle.Regular),
                CueText = "搜索剪贴板历史",
                ShortcutsEnabled = true
            };
            _searchBox.TextChanged += delegate { UpdateFilter(); };
            _searchBox.KeyDown += OnSearchKeyDown;
            _searchBox.GotFocus += delegate
            {
                _searchPanel.BorderColor = UiTheme.Accent;
                _searchPanel.Invalidate();
            };
            _searchBox.LostFocus += delegate
            {
                _searchPanel.BorderColor = UiTheme.Border;
                _searchPanel.Invalidate();
            };
            _searchPanel.Controls.Add(_searchBox);

            _filterButton = new Button
            {
                Text = "全部 ▾",
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.SearchFill,
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.CreateFont(8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _filterButton.FlatAppearance.BorderSize = 0;
            _filterButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 241, 245);
            _filterButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(232, 236, 241);
            _filterButton.Click += delegate { ShowFilterMenu(); };
            _searchPanel.Controls.Add(_filterButton);
        }

        private void BuildList()
        {
            _listBox = new ListBox
            {
                Left = 10,
                Top = 106,
                Width = this.Width - 20,
                Height = this.Height - 118,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawVariable,
                IntegralHeight = false,
                Font = _previewFont,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary
            };
            _listBox.DrawItem += OnDrawItem;
            _listBox.MeasureItem += OnMeasureItem;
            _listBox.MouseClick += OnListMouseClick;
            _listBox.MouseDown += OnListMouseDown;
            _listBox.MouseMove += OnListMouseMove;
            _listBox.MouseWheel += OnListMouseWheel;
            _listBox.MouseLeave += OnListMouseLeave;
            _listBox.KeyDown += OnListKeyDown;
            _listBox.SelectedIndexChanged += delegate { _listBox.Invalidate(); };

            _emptyLabel = new Label
            {
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = UiTheme.TextMuted,
                Font = _emptyFont,
                BackColor = UiTheme.Surface,
                Visible = false
            };
        }

        private void BuildMenus()
        {
            _filterMenu = new ContextMenuStrip();
            AddFilterItem("全部", "all");
            AddFilterItem("文本", "text");
            AddFilterItem("图片", "image");
            AddFilterItem("文件", "filelist");
            AddFilterItem("富文本 / HTML", "richtext");

            _itemMenu = new ContextMenuStrip();
            _miCopy = new ToolStripMenuItem("复制");
            _miCopy.Click += delegate { if (_contextEntry != null) SelectItem(_contextEntry); };
            _miPin = new ToolStripMenuItem("置顶");
            _miPin.Click += delegate
            {
                if (_contextEntry != null) TogglePin(_contextEntry.Id);
            };
            _miDelete = new ToolStripMenuItem("删除");
            _miDelete.Click += delegate
            {
                if (_contextEntry != null) RemoveEntry(_contextEntry.Id);
            };
            _itemMenu.Items.Add(_miCopy);
            _itemMenu.Items.Add(new ToolStripSeparator());
            _itemMenu.Items.Add(_miPin);
            _itemMenu.Items.Add(_miDelete);
            _itemMenu.Opening += OnItemMenuOpening;
            _listBox.ContextMenuStrip = _itemMenu;
        }

        private void AddFilterItem(string label, string value)
        {
            var item = new ToolStripMenuItem(label);
            item.Tag = value;
            item.Click += delegate
            {
                _typeFilter = value;
                _filterButton.Text = label + " ▾";
                UpdateFilter();
            };
            _filterMenu.Items.Add(item);
        }

        private void ApplyLayout()
        {
            if (_titleBar == null) return;

            _titleBar.Width = ClientSize.Width;
            _settingsButton.Location = new Point(ClientSize.Width - 78, 10);
            _closeButton.Location = new Point(ClientSize.Width - 42, 10);

            _searchPanel.Left = 14;
            _searchPanel.Top = 56;
            _searchPanel.Width = Math.Max(200, ClientSize.Width - 28);

            int filterWidth = 76;
            _filterButton.Width = filterWidth;
            _filterButton.Height = 26;
            _filterButton.Left = _searchPanel.Width - filterWidth - 7;
            _filterButton.Top = 7;

            _searchIcon.Left = 9;
            _searchIcon.Top = 9;
            _searchBox.Left = 36;
            _searchBox.Top = 10;
            _searchBox.Width = Math.Max(80, _searchPanel.Width - 36 - filterWidth - 16);
            _searchBox.Height = 22;

            _listBox.Left = 10;
            _listBox.Top = 106;
            _listBox.Width = Math.Max(100, ClientSize.Width - 20);
            _listBox.Height = Math.Max(100, ClientSize.Height - 118);

            _emptyLabel.Bounds = new Rectangle(20, _listBox.Top,
                Math.Max(100, ClientSize.Width - 40), _listBox.Height);
            _emptyLabel.BringToFront();
        }

        public void SetEntries(List<ClipboardEntry> entries)
        {
            _allEntries = entries ?? new List<ClipboardEntry>();
            DisposeThumbnails();
            UpdateFilter();
        }

        public List<ClipboardEntry> GetAllEntries()
        {
            return _allEntries;
        }

        public void AddEntry(ClipboardEntry entry)
        {
            if (entry == null) return;
            _allEntries.Insert(0, entry);
            while (_allEntries.Count > Math.Max(1, MaxEntries))
            {
                var dropped = _allEntries[_allEntries.Count - 1];
                _allEntries.RemoveAt(_allEntries.Count - 1);
                RemoveThumbnail(dropped);
                if (EntryEvicted != null)
                    EntryEvicted(dropped);
            }
            UpdateFilter();
        }

        public void MoveToTop(string entryId)
        {
            int idx = _allEntries.FindIndex(e => e.Id == entryId);
            if (idx >= 0)
            {
                var entry = _allEntries[idx];
                // Re-copying an old item should bring it back to the top of the
                // timestamp-ordered list, not just reorder a list that is re-sorted.
                entry.Timestamp = DateTime.Now;
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
            DisposeThumbnails();
            UpdateFilter();
        }

        public void RemoveEntry(string entryId)
        {
            int idx = _allEntries.FindIndex(e => e.Id == entryId);
            if (idx < 0) return;

            var entry = _allEntries[idx];
            _allEntries.RemoveAt(idx);
            RemoveThumbnail(entry);
            UpdateFilter();

            if (EntryRemoved != null) EntryRemoved(entry);
        }

        public void TogglePin(string entryId)
        {
            var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            entry.IsPinned = !entry.IsPinned;
            UpdateFilter();
            if (HistoryChanged != null) HistoryChanged();
        }

        public void DeleteSelected()
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _filteredEntries.Count)
                RemoveEntry(_filteredEntries[_listBox.SelectedIndex].Id);
        }

        private void UpdateFilter()
        {
            string query = (_searchBox.Text ?? "").Trim();
            IEnumerable<ClipboardEntry> matches = _allEntries.Where(MatchesType);

            if (query.Length > 0)
                matches = matches.Where(e => BuildHaystack(e)
                    .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            string selectedId = null;
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _filteredEntries.Count)
                selectedId = _filteredEntries[_listBox.SelectedIndex].Id;

            _filteredEntries = matches
                .OrderByDescending(e => e.IsPinned)
                .ThenByDescending(e => e.Timestamp)
                .ToList();

            _listBox.BeginUpdate();
            _listBox.Items.Clear();
            foreach (var entry in _filteredEntries)
                _listBox.Items.Add(entry);
            _listBox.EndUpdate();

            _listBox.SelectedIndex = -1;
            if (selectedId != null)
            {
                for (int i = 0; i < _filteredEntries.Count; i++)
                {
                    if (_filteredEntries[i].Id == selectedId)
                    {
                        _listBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            _hoveredIndex = -1;
            UpdateEmptyState(query);

            if (_listBox.Items.Count > 0 && _listBox.SelectedIndex < 0)
                _listBox.SelectedIndex = 0;

            _listBox.Invalidate();
        }

        private bool MatchesType(ClipboardEntry entry)
        {
            switch (_typeFilter)
            {
                case "text": return entry.Type == "text";
                case "image": return entry.Type == "image";
                case "filelist": return entry.Type == "filelist";
                case "richtext": return entry.Type == "richtext" || entry.Type == "html";
                default: return true;
            }
        }

        private static string BuildHaystack(ClipboardEntry entry)
        {
            string text = entry.Text ?? "";
            if (text.Length > 4096) text = text.Substring(0, 4096);
            string rtf = entry.RtfPreview ?? "";
            if (rtf.Length > 4096) rtf = rtf.Substring(0, 4096);
            string paths = (entry.FilePathsRaw ?? "").Replace('\0', ' ');
            if (paths.Length > 4096) paths = paths.Substring(0, 4096);
            return text + "\n"
                + rtf + "\n"
                + paths + "\n"
                + entry.TypeDisplay + "\n"
                + entry.PreviewText;
        }

        private void UpdateEmptyState(string query)
        {
            _emptyLabel.Visible = _filteredEntries.Count == 0;
            if (query.Length > 0)
                _emptyLabel.Text = "没有找到匹配的记录";
            else if (_typeFilter != "all")
                _emptyLabel.Text = "此类型暂时没有记录";
            else
                _emptyLabel.Text = "还没有剪贴板记录\r\n复制任意内容后会自动出现在这里";
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
            else if (e.Control && e.KeyCode == Keys.P && _listBox.SelectedIndex >= 0)
            {
                var entry = _filteredEntries[_listBox.SelectedIndex];
                TogglePin(entry.Id);
                e.SuppressKeyPress = true;
            }
        }

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx >= 0)
                _listBox.SelectedIndex = idx;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx != _hoveredIndex)
            {
                int oldIdx = _hoveredIndex;
                _hoveredIndex = idx;
                if (oldIdx >= 0 && oldIdx < _listBox.Items.Count)
                    _listBox.Invalidate(_listBox.GetItemRectangle(oldIdx));
                if (idx >= 0 && idx < _listBox.Items.Count)
                    _listBox.Invalidate(_listBox.GetItemRectangle(idx));
            }

            _listBox.Cursor = idx >= 0
                && (GetCopyButtonRect(idx).Contains(e.Location)
                    || GetDeleteButtonRect(idx).Contains(e.Location))
                ? Cursors.Hand : Cursors.Default;
        }

        private void OnListMouseLeave(object sender, EventArgs e)
        {
            int oldIdx = _hoveredIndex;
            _hoveredIndex = -1;
            if (oldIdx >= 0 && oldIdx < _listBox.Items.Count)
                _listBox.Invalidate(_listBox.GetItemRectangle(oldIdx));
        }

        private void OnListMouseWheel(object sender, MouseEventArgs e)
        {
            // MouseWheel fires before the native scroll, so a recomputed index here is
            // stale. Clear it and let the next MouseMove pick the new row.
            _hoveredIndex = -1;
            _listBox.Cursor = Cursors.Default;
            _listBox.Invalidate();
        }

        private void OnListMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _listBox.Items.Count) return;

            // IndexFromPoint clamps to the nearest row inside the client area, so a click
            // in the blank space below the last row must not copy that row.
            Rectangle itemRect = _listBox.GetItemRectangle(idx);
            if (!itemRect.Contains(e.Location)) return;

            bool actionsVisible = idx == _hoveredIndex;
            if (actionsVisible && GetDeleteButtonRect(idx).Contains(e.Location))
            {
                RemoveEntry(((ClipboardEntry)_listBox.Items[idx]).Id);
                return;
            }

            // Copy button and the row itself both copy. A single click is the whole
            // interaction; the old build required a double click.
            SelectItem(idx);
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;
            SelectItem(_filteredEntries[index]);
        }

        private void SelectItem(ClipboardEntry entry)
        {
            if (entry == null) return;
            if (EntryCopied != null) EntryCopied(entry);
            // Keep the history window open so the user can copy several entries,
            // pin/delete items or drag the window without it disappearing.
        }

        private void OnTitleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            Point screen = PointToScreen(e.Location);
            _dragOffset = new Point(screen.X - Left, screen.Y - Top);
        }

        private void OnTitleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
        }

        private void OnTitleMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void OnItemMenuOpening(object sender, CancelEventArgs e)
        {
            int idx = _listBox.IndexFromPoint(_listBox.PointToClient(Cursor.Position));
            if (idx < 0 || idx >= _listBox.Items.Count)
            {
                e.Cancel = true;
                return;
            }

            _listBox.SelectedIndex = idx;
            _contextEntry = (ClipboardEntry)_listBox.Items[idx];
            _miPin.Text = _contextEntry.IsPinned ? "取消置顶" : "置顶";
        }

        private void ShowFilterMenu()
        {
            foreach (ToolStripItem item in _filterMenu.Items)
                ((ToolStripMenuItem)item).Checked = string.Equals((string)item.Tag, _typeFilter);
            _filterMenu.Show(_filterButton, new Point(0, _filterButton.Height));
        }

        private void OnMeasureItem(object sender, MeasureItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;
            var entry = (ClipboardEntry)_listBox.Items[e.Index];
            e.ItemHeight = (entry.Type == "image" && !string.IsNullOrEmpty(entry.ImageFile)) ? 78 : 66;
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;

            var entry = (ClipboardEntry)_listBox.Items[e.Index];
            var g = e.Graphics;
            var row = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (selected)
            {
                using (var path = UiTheme.RoundedRect(new Rectangle(row.X + 4, row.Y + 3,
                    Math.Max(1, row.Width - 8), Math.Max(1, row.Height - 6)), 8))
                using (var brush = new SolidBrush(UiTheme.AccentSoft))
                {
                    g.FillPath(brush, path);
                }
                using (var bar = new SolidBrush(UiTheme.AccentBar))
                {
                    g.FillRectangle(bar, row.X + 4, row.Y + 17, 3, Math.Max(1, row.Height - 34));
                }
            }
            else if (e.Index == _hoveredIndex)
            {
                using (var path = UiTheme.RoundedRect(new Rectangle(row.X + 4, row.Y + 3,
                    Math.Max(1, row.Width - 8), Math.Max(1, row.Height - 6)), 8))
                using (var brush = new SolidBrush(Color.FromArgb(250, 251, 253)))
                {
                    g.FillPath(brush, path);
                }
            }

            using (var pen = new Pen(UiTheme.BorderSoft))
            {
                g.DrawLine(pen, row.X + 14, row.Bottom - 1, row.Right - 14, row.Bottom - 1);
            }

            int contentX = row.X + 14;
            if (entry.Type == "image" && !string.IsNullOrEmpty(entry.ImageFile))
            {
                var thumbRect = new Rectangle(row.X + 12, row.Y + (row.Height - 50) / 2, 54, 50);
                DrawThumbnail(g, entry, thumbRect);
                contentX = thumbRect.Right + 12;
            }

            int right = row.Right - 14;
            int maxWidth = Math.Max(40, right - contentX - 76);
            DrawPreview(g, entry.PreviewText, new Rectangle(contentX, row.Y + 11, maxWidth, 21));

            if (e.Index == _hoveredIndex)
            {
                DrawActionButtons(g, e.Index);
            }
            else
            {
                TextRenderer.DrawText(g, entry.TimeDisplay, _metaFont,
                    new Rectangle(right - 78, row.Y + 13, 78, 18), UiTheme.TextMuted,
                    TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            int badgeTop = row.Bottom - 31;
            int badgeX = contentX;
            badgeX += DrawBadge(g, _badgeFont, entry.TypeDisplay, badgeX, badgeTop,
                Color.FromArgb(243, 245, 248), UiTheme.TextSecondary);
            if (entry.IsPinned)
            {
                badgeX += 6;
                DrawBadge(g, _badgeFont, "置顶", badgeX, badgeTop, UiTheme.WarningBg, UiTheme.WarningText);
            }
        }

        private void DrawThumbnail(Graphics g, ClipboardEntry entry, Rectangle target)
        {
            Image image;
            if (_thumbnails.TryGetValue(entry.ImageFile, out image) && image != null)
            {
                float scale = Math.Min((float)(target.Width - 2) / image.Width,
                    (float)(target.Height - 2) / image.Height);
                int width = Math.Max(1, (int)(image.Width * scale));
                int height = Math.Max(1, (int)(image.Height * scale));
                var dest = new Rectangle(target.X + (target.Width - width) / 2,
                    target.Y + (target.Height - height) / 2, width, height);

                using (var path = UiTheme.RoundedRect(target, 6))
                {
                    GraphicsState state = g.Save();
                    try
                    {
                        g.SetClip(path);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(image, dest);
                    }
                    finally
                    {
                        g.Restore(state);
                    }
                    using (var pen = new Pen(UiTheme.Border))
                        g.DrawPath(pen, path);
                }
            }
            else
            {
                using (var path = UiTheme.RoundedRect(target, 6))
                using (var fill = new SolidBrush(Color.FromArgb(244, 246, 249)))
                using (var pen = new Pen(UiTheme.Border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, "图", _metaFont, target, UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                EnsureThumbnail(entry);
            }
        }

        private void EnsureThumbnail(ClipboardEntry entry)
        {
            if (_thumbCache == null || _storage == null || entry == null) return;
            string key = entry.ImageFile;
            if (string.IsNullOrEmpty(key)) return;
            if (_thumbnails.ContainsKey(key) || _thumbRequests.Contains(key)) return;

            _thumbRequests.Add(key);
            _thumbCache.GetThumbnail(key, _storage, this, delegate(string k, Image image)
            {
                if (IsDisposed)
                {
                    if (image != null) image.Dispose();
                    return;
                }
                if (image == null) return;

                Image old;
                if (_thumbnails.TryGetValue(k, out old)) old.Dispose();
                _thumbnails[k] = image;
                if (_listBox != null && !_listBox.IsDisposed)
                    _listBox.Invalidate();
            });
        }

        private void DrawActionButtons(Graphics g, int index)
        {
            var copyRect = GetCopyButtonRect(index);
            var deleteRect = GetDeleteButtonRect(index);
            DrawActionButtonBackground(g, copyRect);
            DrawActionButtonBackground(g, deleteRect);

            using (var pen = new Pen(UiTheme.Accent, 1.3F))
            {
                g.DrawRectangle(pen, copyRect.X + 9, copyRect.Y + 8, 8, 9);
                g.DrawRectangle(pen, copyRect.X + 7, copyRect.Y + 10, 8, 9);
            }

            using (var pen = new Pen(UiTheme.Danger, 1.3F))
            {
                g.DrawLine(pen, deleteRect.X + 10, deleteRect.Y + 8, deleteRect.X + 18, deleteRect.Y + 16);
                g.DrawLine(pen, deleteRect.X + 18, deleteRect.Y + 8, deleteRect.X + 10, deleteRect.Y + 16);
            }
        }

        private static void DrawActionButtonBackground(Graphics g, Rectangle rect)
        {
            using (var path = UiTheme.RoundedRect(rect, 6))
            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(UiTheme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private Rectangle GetCopyButtonRect(int index)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            var bounds = _listBox.GetItemRectangle(index);
            return new Rectangle(bounds.Right - 70, bounds.Y + 9, 28, 26);
        }

        private Rectangle GetDeleteButtonRect(int index)
        {
            if (index < 0 || index >= _listBox.Items.Count) return Rectangle.Empty;
            var bounds = _listBox.GetItemRectangle(index);
            return new Rectangle(bounds.Right - 38, bounds.Y + 9, 28, 26);
        }

        private void DrawPreview(Graphics g, string text, Rectangle area)
        {
            string query = (_searchBox.Text ?? "").Trim();
            string display = text ?? "";
            display = display.Replace("\r", " ").Replace("\n", " ").Trim();
            if (display.Length == 0) display = "（空）";

            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding;

            int measured = TextRenderer.MeasureText(g, display, _previewFont,
                new Size(int.MaxValue, int.MaxValue), flags).Width;
            if (measured > area.Width)
            {
                int low = 0;
                int high = display.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    string candidate = display.Substring(0, mid) + "…";
                    int width = TextRenderer.MeasureText(g, candidate, _previewFont,
                        new Size(int.MaxValue, int.MaxValue), flags).Width;
                    if (width <= area.Width) low = mid;
                    else high = mid - 1;
                }
                display = display.Substring(0, low) + "…";
            }

            int match = query.Length == 0 ? -1
                : display.IndexOf(query, StringComparison.OrdinalIgnoreCase);

            GraphicsState previewState = g.Save();
            try
            {
                g.SetClip(area);
                if (match < 0)
                {
                    TextRenderer.DrawText(g, display, _previewFont, area.Location,
                        UiTheme.TextPrimary, flags);
                }
                else
                {
                    string prefix = display.Substring(0, match);
                    string hit = display.Substring(match, query.Length);
                    string suffix = display.Substring(match + query.Length);
                    int x = area.X;

                    if (prefix.Length > 0)
                    {
                        TextRenderer.DrawText(g, prefix, _previewFont, new Point(x, area.Y),
                            UiTheme.TextPrimary, flags);
                        x += TextRenderer.MeasureText(g, prefix, _previewFont,
                            new Size(int.MaxValue, int.MaxValue), flags).Width;
                    }

                    int hitWidth = TextRenderer.MeasureText(g, hit, _previewFont,
                        new Size(int.MaxValue, int.MaxValue), flags).Width;
                    using (var brush = new SolidBrush(UiTheme.Highlight))
                    {
                        g.FillRectangle(brush, x - 1, area.Y - 1, hitWidth + 2, area.Height);
                    }
                    TextRenderer.DrawText(g, hit, _previewFont, new Point(x, area.Y),
                        Color.FromArgb(11, 74, 158), flags);
                    x += hitWidth;

                    if (suffix.Length > 0)
                    {
                        TextRenderer.DrawText(g, suffix, _previewFont, new Point(x, area.Y),
                            UiTheme.TextPrimary, flags);
                    }
                }
            }
            finally
            {
                g.Restore(previewState);
            }
        }

        private static int DrawBadge(Graphics g, Font font, string text, int x, int y, Color fill, Color textColor)
        {
            int width = TextRenderer.MeasureText(g, text, font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 12;
            var rect = new Rectangle(x, y, width, 18);

            using (var path = UiTheme.RoundedRect(rect, 4))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            TextRenderer.DrawText(g, text, font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return width;
        }

        private void RemoveThumbnail(ClipboardEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ImageFile)) return;
            Image image;
            if (_thumbnails.TryGetValue(entry.ImageFile, out image))
            {
                try { image.Dispose(); } catch { }
                _thumbnails.Remove(entry.ImageFile);
            }
            _thumbRequests.Remove(entry.ImageFile);
        }

        private void DisposeThumbnails()
        {
            foreach (var image in _thumbnails.Values)
            {
                try { image.Dispose(); } catch { }
            }
            _thumbnails.Clear();
            _thumbRequests.Clear();
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
                DisposeThumbnails();
                if (_toolTip != null) _toolTip.Dispose();
                if (_filterMenu != null) _filterMenu.Dispose();
                if (_itemMenu != null) _itemMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
