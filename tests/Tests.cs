using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ApiFairy.Tests
{
    // A tiny test runner, so the tests build with the same built-in compiler and need no packages.
    static class Program
    {
        static int passed, failed;

        static void Check(string name, bool ok)
        {
            if (ok) passed++;
            else { failed++; Console.WriteLine("FAIL  " + name); }
        }

        static int Main(string[] args)
        {
            var root = args.Length > 0 ? args[0] : ".";
            Recognition();
            DotenvParsing();
            Reconciling();
            Warnings();
            NotesGuard();
            StoreRoundTrip();
            Fingerprints();
            NoNetworkCode(root);
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        // Made-up keys in the right shapes. None of these are real.
        const string FakeOpenAi = "sk-proj-AbCdEfGhIjKlMnOpQrStUvWxYz0123456789abcdEFGH";
        const string FakeAnthropic = "sk-ant-api03-AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-abcdEFGH";
        const string FakeGithub = "ghp_AbCdEfGhIjKlMnOpQrStUvWxYz0123456789";

        static void Recognition()
        {
            Check("anthropic by value", Providers.Detect("WHATEVER", FakeAnthropic).Id == "anthropic");
            Check("openai by value", Providers.Detect("MY_KEY", FakeOpenAi).Id == "openai");
            Check("openrouter beats openai", Providers.Detect("OPENAI_API_KEY", "sk-or-v1-0123456789abcdef0123456789abcdef").Id == "openrouter");
            Check("github by value", Providers.Detect("X", FakeGithub).Id == "github");
            Check("gemini by name on a google-shaped key", Providers.Detect("GEMINI_API_KEY", "AIza" + new string('b', 35)).Id == "gemini");
            Check("google by value", Providers.Detect("MAPS_KEY", "AIza" + new string('b', 35)).Id == "google");
            Check("stripe by value", Providers.Detect("PAY", "sk_live_0123456789abcdefABCD").Id == "stripe");
            Check("name fallback", Providers.Detect("REPLICATE_API_TOKEN", "abcdefgh12345678").Id == "replicate");
            Check("unknown", Providers.Detect("SOME_TOKEN", "abcdefgh12345678").Id == "other");

            Check("candidate: api key name", Providers.IsCandidate("OPENAI_API_KEY", "abcdefgh12345678", false));
            Check("candidate: value shape with odd name", Providers.IsCandidate("PRIMARY", FakeOpenAi, false));
            Check("candidate: password in URL", Providers.IsCandidate("DATABASE_URL", "postgres://me:hunter2pass@db.example.com/app", false));
            Check("not: plain url", !Providers.IsCandidate("OPENAI_BASE_URL", "https://api.openai.com/v1", false));
            Check("not: path", !Providers.IsCandidate("GOOGLE_APPLICATION_CREDENTIALS", @"C:\keys\service.json", false));
            Check("not: placeholder", !Providers.IsCandidate("OPENAI_API_KEY", "your-key-here", false));
            Check("not: sk-... placeholder", !Providers.IsCandidate("OPENAI_API_KEY", "sk-...", false));
            Check("not: PATH", !Providers.IsCandidate("Path", @"C:\Windows;C:\Tools", false));
            Check("not: id suffix", !Providers.IsCandidate("DISCORD_CLIENT_ID", "123456789012345678", false));
            Check("not: number", !Providers.IsCandidate("API_KEY_COUNT", "12345678", false));
            Check("not: empty", !Providers.IsCandidate("API_KEY", "", false));
            Check("strict: placeholder in example file", !Providers.IsCandidate("OPENAI_API_KEY", "sk-proj-your-key-goes-here", true));
            Check("strict: real shape in example file", Providers.IsCandidate("OPENAI_API_KEY", FakeOpenAi, true));

            Check("looks real: random token", Providers.LooksLikeRealKey("q8Zt3LmP0vXc7RbN2wYk5HsJ9dGf4AeU1oTi6"));
            Check("looks real: not a sentence", !Providers.LooksLikeRealKey("this is just a note about my key"));
            Check("looks real: not a URL", !Providers.LooksLikeRealKey("https://platform.openai.com/api-keys"));
            Check("looks real: not a GUID-less path", !Providers.LooksLikeRealKey(@"C:\Users\you\Documents\Projects\paper-girl\.env"));
            Check("looks real: not hex-free words", !Providers.LooksLikeRealKey("image-bridge-desktop-2026-10"));
        }

        static void DotenvParsing()
        {
            var text = "\uFEFF# comment\nOPENAI_API_KEY=" + FakeOpenAi + "\nexport ANTHROPIC_API_KEY=\"" + FakeAnthropic + "\"\nNAME='quoted value' \nPLAIN=abc # trailing\nbroken line\n=nothing\n9BAD=x\r\nWIN=crlf\r\n";
            var e = Dotenv.Parse(text);
            Check("dotenv count", e.Count == 5);
            Check("dotenv plain", e[0].Name == "OPENAI_API_KEY" && e[0].Value == FakeOpenAi);
            Check("dotenv export + quotes", e[1].Name == "ANTHROPIC_API_KEY" && e[1].Value == FakeAnthropic);
            Check("dotenv single quotes", e[2].Value == "quoted value");
            Check("dotenv trailing comment", e[3].Value == "abc");
            Check("dotenv crlf", e[4].Name == "WIN" && e[4].Value == "crlf");
            Check("names: .env", Dotenv.IsDotenvFileName(".env") && Dotenv.IsDotenvFileName(".env.local") && Dotenv.IsDotenvFileName("prod.env") && Dotenv.IsDotenvFileName(".envrc"));
            Check("names: not env", !Dotenv.IsDotenvFileName("environment.ts") && !Dotenv.IsDotenvFileName(".env-cmdrc.json") && !Dotenv.IsDotenvFileName("x.envelope"));
            Check("example files", Dotenv.IsExampleFileName(".env.example") && Dotenv.IsExampleFileName(".env.sample") && !Dotenv.IsExampleFileName(".env.local"));
        }

        static StoreData NewData()
        {
            var d = new StoreData();
            d.Pepper = Convert.ToBase64String(new byte[32]);
            d.Settings.Folders.Add(@"C:\Projects");
            d.Settings.Welcomed = true;
            return d;
        }

        static Sighting S(string scope, string name, string fp, string location)
        {
            var s = new Sighting { Scope = scope, Name = name, Fingerprint = fp, Location = location ?? "", ProviderId = "openai", Git = GitStates.Ignored };
            s.Id = scope == Scopes.File ? Ids.ForFile(location, name) : Ids.ForEnv(scope, name);
            return s;
        }

        static ScanResult Scan(params Sighting[] found)
        {
            var r = new ScanResult();
            r.CoveredScopes.Add(Scopes.User);
            r.CoveredScopes.Add(Scopes.System);
            r.CoveredRoots.Add(@"C:\Projects\");
            r.Sightings.AddRange(found);
            return r;
        }

        static void Reconciling()
        {
            var d = NewData();
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            bool changed;
            var ev = Core.Reconcile(d, Scan(S(Scopes.User, "OPENAI_API_KEY", "aaaa", null)), t0, out changed);
            Check("new key event", ev.Count == 1 && ev[0].Kind == EventKinds.New && changed);
            var r = d.Keys.Single();
            Check("new key is unseen", !r.Seen && r.Present);

            ev = Core.Reconcile(d, Scan(S(Scopes.User, "OPENAI_API_KEY", "aaaa", null)), t0.AddSeconds(2), out changed);
            Check("same scan: quiet and unchanged", ev.Count == 0 && !changed);

            r.Seen = true;
            r.CreatedOn = "2025-01-01";
            ev = Core.Reconcile(d, Scan(S(Scopes.User, "OPENAI_API_KEY", "bbbb", null)), t0.AddDays(1), out changed);
            Check("changed value event", ev.Count == 1 && ev[0].Kind == EventKinds.Changed);
            Check("changed value resets age", r.CreatedOn == null && Core.AgeDays(r, t0.AddDays(3)) == 2);

            var partial = Scan();
            partial.CoveredScopes.Clear();
            ev = Core.Reconcile(d, partial, t0.AddDays(2), out changed);
            Check("unreadable scope: not removed", ev.Count == 0 && r.Present);

            ev = Core.Reconcile(d, Scan(), t0.AddDays(2), out changed);
            Check("removed event", ev.Count == 1 && ev[0].Kind == EventKinds.Gone && !r.Present);

            ev = Core.Reconcile(d, Scan(S(Scopes.User, "OPENAI_API_KEY", "bbbb", null)), t0.AddDays(3), out changed);
            Check("back event", ev.Count == 1 && ev[0].Kind == EventKinds.Back && r.Present);

            r.Ignored = true;
            ev = Core.Reconcile(d, Scan(), t0.AddDays(4), out changed);
            Check("ignored keys stay quiet", ev.Count == 0);

            var f = S(Scopes.File, "STRIPE_KEY", "cccc", @"C:\Projects\shop\.env");
            Core.Reconcile(d, Scan(f), t0, out changed);
            d.Settings.Folders.Clear();
            ev = Core.Reconcile(d, Scan(), t0.AddDays(1), out changed);
            var fr = d.Find(f.Id);
            Check("unwatched folder: quietly not present", ev.Count == 0 && !fr.Present);

            Check("bubble: one new", Core.Bubble(new List<FairyEvent> { new FairyEvent(EventKinds.New, r.Id) }, d, null, t0).StartsWith("New key! OPENAI_API_KEY"));
            Check("bubble: many new", Core.Bubble(new List<FairyEvent> { new FairyEvent(EventKinds.New, r.Id), new FairyEvent(EventKinds.New, fr.Id) }, d, null, t0) == "2 new keys! Tap me to see them.");
            Check("welcome", Core.Welcome(0).Contains("No keys yet") && Core.Welcome(3).Contains("3 keys"));
        }

        static void Warnings()
        {
            var d = NewData();
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var file = S(Scopes.File, "STRIPE_KEY", "dddd", @"C:\Projects\shop\.env");
            file.Git = GitStates.NotIgnored;
            var env = S(Scopes.User, "STRIPE_KEY", "dddd", null);
            bool changed;
            Core.Reconcile(d, Scan(file, env), t0, out changed);
            var fr = d.Find(file.Id);
            var w = Core.Warnings(fr, d, null, t0);
            Check("danger: not ignored by git", w.Any(x => x.Level == Levels.Danger && x.Text.Contains("isn't ignoring")));
            Check("warn: same key twice", w.Any(x => x.Level == Levels.Warn && x.Text.Contains("also in")));
            Check("bubble mentions the danger", Core.Bubble(new List<FairyEvent> { new FairyEvent(EventKinds.New, fr.Id) }, d, null, t0).Contains("Careful: git isn't ignoring"));
            var hits = new Dictionary<string, List<string>> { { "dddd", new List<string> { "PowerShell history" } } };
            Check("danger: in shell history", Core.Warnings(d.Find(env.Id), d, hits, t0).Any(x => x.Level == Levels.Danger && x.Text.Contains("PowerShell history")));
            Check("old key", Core.Warnings(d.Find(env.Id), d, null, t0.AddDays(200)).Any(x => x.Text.Contains("200 days old")));
            Check("mood worried", Core.Mood(d, null, 0, t0) == "worried");
            foreach (var k in d.Keys) { k.Seen = true; k.UsedBy = "shop"; k.Fingerprint = k.Id; k.Git = GitStates.Ignored; }
            Check("mood happy", Core.Mood(d, null, 0, t0) == "happy");
        }

        static void NotesGuard()
        {
            var d = NewData();
            Func<string, string> fp = s => s == "q8Zt3LmP0vXc" ? "known" : "x";
            d.Keys.Add(new KeyRecord { Id = "user:A", Fingerprint = "known" });
            Check("notes ok", Core.ValidateNotes(d, fp, "2026-01-05", "image-bridge-desktop-2026-10", "image bridge", "$10/month", "made for the galaxy renders") == null);
            Check("notes: key refused", Core.ValidateNotes(d, fp, null, "label", "used", null, "backup copy " + FakeOpenAi) == Core.KeyInNotesMessage);
            Check("notes: key after = refused", Core.ValidateNotes(d, fp, null, "OPENAI_API_KEY=" + FakeAnthropic) == Core.KeyInNotesMessage);
            Check("notes: known key refused", Core.ValidateNotes(d, fp, null, "it is q8Zt3LmP0vXc") == Core.KeyInNotesMessage);
            Check("notes: bad date", Core.ValidateNotes(d, fp, "10/07/2026") != null);
            Check("clean text", Core.CleanText(" a\u0007b\tc ", false, 10) == "ab c" && Core.CleanText("x\ny", false, 10) == "xy" && Core.CleanText("x\ny", true, 10) == "x\ny");
        }

        static void StoreRoundTrip()
        {
            var d = NewData();
            d.Keys.Add(new KeyRecord { Id = "user:OPENAI_API_KEY", Name = "OPENAI_API_KEY", Scope = Scopes.User, Label = "image-bridge", Present = true });
            var bytes = Store.Encode(d);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            Check("store is encrypted", !text.Contains("OPENAI_API_KEY") && !text.Contains("image-bridge"));
            var back = Store.Decode(bytes);
            Check("store round trip", back.Keys.Single().Label == "image-bridge" && back.Settings.Folders.Single() == @"C:\Projects");
            bytes[bytes.Length - 1] ^= 0xFF;
            bool threw = false;
            try { Store.Decode(bytes); } catch (Exception) { threw = true; }
            Check("tampered store refused", threw);
            var dir = Path.Combine(Path.GetTempPath(), "api-fairy-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var s = Store.Load(dir);
                Check("fresh store has a pepper", s.Data.PepperBytes().Length == 32);
                s.Data.Settings.RotateDays = 90;
                s.Save();
                Check("store reloads", Store.Load(dir).Data.Settings.RotateDays == 90);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        static void Fingerprints()
        {
            var a = new Scanner(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
            var b = new Scanner(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            Check("fingerprint stable", a.Fingerprint(FakeOpenAi) == a.Fingerprint(FakeOpenAi));
            Check("fingerprint short and hex", Regex.IsMatch(a.Fingerprint(FakeOpenAi), "^[0-9a-f]{16}$"));
            Check("fingerprint depends on the secret pepper", a.Fingerprint(FakeOpenAi) != b.Fingerprint(FakeOpenAi));
            Check("fingerprint hides the key", !a.Fingerprint(FakeOpenAi).Contains("proj"));
        }

        // API Fairy promises it has no internet code. Keep it that way.
        static void NoNetworkCode(string root)
        {
            var src = Path.Combine(root, "src");
            if (!Directory.Exists(src)) { Check("source folder found", false); return; }
            var banned = new Regex(@"System\.Net\b|WebClient|HttpClient|WebRequest|TcpClient|UdpClient|Socket\b|WebBrowser|ServicePointManager", RegexOptions.CultureInvariant);
            foreach (var f in Directory.GetFiles(src, "*.cs"))
                Check("no network code in " + Path.GetFileName(f), !banned.IsMatch(File.ReadAllText(f)));
        }
    }
}
