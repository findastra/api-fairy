using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApiFairy
{
    // "API Fairy.exe --snapshot <folder>" draws the windows with made-up keys into PNGs,
    // for the README and for checking the look without touching real keys.
    static class Snapshot
    {
        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = Theme.Load();
            FairyWindow.Still = true;
            var host = new DemoHost();

            var fairy = new FairyWindow();
            fairy.SetMood("curious");
            fairy.SetBadge(3);
            fairy.Say("New key! OPENAI_API_KEY (OpenAI) in your Windows settings. Tap me to add a note.", true);
            Save(Detach(fairy, null), FairyWindow.Wd, FairyWindow.Ht, Path.Combine(dir, "fairy.png"));

            foreach (var tab in new[] { "keys", "settings", "how" })
            {
                var dash = new DashboardWindow(host);
                dash.SelectTab(tab);
                if (tab == "keys") dash.SelectKey("file:c:\\users\\you\\documents\\projects\\paper-girl\\.env#STRIPE_SECRET_KEY");
                Save(Detach(dash, Theme.Shell), 1080, 760, Path.Combine(dir, "dashboard-" + tab + ".png"));
                dash.AllowClose = true;
                dash.Close();
            }
            return 0;
        }

        static FrameworkElement Detach(Window w, Brush background)
        {
            var content = (UIElement)w.Content;
            w.Content = null;
            var wrap = new Border { Background = background ?? Brushes.Transparent, Child = content };
            if (background != null) wrap.Padding = new Thickness(0);
            return wrap;
        }

        static void Save(FrameworkElement e, double w, double h, string path)
        {
            e.Measure(new Size(w, h));
            e.Arrange(new Rect(0, 0, w, h));
            e.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)(w * 1.5), (int)(h * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(e);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) png.Save(fs);
        }

        sealed class DemoHost : IFairyHost
        {
            readonly StoreData data = new StoreData();
            readonly Dictionary<string, List<string>> hits = new Dictionary<string, List<string>>();

            public DemoHost()
            {
                var now = DateTime.UtcNow;
                data.Pepper = Convert.ToBase64String(new byte[32]);
                data.Settings.Folders.Add(@"C:\Users\you\Documents\Projects");
                data.Settings.Welcomed = true;
                Add("user:OPENAI_API_KEY", "OPENAI_API_KEY", Scopes.User, "", "openai", "a1b2c3d4e5f60718", now.AddMinutes(-2), false, "image-bridge-desktop-2026-10", "image bridge", "$10/month", null);
                Add("user:ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY", Scopes.User, "", "anthropic", "0f1e2d3c4b5a6978", now.AddDays(-30), true, "presence-desktop-2026-04", "Claude scripts", "$20/month", "2026-03-20");
                var stripe = Add("file:c:\\users\\you\\documents\\projects\\paper-girl\\.env#STRIPE_SECRET_KEY", "STRIPE_SECRET_KEY", Scopes.File, @"C:\Users\you\Documents\Projects\paper-girl\.env", "stripe", "9988776655443322", now.AddDays(-3), true, null, "paper-girl shop", null, null);
                stripe.Git = GitStates.NotIgnored;
                var hf = Add("file:c:\\users\\you\\documents\\projects\\vrchat-ai-astra\\.env#HF_TOKEN", "HF_TOKEN", Scopes.File, @"C:\Users\you\Documents\Projects\vrchat-ai-astra\.env", "huggingface", "1122334455667788", now.AddDays(-12), true, "astra-voice-2026-09", "vrchat-ai-astra", null, null);
                hf.Git = GitStates.Ignored;
                var gone = Add("user:REPLICATE_API_TOKEN", "REPLICATE_API_TOKEN", Scopes.User, "", "replicate", "5566778899aabbcc", now.AddDays(-60), true, null, "old image tests", null, null);
                gone.Present = false;
                gone.GoneOn = Core.Iso(now.AddDays(-1));
                hits["a1b2c3d4e5f60718"] = new List<string> { "PowerShell history" };
            }

            KeyRecord Add(string id, string name, string scope, string location, string provider, string fp, DateTime first, bool seen, string label, string usedBy, string limit, string created)
            {
                var r = new KeyRecord { Id = id, Name = name, Scope = scope, Location = location, ProviderId = provider, Fingerprint = fp, FirstSeen = Core.Iso(first), LastChanged = Core.Iso(first), Present = true, Seen = seen, Label = label, UsedBy = usedBy, Limit = limit, CreatedOn = created };
                data.Keys.Add(r);
                return r;
            }

            public StoreData Data { get { return data; } }
            public IDictionary<string, List<string>> HistoryHits { get { return hits; } }
            public int HistoryKeyLines { get { return 1; } }
            public DateTime LastScanUtc { get { return DateTime.UtcNow.AddSeconds(-1); } }
            public string NotesFile { get { return @"C:\Users\you\AppData\Local\API Fairy\fairy.dat"; } }
            public string SteadyMood { get { return "worried"; } }
            public bool StartupOn { get { return true; } }
            public string Fingerprint(string text) { return "0000000000000000"; }
            public void MarkSeen(string id) { }
            public void MarkAllSeen() { }
            public string SaveNotes(string id, string label, string usedBy, string limit, string createdOn, string notes, string providerOverride) { return null; }
            public void SetIgnored(string id, bool ignored) { }
            public void Forget(string id) { }
            public void OpenManage(string id) { }
            public void RevealFile(string id) { }
            public void AddFolder() { }
            public void RemoveFolder(string path) { }
            public void SetMode(string mode) { }
            public void ResetPosition() { }
            public void SetStartup(bool on) { }
            public string SetRotateDays(string text) { return null; }
            public void CleanHistory() { }
            public void ScanNow() { }
            public void OpenEnvironmentEditor() { }
            public void Quit() { }
        }
    }
}
