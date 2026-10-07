using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ApiFairy
{
    // Everything the dashboard can ask for. The window never touches files, the registry or URLs itself.
    public interface IFairyHost
    {
        StoreData Data { get; }
        IDictionary<string, List<string>> HistoryHits { get; }
        int HistoryKeyLines { get; }
        DateTime LastScanUtc { get; }
        string NotesFile { get; }
        string SteadyMood { get; }
        bool StartupOn { get; }
        string Fingerprint(string text);
        void MarkSeen(string id);
        void MarkAllSeen();
        string SaveNotes(string id, string label, string usedBy, string limit, string createdOn, string notes, string providerOverride);
        void SetIgnored(string id, bool ignored);
        void Forget(string id);
        void OpenManage(string id);
        void RevealFile(string id);
        void AddFolder();
        void RemoveFolder(string path);
        void SetMode(string mode);
        void ResetPosition();
        void SetStartup(bool on);
        string SetRotateDays(string text);
        void CleanHistory();
        void ScanNow();
        void OpenEnvironmentEditor();
        void Quit();
    }

    public sealed class DashboardWindow : Window
    {
        readonly IFairyHost host;
        readonly WriteableBitmap screen = new WriteableBitmap(Sprite.W, Sprite.H, 96, 96, PixelFormats.Bgra32, null);
        readonly Sprite.Clock clock = new Sprite.Clock();
        readonly DispatcherTimer tick = new DispatcherTimer();
        readonly bool reduceMotion = !SystemParameters.ClientAreaAnimation;

        readonly TextBlock counts = Mono("", 12.5, true), state = Mono("", 17, false), lastCheck = Mono("", 11.5, false);
        readonly Border leak = new Border();
        readonly TextBlock leakText = Body("", 13.5);
        readonly Grid keysView = new Grid(), settingsView = new Grid(), howView = new Grid();
        readonly WrapPanel filters = new WrapPanel();
        readonly ListBox list = new ListBox();
        readonly StackPanel detail = new StackPanel();
        readonly ScrollViewer detailScroll = new ScrollViewer();
        readonly StackPanel settingsPanel = new StackPanel();
        readonly Dictionary<string, RadioButton> filterButtons = new Dictionary<string, RadioButton>();
        readonly Dictionary<string, RadioButton> tabButtons = new Dictionary<string, RadioButton>();

        string filter = "all";
        string shownId;
        bool formDirty, suppressSelection, savedFlash;
        public bool AllowClose;

        public DashboardWindow(IFairyHost host)
        {
            this.host = host;
            Title = "API Fairy";
            Width = 1080;
            Height = 760;
            MinWidth = 820;
            MinHeight = 560;
            Background = Theme.Shell;
            Icon = Pixels.IconSource(64);
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var root = new Grid { Margin = new Thickness(18, 16, 18, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(BuildHeader());
            leak.Visibility = Visibility.Collapsed;
            Grid.SetRow(leak, 1);
            root.Children.Add(BuildLeak());
            var tabs = BuildTabs();
            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var content = new Grid();
            Grid.SetRow(content, 3);
            content.Children.Add(keysView);
            content.Children.Add(settingsView);
            content.Children.Add(howView);
            root.Children.Add(content);
            BuildKeys();
            BuildSettings();
            BuildHow();
            ShowTab("keys");
            Content = root;

            tick.Interval = TimeSpan.FromMilliseconds(280);
            tick.Tick += (s, e) => { Pixels.Write(screen, clock.Next(host.SteadyMood, reduceMotion)); UpdateLastCheck(); };
            IsVisibleChanged += (s, e) => { if (IsVisible) tick.Start(); else tick.Stop(); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            Pixels.Write(screen, clock.Next(host.SteadyMood, reduceMotion));
            Refresh();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Native.TintTitleBar(new WindowInteropHelper(this).Handle, Theme.ShellC, Theme.InkC);
        }

        // Closing the window only tucks it away; the fairy keeps watching.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose) { e.Cancel = true; Hide(); }
            base.OnClosing(e);
        }

        // ---------------------------------------------------------------- helpers

        static TextBlock Mono(string text, double size, bool bold)
        {
            return new TextBlock { Text = text, FontFamily = Theme.Mono, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Theme.LcdInk, TextWrapping = TextWrapping.Wrap };
        }

        static TextBlock Body(string text, double size)
        {
            return new TextBlock { Text = text, FontFamily = Theme.Body, FontSize = size, Foreground = Theme.LcdInk, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45 };
        }

        static TextBlock Heading(string text)
        {
            var t = Mono(text, 12, true);
            t.Foreground = Theme.LcdMuted;
            t.Margin = new Thickness(0, 18, 0, 6);
            return t;
        }

        static Border Lcd(UIElement child)
        {
            return new Border { Background = Theme.Lcd, BorderBrush = Theme.Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 12), Child = child };
        }

        static Button Btn(string text, Action click, bool lcd)
        {
            var b = new Button { Content = text };
            if (lcd) b.Style = (Style)Application.Current.Resources["LcdButton"];
            b.Click += (s, e) => click();
            return b;
        }

        static string Day(string iso)
        {
            var d = Core.ParseIso(iso);
            return d == null ? "—" : d.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }

        static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 5) return "just now";
            if (span.TotalMinutes < 1) return (int)span.TotalSeconds + " seconds ago";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + (span.TotalMinutes < 2 ? " minute ago" : " minutes ago");
            return (int)span.TotalHours + (span.TotalHours < 2 ? " hour ago" : " hours ago");
        }

        // ---------------------------------------------------------------- header

        UIElement BuildHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var img = new Image { Source = screen, Width = 128, Height = 96, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var lcd = new Border { Background = Theme.Lcd, BorderBrush = Theme.Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(16), Padding = new Thickness(8), Child = img, VerticalAlignment = VerticalAlignment.Top };
            g.Children.Add(lcd);

            var mid = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
            var title = new TextBlock { Text = "API FAIRY", FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 24, Foreground = Theme.Ink };
            var sub = new TextBlock { Text = "keeps notes about your keys, never the keys", FontFamily = Theme.Mono, FontSize = 12, Foreground = Theme.Ink, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 10) };
            var status = new StackPanel();
            status.Children.Add(counts);
            state.Margin = new Thickness(0, 4, 0, 2);
            status.Children.Add(state);
            lastCheck.Foreground = Theme.LcdMuted;
            status.Children.Add(lastCheck);
            var statusBox = Lcd(status);
            statusBox.Padding = new Thickness(12, 8, 12, 8);
            mid.Children.Add(title);
            mid.Children.Add(sub);
            mid.Children.Add(statusBox);
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            var add = Btn("ADD A KEY", host.OpenEnvironmentEditor, false);
            add.ToolTip = "Opens Windows' own Environment Variables window. Add the key under User variables; I'll pop up when it lands.";
            buttons.Children.Add(add);
            var check = Btn("CHECK NOW", host.ScanNow, false);
            check.ToolTip = "Look for new, changed or removed keys right away, and re-check git.";
            buttons.Children.Add(check);
            var seen = Btn("MARK ALL SEEN", host.MarkAllSeen, false);
            buttons.Children.Add(seen);
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);
            return g;
        }

        UIElement BuildLeak()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Mono("LEAK CHECK", 12, true);
            label.Foreground = Theme.Danger;
            text.Children.Add(label);
            text.Children.Add(leakText);
            g.Children.Add(text);
            var clean = Btn("CLEAN HISTORY", host.CleanHistory, true);
            clean.VerticalAlignment = VerticalAlignment.Center;
            clean.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(clean, 1);
            g.Children.Add(clean);
            leak.Background = Theme.Lcd;
            leak.BorderBrush = Theme.Ink;
            leak.BorderThickness = new Thickness(3);
            leak.CornerRadius = new CornerRadius(16);
            leak.Padding = new Thickness(14, 10, 14, 10);
            leak.Margin = new Thickness(0, 0, 0, 12);
            leak.Child = g;
            return leak;
        }

        UIElement BuildTabs()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var t in new[] { new[] { "keys", "KEYS" }, new[] { "settings", "SETTINGS" }, new[] { "how", "HOW TO" } })
            {
                var id = t[0];
                var rb = new RadioButton { Content = t[1], GroupName = "tabs", Style = (Style)Application.Current.Resources["Tab"], IsChecked = id == "keys" };
                rb.Checked += (s, e) => ShowTab(id);
                tabButtons[id] = rb;
                row.Children.Add(rb);
            }
            return row;
        }

        public void SelectTab(string id) { tabButtons[id].IsChecked = true; }

        public void SelectKey(string id)
        {
            var item = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == id);
            if (item != null) list.SelectedItem = item;
        }

        void ShowTab(string id)
        {
            keysView.Visibility = id == "keys" ? Visibility.Visible : Visibility.Collapsed;
            settingsView.Visibility = id == "settings" ? Visibility.Visible : Visibility.Collapsed;
            howView.Visibility = id == "how" ? Visibility.Visible : Visibility.Collapsed;
            if (id == "settings") RefreshSettings();
        }

        void UpdateLastCheck()
        {
            lastCheck.Text = host.LastScanUtc == DateTime.MinValue ? "Checking…" : "Last checked " + Ago(host.LastScanUtc) + ". I check every 2 seconds.";
        }

        // ---------------------------------------------------------------- keys tab

        void BuildKeys()
        {
            keysView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400), MinWidth = 300 });
            keysView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            keysView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new DockPanel();
            DockPanel.SetDock(filters, Dock.Top);
            filters.Margin = new Thickness(0, 0, 0, 6);
            foreach (var f in new[] { new[] { "all", "ALL" }, new[] { "needs", "NEEDS YOU" }, new[] { "active", "ACTIVE" }, new[] { "gone", "REMOVED" }, new[] { "ignored", "NOT KEYS" } })
            {
                var id = f[0];
                var rb = new RadioButton { Tag = f[1], GroupName = "filters", Style = (Style)Application.Current.Resources["Filter"], IsChecked = id == filter };
                rb.Checked += (s, e) => { filter = id; Refresh(); };
                filterButtons[id] = rb;
                filters.Children.Add(rb);
            }
            left.Children.Add(filters);
            list.Background = Brushes.Transparent;
            list.BorderThickness = new Thickness(0);
            list.ItemContainerStyle = (Style)Application.Current.Resources["KeyItem"];
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.SelectionChanged += (s, e) => { if (!suppressSelection) ShowDetail(list.SelectedItem is ListBoxItem ? (string)((ListBoxItem)list.SelectedItem).Tag : null, true); };
            left.Children.Add(list);
            keysView.Children.Add(Lcd(left));

            detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            detailScroll.Content = detail;
            var right = Lcd(detailScroll);
            Grid.SetColumn(right, 2);
            keysView.Children.Add(right);
        }

        IEnumerable<KeyRecord> Filtered(string which)
        {
            var d = host.Data;
            var now = DateTime.UtcNow;
            IEnumerable<KeyRecord> all = d.Keys;
            switch (which)
            {
                case "needs": return all.Where(r => Core.NeedsAttention(r, d, host.HistoryHits, now));
                case "active": return all.Where(r => r.Present && !r.Ignored);
                case "gone": return all.Where(r => !r.Present && !r.Ignored);
                case "ignored": return all.Where(r => r.Ignored);
                default: return all.Where(r => !r.Ignored);
            }
        }

        // Rebuilds what's on screen from the latest data, keeping the selection and any half-typed notes.
        public void Refresh()
        {
            var d = host.Data;
            var now = DateTime.UtcNow;
            var hits = host.HistoryHits;
            var active = d.Keys.Count(r => r.Present && !r.Ignored);
            var needs = d.Keys.Count(r => Core.NeedsAttention(r, d, hits, now));
            var risky = d.Keys.Count(r => Core.HasDanger(r, d, hits, now));
            counts.Text = active + (active == 1 ? " KEY" : " KEYS") + "  ·  " + needs + " NEED YOU  ·  " + risky + " AT RISK";
            if (host.HistoryKeyLines > 0 || risky > 0) state.Text = "a key is at risk. Look for the red marks.";
            else if (needs > 0) state.Text = needs + (needs == 1 ? " key needs" : " keys need") + " a look.";
            else if (active == 0) state.Text = "no keys yet. Add one and I'll pop up.";
            else state.Text = "all clear.";
            UpdateLastCheck();

            leak.Visibility = host.HistoryKeyLines > 0 ? Visibility.Visible : Visibility.Collapsed;
            leakText.Text = host.HistoryKeyLines + (host.HistoryKeyLines == 1 ? " line" : " lines") + " in your shell history contain a key. Anyone who opens those files can copy it. Clean history removes those lines; then replace any key you still use.";

            foreach (var pair in filterButtons)
                pair.Value.Content = (string)pair.Value.Tag + " " + Filtered(pair.Key).Count();

            var rows = Filtered(filter)
                .OrderByDescending(r => Core.HasDanger(r, d, hits, now))
                .ThenByDescending(r => Core.NeedsAttention(r, d, hits, now))
                .ThenByDescending(r => r.Present)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var keep = shownId;
            suppressSelection = true;
            list.Items.Clear();
            foreach (var r in rows) list.Items.Add(Row(r, d, hits, now));
            if (rows.Count == 0)
            {
                var empty = Body(d.Keys.Count == 0 ? "No keys yet. Click ADD A KEY, or add one to a .env file in a watched folder, and I'll pop up." : "Nothing here.", 13.5);
                empty.Margin = new Thickness(6, 10, 6, 0);
                list.Items.Add(new ListBoxItem { Content = empty, IsEnabled = false, Style = (Style)Application.Current.Resources["KeyItem"] });
            }
            var again = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == keep && keep != null);
            if (again == null && rows.Count > 0) again = (ListBoxItem)list.Items[0];
            list.SelectedItem = again;
            suppressSelection = false;
            var id = again == null ? null : (string)again.Tag;
            if (id != shownId || !formDirty) ShowDetail(id, id != shownId);
            if (settingsView.Visibility == Visibility.Visible) RefreshSettings();
        }

        ListBoxItem Row(KeyRecord r, StoreData d, IDictionary<string, List<string>> hits, DateTime now)
        {
            var warnings = Core.Warnings(r, d, hits, now);
            string mark; Brush markBg, markFg;
            if (warnings.Any(w => w.Level == Levels.Danger)) { mark = "!"; markBg = Theme.Danger; markFg = Theme.Lcd; }
            else if (r.Ignored) { mark = "–"; markBg = Brushes.Transparent; markFg = Theme.LcdMuted; }
            else if (!r.Present) { mark = "×"; markBg = Brushes.Transparent; markFg = Theme.LcdMuted; }
            else if (!r.Seen) { mark = "•"; markBg = Theme.LcdInk; markFg = Theme.Lcd; }
            else if (warnings.Any(w => w.Level == Levels.Warn)) { mark = "~"; markBg = Brushes.Transparent; markFg = Theme.Warn; }
            else { mark = "✓"; markBg = Brushes.Transparent; markFg = Theme.Good; }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var box = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), BorderBrush = Theme.LcdInk, BorderThickness = new Thickness(2), Background = markBg, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            box.Child = new TextBlock { Text = mark, FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = markFg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
            g.Children.Add(box);
            var text = new StackPanel();
            var name = Mono(r.Name, 14, true);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (!r.Present || r.Ignored) name.Foreground = Theme.LcdMuted;
            var p = Core.ProviderOf(r);
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(r.Label)) bits.Add(r.Label);
            bits.Add(p == Providers.Other ? "unknown provider" : p.Name);
            bits.Add(Core.WhereShort(r));
            if (r.Present) bits.Add(Core.AgeDays(r, now) + "d old");
            var sub = Mono(string.Join(" · ", bits), 11.5, false);
            sub.Foreground = Theme.LcdMuted;
            sub.TextWrapping = TextWrapping.NoWrap;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(name);
            text.Children.Add(sub);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            var item = new ListBoxItem { Content = g, Tag = r.Id };
            System.Windows.Automation.AutomationProperties.SetName(item, r.Name + ", " + Core.StatusText(r).ToLowerInvariant() + (mark == "!" ? ", at risk" : ""));
            return item;
        }

        void ShowDetail(string id, bool markSeen)
        {
            shownId = id;
            formDirty = false;
            detail.Children.Clear();
            var r = host.Data.Find(id);
            if (r == null)
            {
                detail.Children.Add(Mono("Pick a key on the left to see everything I know about it.", 14, false));
                return;
            }
            // Marking seen changes the list, so do it after this detail is built.
            if (markSeen && !r.Seen) Dispatcher.BeginInvoke(new Action(() => host.MarkSeen(id)));
            var d = host.Data;
            var now = DateTime.UtcNow;
            var p = Core.ProviderOf(r);

            var head = new DockPanel { LastChildFill = true };
            var pill = new Border { Background = r.Present && !r.Ignored ? Theme.LcdInk : Theme.LcdMuted, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 4, 0, 0) };
            pill.Child = new TextBlock { Text = Core.StatusText(r), FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Theme.Lcd };
            DockPanel.SetDock(pill, Dock.Right);
            head.Children.Add(pill);
            var name = Mono(r.Name, 21, true);
            head.Children.Add(name);
            detail.Children.Add(head);
            var provLine = Mono((p == Providers.Other ? "Provider unknown" : p.Name) + (r.ProviderOverride != null ? " (you chose this)" : " (detected)"), 13, false);
            provLine.Foreground = Theme.LcdMuted;
            provLine.Margin = new Thickness(0, 2, 0, 4);
            detail.Children.Add(provLine);

            // facts
            var facts = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Action<string, string> fact = (label, value) =>
            {
                int row = facts.RowDefinitions.Count;
                facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = Mono(label, 11.5, true);
                l.Foreground = Theme.LcdMuted;
                l.Margin = new Thickness(0, 4, 8, 4);
                var v = Mono(value, 13, false);
                v.Margin = new Thickness(0, 3, 0, 3);
                Grid.SetRow(l, row);
                Grid.SetRow(v, row);
                Grid.SetColumn(v, 1);
                facts.Children.Add(l);
                facts.Children.Add(v);
            };
            fact("WHERE", Core.WhereLong(r));
            if (r.Present)
            {
                int age = Core.AgeDays(r, now);
                var due = (Core.ParseDay(r.CreatedOn) ?? Core.ParseIso(r.LastChanged) ?? now).AddDays(d.Settings.RotateDays).ToLocalTime();
                fact("AGE", age + (age == 1 ? " day" : " days") + (string.IsNullOrEmpty(r.CreatedOn) ? " (since I first saw this value)" : " (since the created date you set)") + ". Replace by " + due.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) + ".");
            }
            else if (!string.IsNullOrEmpty(r.GoneOn)) fact("REMOVED", Day(r.GoneOn));
            fact("FIRST SEEN", Day(r.FirstSeen));
            fact("VALUE CHANGED", Day(r.LastChanged));
            if (r.Scope == Scopes.File)
            {
                string git;
                switch (r.Git)
                {
                    case GitStates.Ignored: git = "Ignored by git. Good."; break;
                    case GitStates.NotIgnored: git = "NOT ignored by git."; break;
                    case GitStates.Tracked: git = "Committed to git."; break;
                    case GitStates.NoRepo: git = "Not in a git repo."; break;
                    default: git = "Couldn't check (is git installed?)."; break;
                }
                fact("GIT", git);
            }
            fact("FINGERPRINT", r.Fingerprint + "  (scrambled, can't be turned back into the key)");
            detail.Children.Add(facts);

            var warnings = Core.Warnings(r, d, host.HistoryHits, now);
            if (warnings.Count > 0)
            {
                detail.Children.Add(Heading("HEADS UP"));
                foreach (var w in warnings)
                {
                    var bar = w.Level == Levels.Danger ? Theme.Danger : w.Level == Levels.Warn ? Theme.Warn : Theme.LcdMuted;
                    var t = Body(w.Text, 13.5);
                    if (w.Level == Levels.Danger) t.FontWeight = FontWeights.SemiBold;
                    detail.Children.Add(new Border { BorderBrush = bar, BorderThickness = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 0, 0, 8), Child = t });
                }
            }

            // notes form
            detail.Children.Add(Heading("YOUR NOTES"));
            var caption = Body("Only notes about the key. Never paste the key itself here; I'll refuse to save it.", 12.5);
            caption.Foreground = Theme.LcdMuted;
            caption.Margin = new Thickness(0, -2, 0, 8);
            detail.Children.Add(caption);
            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Func<string, string, string, int, bool, TextBox> field = (label, hint, value, max, multi) =>
            {
                int row = form.RowDefinitions.Count;
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new StackPanel { Margin = new Thickness(0, 6, 8, 6) };
                l.Children.Add(Mono(label, 11.5, true));
                if (hint != null) { var h = Mono(hint, 10.5, false); h.Foreground = Theme.LcdMuted; l.Children.Add(h); }
                var tb = new TextBox { Text = value ?? "", MaxLength = max, Margin = new Thickness(0, 4, 0, 4) };
                if (multi) { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; tb.MinHeight = 74; tb.VerticalContentAlignment = VerticalAlignment.Top; }
                tb.TextChanged += (s, e) => formDirty = true;
                System.Windows.Automation.AutomationProperties.SetName(tb, label);
                Grid.SetRow(l, row);
                Grid.SetRow(tb, row);
                Grid.SetColumn(tb, 1);
                form.Children.Add(l);
                form.Children.Add(tb);
                return tb;
            };
            var label1 = field("NICKNAME", "its name at the provider", r.Label, 80, false);
            var usedBy = field("USED BY", "apps that break without it", r.UsedBy, 200, false);
            var limit = field("SPENDING LIMIT", "like $10/month", r.Limit, 60, false);
            var created = field("CREATED ON", "YYYY-MM-DD", r.CreatedOn, 10, false);

            int prow = form.RowDefinitions.Count;
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var plabel = Mono("PROVIDER", 11.5, true);
            plabel.Margin = new Thickness(0, 10, 8, 6);
            var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 4), FontFamily = Theme.Mono, FontSize = 13, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
            var detected = Providers.ById(r.ProviderId) ?? Providers.Other;
            combo.Items.Add(new ComboBoxItem { Content = "Detected: " + detected.Name, Tag = null });
            foreach (var each in Providers.All.Concat(new[] { Providers.Other }))
                combo.Items.Add(new ComboBoxItem { Content = each.Name, Tag = each.Id });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == r.ProviderOverride) ?? combo.Items[0];
            combo.SelectionChanged += (s, e) => formDirty = true;
            System.Windows.Automation.AutomationProperties.SetName(combo, "Provider");
            Grid.SetRow(plabel, prow);
            Grid.SetRow(combo, prow);
            Grid.SetColumn(combo, 1);
            form.Children.Add(plabel);
            form.Children.Add(combo);
            var notes = field("NOTES", null, r.Notes, 2000, true);
            detail.Children.Add(form);

            var saveRow = new WrapPanel { Margin = new Thickness(150, 6, 0, 0) };
            var result = Mono(savedFlash ? "Saved." : "", 12.5, true);
            result.Foreground = Theme.Good;
            savedFlash = false;
            result.VerticalAlignment = VerticalAlignment.Center;
            result.Margin = new Thickness(6, 0, 0, 6);
            var keyId = r.Id;
            saveRow.Children.Add(Btn("SAVE NOTES", () =>
            {
                var chosen = combo.SelectedItem as ComboBoxItem;
                // A successful save refreshes the window, which rebuilds this form showing "Saved."
                formDirty = false;
                savedFlash = true;
                var error = host.SaveNotes(keyId, label1.Text, usedBy.Text, limit.Text, created.Text, notes.Text, chosen == null ? null : (string)chosen.Tag);
                if (error != null)
                {
                    formDirty = true;
                    savedFlash = false;
                    result.Text = error;
                    result.Foreground = Theme.Danger;
                }
            }, true));
            saveRow.Children.Add(result);
            detail.Children.Add(saveRow);

            // actions
            detail.Children.Add(Heading("DO SOMETHING"));
            var actions = new WrapPanel();
            if (p.ManageUrl != null)
            {
                var manage = Btn("MANAGE AT " + p.Name.ToUpperInvariant() + " ↗", () => host.OpenManage(keyId), true);
                manage.ToolTip = "Opens " + p.ManageUrl + " in your browser, to replace, limit or revoke the key there.";
                actions.Children.Add(manage);
            }
            if (r.Scope == Scopes.File && r.Present) actions.Children.Add(Btn("SHOW FILE", () => host.RevealFile(keyId), true));
            if (r.Ignored) actions.Children.Add(Btn("IT IS A KEY", () => host.SetIgnored(keyId, false), true));
            else actions.Children.Add(Btn("NOT A KEY", () => host.SetIgnored(keyId, true), true));
            if (!r.Present || r.Ignored)
            {
                actions.Children.Add(Btn("FORGET IT", () =>
                {
                    if (MessageBox.Show(this, "Forget " + r.Name + " and your notes about it? This can't be undone.", "API Fairy", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                        host.Forget(keyId);
                }, true));
            }
            detail.Children.Add(actions);
            if (markSeen) detailScroll.ScrollToTop();
        }

        // ---------------------------------------------------------------- settings tab

        readonly ListBox folders = new ListBox();
        readonly RadioButton perch = new RadioButton(), popUp = new RadioButton();
        readonly CheckBox startup = new CheckBox();
        readonly TextBox rotate = new TextBox { Width = 70, MaxLength = 4 };
        readonly TextBlock rotateResult = Mono("", 12, true);
        readonly TextBlock notesFile = Body("", 13.5);
        bool settingsLoading;

        void BuildSettings()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = settingsPanel };
            settingsView.Children.Add(Lcd(scroll));
            var s = settingsPanel;
            s.MaxWidth = 760;
            s.HorizontalAlignment = HorizontalAlignment.Left;

            var h = Heading("WATCHED FOLDERS");
            h.Margin = new Thickness(0, 0, 0, 6);
            s.Children.Add(h);
            s.Children.Add(Body("I look for .env files in these folders and their subfolders, up to 6 levels down. I skip node_modules, .git and build folders, and never follow shortcuts or junctions out of the folder.", 13.5));
            folders.Background = Brushes.Transparent;
            folders.BorderThickness = new Thickness(0);
            folders.ItemContainerStyle = (Style)Application.Current.Resources["KeyItem"];
            folders.Margin = new Thickness(0, 8, 0, 6);
            s.Children.Add(folders);
            var fb = new WrapPanel();
            fb.Children.Add(Btn("ADD FOLDER…", host.AddFolder, true));
            fb.Children.Add(Btn("STOP WATCHING SELECTED", () =>
            {
                var item = folders.SelectedItem as ListBoxItem;
                if (item != null) host.RemoveFolder((string)item.Tag);
            }, true));
            s.Children.Add(fb);

            s.Children.Add(Heading("THE FAIRY ON YOUR SCREEN"));
            perch.Content = "Stay on screen in the corner";
            popUp.Content = "Stay hidden, and pop up only when a key is added, changed or removed";
            foreach (var rb in new[] { perch, popUp })
            {
                rb.GroupName = "mode";
                rb.Style = (Style)Application.Current.Resources["Choice"];
                s.Children.Add(rb);
            }
            perch.Checked += (o, e) => { if (!settingsLoading) host.SetMode(Modes.Perch); };
            popUp.Checked += (o, e) => { if (!settingsLoading) host.SetMode(Modes.PopUp); };
            var back = Btn("PUT HER BACK IN THE CORNER", host.ResetPosition, true);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(back);
            s.Children.Add(Body("Drag her anywhere. Right-click her for more.", 12.5));

            s.Children.Add(Heading("START WITH WINDOWS"));
            startup.Content = "Start API Fairy when I sign in, so she never misses a new key";
            startup.Checked += (o, e) => { if (!settingsLoading) host.SetStartup(true); };
            startup.Unchecked += (o, e) => { if (!settingsLoading) host.SetStartup(false); };
            s.Children.Add(startup);

            s.Children.Add(Heading("REPLACE REMINDER"));
            var rr = new WrapPanel();
            var lbl = Body("Remind me when a key is older than", 13.5);
            lbl.VerticalAlignment = VerticalAlignment.Center;
            lbl.Margin = new Thickness(0, 0, 8, 6);
            rr.Children.Add(lbl);
            rotate.Margin = new Thickness(0, 0, 8, 6);
            rr.Children.Add(rotate);
            var days = Body("days.", 13.5);
            days.VerticalAlignment = VerticalAlignment.Center;
            days.Margin = new Thickness(0, 0, 10, 6);
            rr.Children.Add(days);
            rr.Children.Add(Btn("SAVE", () =>
            {
                var error = host.SetRotateDays(rotate.Text);
                rotateResult.Text = error ?? "Saved.";
                rotateResult.Foreground = error == null ? Theme.Good : Theme.Danger;
            }, true));
            rotateResult.VerticalAlignment = VerticalAlignment.Center;
            rr.Children.Add(rotateResult);
            s.Children.Add(rr);

            s.Children.Add(Heading("YOUR NOTES FILE"));
            s.Children.Add(notesFile);

            s.Children.Add(Heading("QUIT"));
            s.Children.Add(Body("Closing this window keeps the fairy watching. Quit stops her until you start API Fairy again.", 13.5));
            var quit = Btn("QUIT API FAIRY", host.Quit, true);
            quit.HorizontalAlignment = HorizontalAlignment.Left;
            quit.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(quit);
        }

        void RefreshSettings()
        {
            settingsLoading = true;
            var st = host.Data.Settings;
            var selected = folders.SelectedItem is ListBoxItem ? (string)((ListBoxItem)folders.SelectedItem).Tag : null;
            folders.Items.Clear();
            foreach (var f in st.Folders)
            {
                var item = new ListBoxItem { Content = Mono(f, 13, false), Tag = f };
                folders.Items.Add(item);
                if (f == selected) folders.SelectedItem = item;
            }
            if (st.Folders.Count == 0) folders.Items.Add(new ListBoxItem { Content = Body("No folders yet. Only Windows settings are being watched.", 13), IsEnabled = false });
            perch.IsChecked = st.FairyMode == Modes.Perch;
            popUp.IsChecked = st.FairyMode == Modes.PopUp;
            startup.IsChecked = host.StartupOn;
            if (!rotate.IsKeyboardFocused) rotate.Text = st.RotateDays.ToString(CultureInfo.InvariantCulture);
            notesFile.Text = "Encrypted for your Windows account at " + host.NotesFile + ". Other accounts and other PCs can't read it, and the folder is locked to your account.";
            settingsLoading = false;
        }

        // ---------------------------------------------------------------- how-to tab

        void BuildHow()
        {
            var s = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            Action<string> h = t => { var x = Heading(t); if (s.Children.Count == 0) x.Margin = new Thickness(0, 0, 0, 6); s.Children.Add(x); };
            Action<string> p = t => { var x = Body(t, 14); x.Margin = new Thickness(0, 0, 0, 6); s.Children.Add(x); };

            h("ADD A KEY THE SAFE WAY");
            p("1. Click ADD A KEY. Windows opens its Environment Variables window.");
            p("2. Under User variables, click New.");
            p("3. Name it after the provider, like OPENAI_API_KEY, and paste the key as the value.");
            p("4. Click OK twice. I'll pop up within a couple of seconds.");
            p("5. Restart any app that needs the key, so it sees the new value.");
            p("Why not setx in a terminal? Terminals save what you type in a history file, key included. If that already happened, the leak check will offer to clean it.");

            h("NAME KEYS SO FUTURE YOU KNOWS");
            p("When you create a key at the provider, name it after what uses it, where, and when: image-bridge-desktop-2026-10. Put the same name in NICKNAME here. Avoid names like \"test\" or \"my key\".");

            h("SIX HABITS");
            p("• One key per app, so you can replace one without breaking the rest.");
            p("• Set a spending limit on the provider's billing page.");
            p("• Give each key only the access it needs.");
            p("• Keep keys out of chats, repos and screenshots.");
            p("• Delete keys you've stopped using, and replace any you think leaked.");
            p("• Check the provider's usage page now and then for anything you don't recognize.");

            h("IF A KEY LEAKS");
            p("1. Revoke it at the provider. MANAGE takes you to the right page.");
            p("2. Create a new key and put it where the old one was. I'll notice the change.");
            p("3. Check the provider's usage page for anything you didn't do.");

            h("WHAT API FAIRY CAN SEE");
            p("I read key values for a moment, to recognize the provider and make a scrambled fingerprint, so I can tell when a key changes or shows up twice. I never store, show, copy or send a key. I have no internet code at all; the only page I ever open is a provider's own key page, when you click MANAGE.");
            howView.Children.Add(Lcd(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = s }));
        }
    }
}
