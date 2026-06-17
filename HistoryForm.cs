using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipLite
{
    public class HistoryForm : Form
    {
        private TextBox _searchBox;
        private ListBox _listBox;
        private Label _closeBtn;
        private List<ClipboardEntry> _allEntries = new List<ClipboardEntry>();
        private List<ClipboardEntry> _filteredEntries = new List<ClipboardEntry>();

        public event Action<string> ItemSelected;

        public HistoryForm()
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
            this.Text = "ClipLite";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Width = 420;
            this.Height = 500;
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
                Location = new Point(10, 44),
                Width = 400,
                Height = 30,
                Font = new Font("Segoe UI", 11),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            _searchBox.TextChanged += (s, e) => UpdateFilter();
            _searchBox.KeyDown += OnSearchKeyDown;

            _listBox = new ListBox
            {
                Location = new Point(10, 82),
                Width = 400,
                Height = 408,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawVariable,
                IntegralHeight = false,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(250, 250, 250)
            };
            _listBox.DrawItem += OnDrawItem;
            _listBox.MeasureItem += OnMeasureItem;
            _listBox.MouseDoubleClick += (s, e) => { var idx = _listBox.IndexFromPoint(e.Location); if (idx >= 0) SelectItem(idx); };
            _listBox.KeyDown += OnListKeyDown;
            _listBox.MouseMove += OnListMouseMove;
            _listBox.MouseLeave += (s, e) => _listBox.Invalidate();
            _listBox.SelectedIndexChanged += (s, e) => _listBox.Invalidate();

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
            if (_allEntries.Count > JsonStorage.MaxEntries)
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
            if (idx >= 0 && idx < _listBox.Items.Count && _listBox.SelectedIndex != idx)
            {
                _listBox.SelectedIndex = idx;
            }
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;
            var entry = _filteredEntries[index];
            if (ItemSelected != null) ItemSelected(entry.Text);
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

            using (var bgBrush = new SolidBrush(selected ? Color.FromArgb(200, 220, 240) : Color.FromArgb(250, 250, 250)))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            using (var sepPen = new Pen(Color.FromArgb(235, 235, 235)))
                g.DrawLine(sepPen, bounds.X + 8, bounds.Bottom - 1, bounds.Right - 8, bounds.Bottom - 1);

            int x = bounds.X + 10;
            int y = bounds.Y + 5;
            int maxW = bounds.Width - 20;

            if (entry.IsPinned)
            {
                using (var pinBrush = new SolidBrush(Color.FromArgb(255, 160, 0)))
                using (var pinFont = new Font("Segoe UI", 7, FontStyle.Bold))
                {
                    g.DrawString("置顶", pinFont, pinBrush, x, y);
                }
                y += 14;
            }

            string preview = entry.PreviewText;
            using (var textBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (var textFont = new Font("Segoe UI", 10))
            {
                var textRect = new Rectangle(x, y, maxW, 22);
                TextRenderer.DrawText(g, preview, textFont, textRect, Color.FromArgb(40, 40, 40),
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            using (var timeBrush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            using (var timeFont = new Font("Segoe UI", 8))
            {
                g.DrawString(entry.TimeDisplay, timeFont, timeBrush, x, bounds.Bottom - 18);
            }
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
}

