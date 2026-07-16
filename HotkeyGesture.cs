using System;
using System.Windows.Forms;

namespace ClipLite
{
    internal static class HotkeyGesture
    {
        public const uint Alt = 0x0001;
        public const uint Control = 0x0002;
        public const uint Shift = 0x0004;
        public const uint Win = 0x0008;
        public const uint NoRepeat = 0x4000;

        private const uint SupportedModifiers = Alt | Control | Shift | Win;

        public static bool IsDisabled(uint modifiers, uint keyCode)
        {
            return modifiers == 0 && keyCode == 0;
        }

        public static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                   key == Keys.LWin || key == Keys.RWin;
        }

        public static bool TryValidate(uint modifiers, uint keyCode, out string error)
        {
            error = null;
            if (IsDisabled(modifiers, keyCode)) return true;

            if (modifiers == 0 || keyCode == 0)
            {
                error = "快捷键必须同时包含修饰键和主键。";
                return false;
            }
            if ((modifiers & ~SupportedModifiers) != 0)
            {
                error = "快捷键包含不支持的修饰键。";
                return false;
            }

            Keys key = (Keys)keyCode;
            if (IsModifierKey(key))
            {
                error = "请再按一个字母、数字或功能键。";
                return false;
            }
            if (key == Keys.F12)
            {
                error = "F12 被 Windows 保留，请换一个主键。";
                return false;
            }
            if ((modifiers & Alt) != 0 && key == Keys.F4)
            {
                error = "Alt+F4 是系统关闭窗口快捷键，不能使用。";
                return false;
            }
            if ((modifiers & Control) != 0 && (modifiers & Alt) != 0 && key == Keys.Delete)
            {
                error = "Ctrl+Alt+Delete 是系统保留快捷键。";
                return false;
            }
            if ((modifiers & Control) != 0 && (modifiers & Shift) != 0 && key == Keys.Escape)
            {
                error = "Ctrl+Shift+Esc 是系统保留快捷键。";
                return false;
            }
            return true;
        }

        public static string Format(uint modifiers, uint keyCode)
        {
            if (IsDisabled(modifiers, keyCode)) return "无";

            string text = "";
            if ((modifiers & Control) != 0) text += "Ctrl+";
            if ((modifiers & Shift) != 0) text += "Shift+";
            if ((modifiers & Alt) != 0) text += "Alt+";
            if ((modifiers & Win) != 0) text += "Win+";
            return text + FormatKey((Keys)keyCode);
        }

        private static string FormatKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)key - (int)Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num " + ((int)key - (int)Keys.NumPad0);

            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "+";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                default: return key.ToString();
            }
        }
    }
}
