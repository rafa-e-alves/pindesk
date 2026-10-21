using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace PinDesk
{
    internal static class WinUtil
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        // Partes do próprio Windows que não faz sentido fixar.
        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame",
            "Windows.UI.Core.CoreWindow"
        };

        public static string GetTitle(IntPtr h)
        {
            int len = Native.GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static uint GetPid(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        public static bool IsOwn(IntPtr h)
        {
            return GetPid(h) == OwnPid;
        }

        static int GetExStyle(IntPtr h)
        {
            return (int)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        }

        public static bool IsTopmost(IntPtr h)
        {
            return (GetExStyle(h) & Native.WS_EX_TOPMOST) != 0;
        }

        public static bool SetTopmost(IntPtr h, bool topmost)
        {
            Native.SetWindowPos(h, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            return IsTopmost(h) == topmost;
        }

        public static bool IsCloaked(IntPtr h)
        {
            int cloaked;
            return Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0;
        }

        // Retângulo visível da janela (sem as bordas invisíveis do Windows 10/11).
        public static Rectangle GetFrame(IntPtr h)
        {
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0)
                Native.GetWindowRect(h, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static float GetScale(IntPtr h)
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(h);
                if (dpi > 0) return dpi / 96f;
            }
            catch (EntryPointNotFoundException) { }
            return 1f;
        }

        public static bool IsPinnable(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h)) return false;
            if (IsOwn(h)) return false;
            return !ShellClasses.Contains(GetClassName(h));
        }

        // Janelas "de verdade", as mesmas que aparecem no Alt+Tab.
        static bool IsAppWindow(IntPtr h)
        {
            if (!IsPinnable(h) || IsCloaked(h)) return false;
            if (GetTitle(h).Trim().Length == 0) return false;
            int ex = GetExStyle(h);
            bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero && !appWindow) return false;
            return true;
        }

        public static List<IntPtr> ListAppWindows()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) =>
            {
                if (IsAppWindow(h)) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Janela de cima que está embaixo do ponto, ignorando as do próprio PinDesk.
        public static IntPtr WindowAt(Point pt)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || IsOwn(h) || IsCloaked(h)) return true;
                if ((GetExStyle(h) & Native.WS_EX_TRANSPARENT) != 0) return true;
                Rectangle r = GetFrame(h);
                if (r.Width <= 0 || r.Height <= 0 || !r.Contains(pt)) return true;
                if (ShellClasses.Contains(GetClassName(h))) return false;
                if (GetTitle(h).Length == 0) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return IsPinnable(found) ? found : IntPtr.Zero;
        }

        public static Bitmap GetIconBitmap(IntPtr h, int size)
        {
            IntPtr hIcon = QueryIcon(h, Native.ICON_SMALL2);
            if (hIcon == IntPtr.Zero) hIcon = QueryIcon(h, Native.ICON_SMALL);
            if (hIcon == IntPtr.Zero) hIcon = QueryIcon(h, Native.ICON_BIG);
            if (hIcon == IntPtr.Zero) hIcon = Native.GetClassLongPtr(h, Native.GCLP_HICONSM);
            if (hIcon == IntPtr.Zero) hIcon = Native.GetClassLongPtr(h, Native.GCLP_HICON);
            if (hIcon == IntPtr.Zero) return null;

            try
            {
                using (Icon icon = Icon.FromHandle(hIcon))
                using (Bitmap src = icon.ToBitmap())
                {
                    var bmp = new Bitmap(size, size);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, size, size);
                    }
                    return bmp;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static IntPtr QueryIcon(IntPtr h, int type)
        {
            IntPtr result;
            if (Native.SendMessageTimeout(h, Native.WM_GETICON, new IntPtr(type), IntPtr.Zero,
                    Native.SMTO_ABORTIFHUNG, 100, out result) == IntPtr.Zero)
                return IntPtr.Zero;
            return result;
        }
    }
}
