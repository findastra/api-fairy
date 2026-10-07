using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("API Fairy")]
[assembly: AssemblyProduct("API Fairy")]
[assembly: AssemblyCompany("findastra")]
[assembly: AssemblyCopyright("Copyright (c) 2026 findastra. MIT License.")]
[assembly: AssemblyDescription("A desktop fairy that keeps notes about your API keys, never the keys.")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace ApiFairy
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Don't load DLLs from the current folder (DLL planting).
            Native.SetDllDirectory("");

            int i = Array.IndexOf(args, "--export-icon");
            if (i >= 0 && i + 1 < args.Length) { Pixels.WriteIco(args[i + 1]); return 0; }
            i = Array.IndexOf(args, "--snapshot");
            if (i >= 0 && i + 1 < args.Length) return Snapshot.Run(args[i + 1]);

            var sid = WindowsIdentity.GetCurrent().User.Value;
            bool first;
            using (var mutex = new Mutex(true, @"Local\ApiFairy-" + sid, out first))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ApiFairy-Show-" + sid))
            {
                if (!first)
                {
                    // Already running: ask that copy to open its window instead.
                    Native.AllowSetForegroundWindow(-1);
                    show.Set();
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources = Theme.Load();
                Controller controller = null;
                app.DispatcherUnhandledException += (s, e) => { Log.Error(e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error(e.ExceptionObject as Exception);
                app.Startup += (s, e) =>
                {
                    controller = new Controller(app, args.Contains("--startup"));
                    controller.Start();
                    var waiter = new Thread(() =>
                    {
                        while (true)
                        {
                            show.WaitOne();
                            if (controller.Stopping) return;
                            app.Dispatcher.BeginInvoke(new Action(controller.ShowDashboard));
                        }
                    });
                    waiter.IsBackground = true;
                    waiter.Start();
                };
                app.Run();
                GC.KeepAlive(mutex);
                return 0;
            }
        }
    }

    // Errors go to a small local log. Anything that looks like a key is blanked out first.
    static class Log
    {
        public static void Error(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var path = Path.Combine(Store.DefaultDir(), "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                var text = ex.ToString();
                foreach (var t in Providers.Tokens(text).Where(Providers.LooksLikeRealKey).Distinct().ToList()) text = text.Replace(t, "[hidden]");
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + text + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    sealed class Controller : IFairyHost
    {
        readonly Application app;
        readonly bool launchedAtSignIn;
        readonly Store store;
        readonly Scanner scanner;
        readonly FairyWindow fairy = new FairyWindow();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object rootsLock = new object();
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly DispatcherTimer hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        DashboardWindow dash;
        System.Windows.Forms.NotifyIcon tray;
        IntPtr trayIcon;
        List<string> roots = new List<string>();
        volatile bool stopping, walkWanted = true, gitWanted;
        IDictionary<string, List<string>> hits = new Dictionary<string, List<string>>();
        int historyLines;
        string hitsSignature = "";
        string mood = "happy";
        DateTime lastScan = DateTime.MinValue;
        bool tucked;

        public Controller(Application app, bool launchedAtSignIn)
        {
            this.app = app;
            this.launchedAtSignIn = launchedAtSignIn;
            store = Store.Load(Store.DefaultDir());
            scanner = new Scanner(store.Data.PepperBytes());
        }

        public bool Stopping { get { return stopping; } }

        public void Start()
        {
            var st = store.Data.Settings;
            if (st.FairyPlaced)
            {
                fairy.Left = st.FairyLeft;
                fairy.Top = st.FairyTop;
                fairy.ClampToScreen();
            }
            else fairy.PlaceDefault();
            fairy.OpenRequested += (s, e) => ShowDashboard();
            fairy.HideRequested += (s, e) => { tucked = true; fairy.HideBubble(); fairy.Hide(); };
            fairy.QuitRequested += (s, e) => Quit();
            fairy.Moved += (s, e) => { st.FairyLeft = fairy.Left; st.FairyTop = fairy.Top; st.FairyPlaced = true; Save(); };
            fairy.BubbleClosed += (s, e) => { if (ShouldHide()) hideTimer.Start(); };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); if (ShouldHide() && !fairy.BubbleVisible) fairy.Hide(); };
            if (st.FairyMode == Modes.Perch) fairy.Show();
            SetupTray();
            Startup.Refresh();
            if (store.LoadProblem != null) Say(store.LoadProblem, true);
            ApplyFolders();
            var worker = new Thread(Work) { IsBackground = true, Name = "API Fairy scanner", Priority = ThreadPriority.BelowNormal };
            worker.Start();
            if (!launchedAtSignIn && st.Welcomed) ShowDashboard();
        }

        bool ShouldHide() { return tucked || store.Data.Settings.FairyMode == Modes.PopUp; }

        // ---- background checks ----

        void Work()
        {
            var lastWalk = DateTime.MinValue;
            while (!stopping)
            {
                bool walk = walkWanted || DateTime.UtcNow - lastWalk > TimeSpan.FromSeconds(60);
                bool git = gitWanted;
                walkWanted = false;
                gitWanted = false;
                List<string> r;
                lock (rootsLock) r = new List<string>(roots);
                try
                {
                    var result = scanner.Scan(r, walk, git);
                    if (walk) lastWalk = DateTime.UtcNow;
                    app.Dispatcher.BeginInvoke(new Action(() => Apply(result)));
                }
                catch (Exception ex) { Log.Error(ex); }
                wake.WaitOne(2000);
            }
        }

        void Apply(ScanResult result)
        {
            if (stopping) return;
            var now = DateTime.UtcNow;
            lastScan = result.At;
            int linesBefore = historyLines;
            bool firstScan = hitsSignature.Length == 0;
            hits = result.HistoryHits;
            historyLines = result.HistoryKeyLines;
            var signature = "#" + historyLines + ":" + string.Join(",", hits.Keys.OrderBy(k => k));
            bool hitsChanged = signature != hitsSignature;
            hitsSignature = signature;

            bool changed;
            var events = Core.Reconcile(store.Data, result, now, out changed);
            var st = store.Data.Settings;
            if (!st.Welcomed)
            {
                st.Welcomed = true;
                changed = true;
                Say(Core.Welcome(store.Data.Keys.Count(k => k.Present && !k.Ignored)), true);
            }
            else if (events.Count > 0)
            {
                Say(Core.Bubble(events, store.Data, hits, now), events.Any(e => e.Kind != EventKinds.Gone));
            }
            else if (!firstScan && historyLines > linesBefore)
            {
                Say("Careful! A key just got saved in your shell history. Tap me and use Clean history.", true);
            }
            if (changed) Save();
            if (changed || hitsChanged) Changed(false);
        }

        void ApplyFolders()
        {
            var folders = store.Data.Settings.Folders.ToList();
            lock (rootsLock) roots = folders;
            foreach (var w in watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
            watchers.Clear();
            foreach (var f in folders)
            {
                try
                {
                    if (!Directory.Exists(f)) continue;
                    var w = new FileSystemWatcher(f) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
                    w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                    FileSystemEventHandler poke = (s, e) => { if (Dotenv.IsDotenvFileName(Path.GetFileName(e.FullPath))) { walkWanted = true; wake.Set(); } };
                    w.Created += poke;
                    w.Changed += poke;
                    w.Deleted += poke;
                    w.Renamed += (s, e) => { walkWanted = true; wake.Set(); };
                    w.Error += (s, e) => { walkWanted = true; wake.Set(); };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex) { Log.Error(ex); }
            }
            walkWanted = true;
            wake.Set();
        }

        // ---- the fairy and the tray ----

        void Say(string text, bool sticky)
        {
            if (string.IsNullOrEmpty(text)) return;
            hideTimer.Stop();
            tucked = false;
            if (!fairy.IsVisible) fairy.Show();
            fairy.Say(text, sticky);
        }

        void Changed(bool save)
        {
            if (save) Save();
            var d = store.Data;
            var now = DateTime.UtcNow;
            mood = Core.Mood(d, hits, historyLines, now);
            int needs = d.Keys.Count(r => Core.NeedsAttention(r, d, hits, now)) + (historyLines > 0 ? 1 : 0);
            fairy.SetMood(mood);
            fairy.SetBadge(needs);
            if (tray != null)
            {
                int active = d.Keys.Count(r => r.Present && !r.Ignored);
                var tip = "API Fairy · " + active + (active == 1 ? " key" : " keys") + (needs > 0 ? " · " + needs + " need you" : "");
                tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }
            if (dash != null) dash.Refresh();
        }

        void Save()
        {
            try { store.Save(); }
            catch (Exception ex)
            {
                Log.Error(ex);
                Say("I couldn't save my notes just now. I'll try again on the next change.", false);
            }
        }

        void SetupTray()
        {
            using (var bmp = Pixels.Icon(32)) trayIcon = bmp.GetHicon();
            tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.FromHandle(trayIcon), Text = "API Fairy", Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open API Fairy", null, (s, e) => ShowDashboard());
            menu.Items.Add("Show the fairy", null, (s, e) => { tucked = false; fairy.Show(); });
            menu.Items.Add("Check now", null, (s, e) => ScanNow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => Quit());
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowDashboard(); };
        }

        public void ShowDashboard()
        {
            if (stopping) return;
            if (dash == null) dash = new DashboardWindow(this);
            dash.Refresh();
            if (!dash.IsVisible) dash.Show();
            if (dash.WindowState == WindowState.Minimized) dash.WindowState = WindowState.Normal;
            dash.Activate();
            fairy.HideBubble();
            if (ShouldHide()) hideTimer.Start();
        }

        // ---- IFairyHost ----

        public StoreData Data { get { return store.Data; } }
        public IDictionary<string, List<string>> HistoryHits { get { return hits; } }
        public int HistoryKeyLines { get { return historyLines; } }
        public DateTime LastScanUtc { get { return lastScan; } }
        public string NotesFile { get { return store.FilePath; } }
        public string SteadyMood { get { return mood; } }
        public bool StartupOn { get { try { return Startup.IsOn(); } catch (Exception) { return false; } } }
        public string Fingerprint(string text) { return scanner.Fingerprint(text); }

        public void MarkSeen(string id)
        {
            var r = store.Data.Find(id);
            if (r == null || r.Seen) return;
            r.Seen = true;
            Changed(true);
        }

        public void MarkAllSeen()
        {
            foreach (var r in store.Data.Keys) r.Seen = true;
            Changed(true);
        }

        public string SaveNotes(string id, string label, string usedBy, string limit, string createdOn, string notes, string providerOverride)
        {
            var r = store.Data.Find(id);
            if (r == null) return "That key isn't in my notes anymore.";
            label = Core.CleanText(label, false, 80);
            usedBy = Core.CleanText(usedBy, false, 200);
            limit = Core.CleanText(limit, false, 60);
            createdOn = Core.CleanText(createdOn, false, 10);
            notes = Core.CleanText(notes, true, 2000);
            var error = Core.ValidateNotes(store.Data, scanner.Fingerprint, createdOn, label, usedBy, limit, notes);
            if (error != null) return error;
            r.Label = label;
            r.UsedBy = usedBy;
            r.Limit = limit;
            r.CreatedOn = createdOn;
            r.Notes = notes;
            r.ProviderOverride = Providers.ById(providerOverride) != null ? providerOverride : null;
            r.Seen = true;
            Changed(true);
            return null;
        }

        public void SetIgnored(string id, bool ignored)
        {
            var r = store.Data.Find(id);
            if (r == null) return;
            r.Ignored = ignored;
            r.Seen = true;
            Changed(true);
        }

        public void Forget(string id)
        {
            var r = store.Data.Find(id);
            if (r == null || (r.Present && !r.Ignored)) return;
            store.Data.Keys.Remove(r);
            Changed(true);
        }

        public void OpenManage(string id)
        {
            var r = store.Data.Find(id);
            if (r == null) return;
            var url = Core.ProviderOf(r).ManageUrl;
            Uri uri;
            // Only the fixed provider pages from Providers.cs, and only over https.
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error(ex); }
        }

        public void RevealFile(string id)
        {
            var r = store.Data.Find(id);
            if (r == null || r.Scope != Scopes.File || !Dotenv.IsDotenvFileName(Path.GetFileName(r.Location)) || !File.Exists(r.Location)) return;
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            try { Process.Start(explorer, "/select,\"" + r.Location + "\""); }
            catch (Exception ex) { Log.Error(ex); }
        }

        public void AddFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Pick a folder where your projects live. I'll look for .env files in it.";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string path;
                try { path = Path.GetFullPath(dlg.SelectedPath); } catch (Exception) { return; }
                var root = Path.GetPathRoot(path);
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || Ids.IsUnder(path, windows) || string.Equals(path.TrimEnd('\\'), home.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("That folder is too big to watch well. Pick the folder your projects are in, like Documents\\Projects.", "API Fairy", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var folders = store.Data.Settings.Folders;
                if (folders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))) return;
                if (folders.Count >= 32) return;
                folders.Add(path);
                Changed(true);
                ApplyFolders();
            }
        }

        public void RemoveFolder(string path)
        {
            store.Data.Settings.Folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            Changed(true);
            ApplyFolders();
        }

        public void SetMode(string mode)
        {
            if (mode != Modes.Perch && mode != Modes.PopUp) return;
            store.Data.Settings.FairyMode = mode;
            Save();
            tucked = false;
            if (mode == Modes.Perch) { hideTimer.Stop(); fairy.Show(); }
            else if (!fairy.BubbleVisible) fairy.Hide();
        }

        public void ResetPosition()
        {
            store.Data.Settings.FairyPlaced = false;
            Save();
            fairy.PlaceDefault();
            Say("Back in my corner!", false);
        }

        public void SetStartup(bool on)
        {
            try { Startup.Set(on); }
            catch (Exception ex) { Log.Error(ex); }
            if (dash != null) dash.Refresh();
        }

        public string SetRotateDays(string text)
        {
            int days;
            if (!int.TryParse((text ?? "").Trim(), out days) || days < 7 || days > 3650) return "Use a number of days from 7 to 3650.";
            store.Data.Settings.RotateDays = days;
            Changed(true);
            return null;
        }

        public void CleanHistory()
        {
            var files = Scanner.HistoryFiles().Select(Scanner.HistoryLabel).Distinct().ToList();
            var ask = "Remove the " + historyLines + (historyLines == 1 ? " line" : " lines") + " that contain a key from your " + string.Join(", ", files) + "?\n\n"
                + "Close any open PowerShell or Git Bash windows first, or they may write the lines back.\n\n"
                + "A key that sat in a history file may already have been copied. Replace it at the provider to be safe.";
            if (MessageBox.Show(dash, ask, "API Fairy", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            int removed = Scanner.ScrubHistory();
            MessageBox.Show(dash, removed == 0 ? "Nothing to remove." : "Removed " + removed + (removed == 1 ? " line." : " lines."), "API Fairy", MessageBoxButton.OK, MessageBoxImage.Information);
            ScanNow();
        }

        public void ScanNow()
        {
            walkWanted = true;
            gitWanted = true;
            wake.Set();
        }

        // Windows' own editor stores the key; API Fairy never handles it.
        public void OpenEnvironmentEditor()
        {
            var rundll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe");
            try { Process.Start(rundll, "sysdm.cpl,EditEnvironmentVariables"); }
            catch (Exception ex) { Log.Error(ex); }
        }

        public void Quit()
        {
            stopping = true;
            wake.Set();
            foreach (var w in watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
            watchers.Clear();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Native.FreeIcon(trayIcon);
            if (dash != null) { dash.AllowClose = true; dash.Close(); }
            fairy.Close();
            app.Shutdown();
        }
    }
}
