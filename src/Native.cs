using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ApiFairy
{
    static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetDllDirectory(string path);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        // The fairy never steals focus from what you're typing in, and stays out of Alt+Tab.
        public static void MakeOverlay(IntPtr hwnd)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public static TimeSpan IdleTime()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }

        // Windows 11: pink title bar with ink text. Older Windows ignores this.
        public static void TintTitleBar(IntPtr hwnd, System.Windows.Media.Color caption, System.Windows.Media.Color text)
        {
            try
            {
                int c = caption.R | caption.G << 8 | caption.B << 16, t = text.R | text.G << 8 | text.B << 16;
                DwmSetWindowAttribute(hwnd, 35, ref c, 4);
                DwmSetWindowAttribute(hwnd, 36, ref t, 4);
                DwmSetWindowAttribute(hwnd, 34, ref t, 4);
            }
            catch (Exception) { }
        }

        public static void FreeIcon(IntPtr h) { if (h != IntPtr.Zero) DestroyIcon(h); }
    }

    // Start with Windows: one value under the per-user Run key, written only when the person ticks the box.
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "API Fairy";

        static string Command() { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --startup"; }

        public static bool IsOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(ValueName) is string;
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(ValueName, Command(), RegistryValueKind.String);
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
            }
        }

        // If the app folder moved, point the existing entry at the new place.
        public static void Refresh()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    var v = k == null ? null : k.GetValue(ValueName) as string;
                    if (v != null && v != Command()) k.SetValue(ValueName, Command(), RegistryValueKind.String);
                }
            }
            catch (Exception) { }
        }
    }
}
