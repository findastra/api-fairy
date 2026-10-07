using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ApiFairy
{
    public static class EventKinds
    {
        public const string New = "new";
        public const string Back = "back";
        public const string Changed = "changed";
        public const string Gone = "gone";
    }

    public sealed class FairyEvent
    {
        public string Kind;
        public string KeyId;
        public FairyEvent(string kind, string keyId) { Kind = kind; KeyId = keyId; }
    }

    public static class Levels
    {
        public const string Danger = "danger";
        public const string Warn = "warn";
        public const string Info = "info";
    }

    public sealed class Warning
    {
        public readonly string Level;
        public readonly string Text;
        public readonly string Short;
        public Warning(string level, string text, string shortText) { Level = level; Text = text; Short = shortText; }
    }

    // The fairy's judgment: what changed, what's risky, and what to say about it. No UI and no I/O here.
    public static class Core
    {
        public static string Iso(DateTime utc) { return utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }

        public static DateTime? ParseIso(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d.ToUniversalTime();
            return null;
        }

        public static DateTime? ParseDay(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d)) return d.ToUniversalTime();
            return null;
        }

        // Folds a scan into the notes. "changed" tells the caller whether anything worth saving moved.
        public static List<FairyEvent> Reconcile(StoreData d, ScanResult scan, DateTime nowUtc, out bool changed)
        {
            var now = Iso(nowUtc);
            changed = false;
            var events = new List<FairyEvent>();
            var found = new HashSet<string>();
            foreach (var s in scan.Sightings)
            {
                if (!found.Add(s.Id)) continue;
                var r = d.Find(s.Id);
                if (r == null)
                {
                    r = new KeyRecord();
                    r.Id = s.Id;
                    r.FirstSeen = now;
                    r.LastChanged = now;
                    r.Present = true;
                    d.Keys.Add(r);
                    changed = true;
                    events.Add(new FairyEvent(EventKinds.New, r.Id));
                }
                else if (!r.Present)
                {
                    r.Present = true;
                    r.GoneOn = null;
                    r.Seen = false;
                    if (r.Fingerprint != s.Fingerprint) { r.LastChanged = now; r.CreatedOn = null; }
                    changed = true;
                    events.Add(new FairyEvent(EventKinds.Back, r.Id));
                }
                else if (r.Fingerprint != s.Fingerprint)
                {
                    // A different value under the same name: the key was replaced, so its age starts over.
                    r.LastChanged = now;
                    r.CreatedOn = null;
                    r.Seen = false;
                    changed = true;
                    events.Add(new FairyEvent(EventKinds.Changed, r.Id));
                }
                var location = s.Location ?? "";
                if (r.Name != s.Name || r.Scope != s.Scope || r.Location != location || r.ProviderId != s.ProviderId || r.Fingerprint != s.Fingerprint
                    || r.Git != s.Git || r.ExampleFile != s.ExampleFile || r.OneDrive != s.OneDrive)
                {
                    r.Name = s.Name;
                    r.Scope = s.Scope;
                    r.Location = location;
                    r.ProviderId = s.ProviderId;
                    r.Fingerprint = s.Fingerprint;
                    r.Git = s.Git;
                    r.ExampleFile = s.ExampleFile;
                    r.OneDrive = s.OneDrive;
                    changed = true;
                }
            }
            foreach (var r in d.Keys)
            {
                if (!r.Present || found.Contains(r.Id)) continue;
                if (r.Scope == Scopes.File && !IsWatched(r, d.Settings))
                {
                    r.Present = false;
                    r.GoneOn = now;
                    changed = true;
                    continue;
                }
                // Only call a key removed when its place was actually checked this time.
                if (!IsCovered(r, scan)) continue;
                r.Present = false;
                r.GoneOn = now;
                r.Seen = false;
                changed = true;
                events.Add(new FairyEvent(EventKinds.Gone, r.Id));
            }
            return events.Where(e => !d.Find(e.KeyId).Ignored).ToList();
        }

        public static bool IsCovered(KeyRecord r, ScanResult scan)
        {
            if (r.Scope == Scopes.File) return scan.CoveredRoots.Any(root => Ids.IsUnder(r.Location, root));
            return scan.CoveredScopes.Contains(r.Scope);
        }

        public static bool IsWatched(KeyRecord r, AppSettings s)
        {
            return r.Scope != Scopes.File || s.Folders.Any(f => Ids.IsUnder(r.Location, f));
        }

        public static Provider ProviderOf(KeyRecord r)
        {
            return Providers.ById(r.ProviderOverride) ?? Providers.ById(r.ProviderId) ?? Providers.Other;
        }

        public static int AgeDays(KeyRecord r, DateTime nowUtc)
        {
            var since = ParseDay(r.CreatedOn) ?? ParseIso(r.LastChanged) ?? ParseIso(r.FirstSeen) ?? nowUtc;
            return Math.Max(0, (int)Math.Floor((nowUtc - since).TotalDays));
        }

        public static string WhereShort(KeyRecord r)
        {
            if (r.Scope == Scopes.User) return "your Windows settings";
            if (r.Scope == Scopes.System) return "Windows system settings";
            try { return Path.GetFileName(Path.GetDirectoryName(r.Location)) + "\\" + Path.GetFileName(r.Location); }
            catch (Exception) { return r.Location; }
        }

        public static string WhereLong(KeyRecord r)
        {
            if (r.Scope == Scopes.User) return "Windows environment variable, your account only";
            if (r.Scope == Scopes.System) return "Windows environment variable, every account on this PC";
            return r.Location;
        }

        static string JoinAnd(IList<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }

        public static List<Warning> Warnings(KeyRecord r, StoreData d, IDictionary<string, List<string>> historyHits, DateTime nowUtc)
        {
            var list = new List<Warning>();
            if (r.Ignored) return list;
            if (!r.Present)
            {
                list.Add(new Warning(Levels.Info, "This key isn't on this PC anymore. A deleted key still works until you revoke it, so revoke it at the provider if you're done with it.", null));
                return list;
            }
            if (r.Scope == Scopes.File)
            {
                if (r.Git == GitStates.Tracked)
                    list.Add(new Warning(Levels.Danger, "This file is committed to git, so anyone with the repo can read this key. Replace the key at the provider, then remove the file from git.", "that file is committed to git."));
                else if (r.Git == GitStates.NotIgnored)
                    list.Add(new Warning(Levels.Danger, "Git isn't ignoring this file, so the key could get committed by accident. Add the file name to .gitignore.", "git isn't ignoring that file."));
                if (r.ExampleFile)
                    list.Add(new Warning(Levels.Danger, "A real-looking key is in an example file, and example files usually get shared. Move it to .env and replace the key.", "that's an example file, and those usually get shared."));
                if (r.OneDrive)
                    list.Add(new Warning(Levels.Warn, "This file syncs to OneDrive, so the key is stored in the cloud too.", "that file syncs to OneDrive."));
            }
            List<string> hits;
            if (historyHits != null && r.Fingerprint != null && historyHits.TryGetValue(r.Fingerprint, out hits) && hits.Count > 0)
                list.Add(new Warning(Levels.Danger, "This key is saved in your " + JoinAnd(hits) + ". Anyone who opens that file can use it. Use Clean history in the leak check.", "it's saved in your " + hits[0] + "."));
            var twins = d.Keys.Where(o => o != r && o.Present && !o.Ignored && o.Fingerprint == r.Fingerprint).Select(WhereShort).Distinct().ToList();
            if (twins.Count > 0)
                list.Add(new Warning(Levels.Warn, "The same key is also in " + JoinAnd(twins) + ". One key per app lets you replace one without breaking the others.", null));
            int age = AgeDays(r, nowUtc);
            if (age >= d.Settings.RotateDays)
                list.Add(new Warning(Levels.Warn, "This key is " + age + " days old. Consider replacing it.", null));
            if (r.Scope == Scopes.System)
                list.Add(new Warning(Levels.Info, "Every account on this PC can read system settings. Your account's own settings are a safer home.", null));
            if (string.IsNullOrWhiteSpace(r.UsedBy) && string.IsNullOrWhiteSpace(r.Label))
                list.Add(new Warning(Levels.Info, "Who uses this key? Fill in Used by, so you know what breaks if you delete it.", null));
            return list;
        }

        public static bool NeedsAttention(KeyRecord r, StoreData d, IDictionary<string, List<string>> hits, DateTime nowUtc)
        {
            if (r.Ignored) return false;
            if (!r.Seen) return true;
            return Warnings(r, d, hits, nowUtc).Any(w => w.Level != Levels.Info);
        }

        public static bool HasDanger(KeyRecord r, StoreData d, IDictionary<string, List<string>> hits, DateTime nowUtc)
        {
            return !r.Ignored && r.Present && Warnings(r, d, hits, nowUtc).Any(w => w.Level == Levels.Danger);
        }

        // Steady moods; "alert" and "sleep" are decided by the window.
        public static string Mood(StoreData d, IDictionary<string, List<string>> hits, int historyKeyLines, DateTime nowUtc)
        {
            if (historyKeyLines > 0 || d.Keys.Any(r => HasDanger(r, d, hits, nowUtc))) return "worried";
            if (d.Keys.Any(r => NeedsAttention(r, d, hits, nowUtc))) return "curious";
            return "happy";
        }

        public static string StatusText(KeyRecord r)
        {
            if (r.Ignored) return "NOT A KEY";
            if (!r.Present) return "REMOVED";
            if (!r.Seen) return "NEW";
            return "ACTIVE";
        }

        public static string Welcome(int count)
        {
            if (count == 0) return "Hi, I'm your API Fairy! No keys yet. I'll pop up when you add one.";
            return "Hi, I'm your API Fairy! I found " + count + (count == 1 ? " key" : " keys") + " on this PC. Tap me to meet " + (count == 1 ? "it." : "them.");
        }

        public static string Bubble(List<FairyEvent> events, StoreData d, IDictionary<string, List<string>> hits, DateTime nowUtc)
        {
            if (events == null || events.Count == 0) return null;
            if (events.Count > 1)
            {
                bool allNew = events.All(e => e.Kind == EventKinds.New);
                return allNew ? events.Count + " new keys! Tap me to see them." : events.Count + " key changes. Tap me to see them.";
            }
            var ev = events[0];
            var r = d.Find(ev.KeyId);
            var p = ProviderOf(r);
            string text;
            if (ev.Kind == EventKinds.New)
                text = "New key! " + r.Name + (p != Providers.Other ? " (" + p.Name + ")" : "") + " in " + WhereShort(r) + ". Tap me to add a note.";
            else if (ev.Kind == EventKinds.Back)
                text = r.Name + " is back in " + WhereShort(r) + ".";
            else if (ev.Kind == EventKinds.Changed)
                text = r.Name + " changed in " + WhereShort(r) + ". A fresh key? I restarted its age.";
            else
                return r.Name + " was removed from " + WhereShort(r) + ". If you're done with it, revoke it at " + (p != Providers.Other ? p.Name : "the provider") + " too.";
            var danger = Warnings(r, d, hits, nowUtc).FirstOrDefault(w => w.Level == Levels.Danger && w.Short != null);
            if (danger != null) text += " Careful: " + danger.Short;
            return text;
        }

        public static string CleanText(string s, bool multiline, int max)
        {
            if (s == null) return null;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\n' && multiline) sb.Append(c);
                else if (c == '\t') sb.Append(' ');
                else if (!char.IsControl(c)) sb.Append(c);
            }
            var t = sb.ToString().Trim();
            if (t.Length > max) t = t.Substring(0, max);
            return t.Length == 0 ? null : t;
        }

        public const string KeyInNotesMessage = "That looks like an actual key. API Fairy keeps notes about keys, never the keys themselves. Take it out and save again.";

        // Returns an error message, or null when the notes are fine to save.
        public static string ValidateNotes(StoreData d, Func<string, string> fingerprint, string createdOn, params string[] fields)
        {
            var known = new HashSet<string>(d.Keys.Where(k => k.Fingerprint != null).Select(k => k.Fingerprint));
            foreach (var f in fields.Concat(new[] { createdOn }))
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (Providers.TextContainsKey(f)) return KeyInNotesMessage;
                foreach (var t in Providers.Tokens(f))
                    if (t.Length >= 12 && known.Contains(fingerprint(Providers.Unquote(t)))) return KeyInNotesMessage;
            }
            if (!string.IsNullOrEmpty(createdOn))
            {
                var day = ParseDay(createdOn);
                if (day == null) return "Write the created date as YYYY-MM-DD, for example 2026-10-07.";
                if (day.Value.Year < 2000 || day.Value > DateTime.UtcNow.AddDays(1)) return "That created date looks off. Use a date between 2000 and today.";
            }
            return null;
        }
    }
}
