using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace ApiFairy
{
    // API Fairy's approved art: the nine mood frames in art/ (sprite-20261008.json), built into the program by build.cmd.
    // The desktop pet itself is drawn by Startup Star; this app only uses the frames in its window and icons.
    static class Art
    {
        public const string Date = "20261008";
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        // The app's steady states, in the shared mood words (PET-FILES.md): happy = checked, nothing wrong;
        // worried = found problems; curious = something new to look at; alarmed = a key is at risk.
        public static string MoodFor(string state)
        {
            switch (state)
            {
                case "worried": return "alarmed";
                case "curious": return "curious";
                case "sleep": return "sleep";
                default: return "happy";
            }
        }

        static Stream Open(string mood)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(mood + "-" + Date + ".png");
        }

        public static BitmapSource Frame(string mood)
        {
            BitmapSource b;
            if (cache.TryGetValue(mood, out b)) return b;
            using (var s = Open(mood) ?? Open("idle"))
            {
                if (s == null) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = s;
                img.EndInit();
                img.Freeze();
                cache[mood] = img;
                return img;
            }
        }

        public static Bitmap Icon(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var s = Open("happy"))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (s == null) return bmp;
                using (var src = new Bitmap(s))
                {
                    g.InterpolationMode = size >= 64 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            return bmp;
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
