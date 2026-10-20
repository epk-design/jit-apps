using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TrayKanban
{
    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        public static bool IsDark
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && (k.GetValue("AppsUseLightTheme") as int?) == 0;
            }
        }

        static bool SupportsBackdrop { get { return Environment.OSVersion.Version.Build >= 22621; } }

        // Windows 11 look: rounded corners, dark title bar when needed, and Mica (or Acrylic for flyouts).
        public static void Style(Window w, bool acrylic)
        {
            w.SourceInitialized += delegate
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int dark = IsDark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                if (!SupportsBackdrop) return;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int type = acrylic ? 3 : 2; DwmSetWindowAttribute(hwnd, 38, ref type, 4);
                w.Background = Brushes.Transparent;
            };
            if (!SupportsBackdrop) w.SetResourceReference(Window.BackgroundProperty, "ApplicationBackgroundBrush");
        }
    }
}
