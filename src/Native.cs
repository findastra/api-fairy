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

    static class Pixels
    {
        public static BitmapSource ToBitmap(uint[] px, int w, int h)
        {
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            Write(bmp, px);
            return bmp;
        }

        public static void Write(WriteableBitmap bmp, uint[] px)
        {
            var ints = new int[px.Length];
            for (int i = 0; i < px.Length; i++) ints[i] = unchecked((int)px[i]);
            bmp.WritePixels(new Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), ints, bmp.PixelWidth * 4, 0);
        }

        // The app and tray icon: a pink handheld with the fairy on its LCD.
        public static Bitmap Icon(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                float stroke = Math.Max(1f, size / 16f), r = size * 0.22f;
                var shell = new RectangleF(stroke / 2, stroke / 2, size - stroke, size - stroke);
                using (var path = Rounded(shell, r))
                using (var fill = new SolidBrush(System.Drawing.Color.FromArgb(unchecked((int)0xFFFFA9DA))))
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(unchecked((int)0xFF1E0A19)), stroke))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                float inset = size * 0.14f;
                var lcd = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
                using (var path = Rounded(lcd, r * 0.6f))
                using (var fill = new SolidBrush(System.Drawing.Color.FromArgb(unchecked((int)Sprite.Bg))))
                    g.FillPath(fill, path);
                var frame = new Sprite.Frame();
                var px = Sprite.Render(frame);
                // crop to the fairy (columns 3..28, rows 0..19) and scale with hard pixel edges
                int cx = 3, cy = 0, cw = 26, ch = 20;
                float scale = Math.Min(lcd.Width / cw, lcd.Height / ch);
                if (scale >= 1) scale = (float)Math.Floor(scale);
                float ox = lcd.X + (lcd.Width - cw * scale) / 2, oy = lcd.Y + (lcd.Height - ch * scale) / 2;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        uint c = px[(cy + y) * Sprite.W + cx + x];
                        if (c == Sprite.Bg) continue;
                        using (var b = new SolidBrush(System.Drawing.Color.FromArgb(unchecked((int)c))))
                            g.FillRectangle(b, ox + x * scale, oy + y * scale, scale, scale);
                    }
            }
            return bmp;
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static BitmapSource IconSource(int size)
        {
            using (var bmp = Icon(size))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        // Writes a multi-size .ico (PNG entries) for the exe.
        public static void WriteIco(string path)
        {
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (var s in sizes)
                using (var bmp = Icon(s))
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }
}
