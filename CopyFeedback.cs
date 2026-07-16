using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipLite
{
    internal sealed class CopyFeedbackController : IDisposable
    {
        private const string SuccessMessage = "已复制到剪贴板";

        private readonly CopyFeedbackForm _feedbackForm;
        private bool _successEnabled;
        private bool _disposed;

        public CopyFeedbackController(bool successEnabled)
        {
            _successEnabled = successEnabled;
            _feedbackForm = new CopyFeedbackForm();
        }

        public void SetSuccessEnabled(bool enabled)
        {
            if (_disposed) return;
            _successEnabled = enabled;
            if (!enabled) _feedbackForm.HideSuccess();
        }

        public void ShowSuccess(Screen screen)
        {
            if (_disposed || !_successEnabled) return;
            _feedbackForm.ShowMessage(SuccessMessage, false, ResolveScreen(screen));
        }

        public void ShowFailure(string message, Screen screen)
        {
            if (_disposed) return;
            string text = string.IsNullOrWhiteSpace(message)
                ? "复制失败，请重试。" : message;
            _feedbackForm.ShowMessage(text, true, ResolveScreen(screen));
        }

        private static Screen ResolveScreen(Screen screen)
        {
            if (screen != null) return screen;
            return Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _feedbackForm.Dispose();
        }

    }

    internal sealed class CopyFeedbackForm : Form
    {
        private const int SuccessWidth = 98;
        private const int FeedbackHeight = 28;
        private const int MinimumWidth = 98;
        private const int MaximumWidth = 360;
        private const int HorizontalPadding = 24;
        private const int BottomOffset = 48;
        private const int HideDelay = 1800;
        private const int BaseDpi = 96;

        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private readonly Label _messageLabel;
        private readonly Timer _hideTimer;
        private bool _showingSuccess;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
                return parameters;
            }
        }

        public CopyFeedbackForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(SuccessWidth, FeedbackHeight);
            BackColor = UiTheme.InverseSurface;

            _messageLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.MetaFont,
                ForeColor = UiTheme.InverseOnSurface,
                BackColor = UiTheme.InverseSurface,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _messageLabel.Click += (s, e) => HideMessage();
            Controls.Add(_messageLabel);

            _hideTimer = new Timer { Interval = HideDelay };
            _hideTimer.Tick += (s, e) => HideMessage();
        }

        public void ShowMessage(string message, bool isError, Screen screen)
        {
            _showingSuccess = !isError;
            PositionOnScreen(screen, BaseDpi);
            int dpi = ResolveDpi();
            ApplyAppearance(message, isError, dpi);
            PositionOnScreen(screen, dpi);
            NativeWindowEffects.ApplyRoundedRegion(this, Height / 2);

            if (!Visible) Show();
            SetWindowPos(Handle, HwndTopmost, Left, Top, Width, Height,
                SwpNoActivate | SwpShowWindow);

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        public void HideSuccess()
        {
            if (_showingSuccess) HideMessage();
        }

        private void ApplyAppearance(string message, bool isError, int dpi)
        {
            Color background = isError
                ? Color.FromArgb(132, 35, 40) : UiTheme.InverseSurface;
            int measuredWidth = TextRenderer.MeasureText(message, UiTheme.MetaFont,
                Size.Empty, TextFormatFlags.NoPadding).Width + Scale(HorizontalPadding, dpi);
            int targetWidth = isError
                ? Math.Max(Scale(MinimumWidth, dpi),
                    Math.Min(Scale(MaximumWidth, dpi), measuredWidth))
                : Scale(SuccessWidth, dpi);

            ClientSize = new Size(targetWidth, Scale(FeedbackHeight, dpi));
            BackColor = background;
            _messageLabel.BackColor = background;
            _messageLabel.Text = message;
        }

        private void PositionOnScreen(Screen screen, int dpi)
        {
            Rectangle area = screen.WorkingArea;
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Bottom - Height - Scale(BottomOffset, dpi);
        }

        private int ResolveDpi()
        {
            try
            {
                uint dpi = GetDpiForWindow(Handle);
                if (dpi > 0) return (int)dpi;
            }
            catch (EntryPointNotFoundException) { }

            using (Graphics graphics = Graphics.FromHwnd(Handle))
                return Math.Max(BaseDpi, (int)Math.Round(graphics.DpiX));
        }

        private static int Scale(int value, int dpi)
        {
            return Math.Max(1, (int)Math.Round(value * dpi / (double)BaseDpi,
                MidpointRounding.AwayFromZero));
        }

        private void HideMessage()
        {
            _hideTimer.Stop();
            if (Visible) Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hideTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
