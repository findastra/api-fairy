using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ApiFairy
{
    public static class Scopes
    {
        public const string User = "user";
        public const string System = "system";
        public const string File = "file";
    }

    public static class GitStates
    {
        public const string Ignored = "ignored";
        public const string NotIgnored = "not-ignored";
        public const string Tracked = "tracked";
        public const string NoRepo = "no-repo";
        public const string Unknown = "unknown";
    }

    // One place a key was found. It holds a name, a location and a scrambled fingerprint, never the key.
    public sealed class Sighting
    {
        public string Id;
        public string Name;
        public string Scope;
        public string Location;
        public string ProviderId;
        public string Fingerprint;
        public string Git;
        public bool ExampleFile;
        public bool OneDrive;
    }

    public sealed class ScanResult
    {
        public readonly List<Sighting> Sightings = new List<Sighting>();
        public readonly HashSet<string> CoveredScopes = new HashSet<string>();
        public readonly List<string> CoveredRoots = new List<string>();
        // Fingerprint of a key-like string found in shell history -> which history files hold it.
        public readonly Dictionary<string, List<string>> HistoryHits = new Dictionary<string, List<string>>();
        public int HistoryKeyLines;
        public DateTime At;
    }

    public static class Ids
    {
        public static string ForEnv(string scope, string name) { return scope + ":" + name.ToUpperInvariant(); }
        public static string ForFile(string path, string name) { return Scopes.File + ":" + path.ToLowerInvariant() + "#" + name; }

        public static string NormalizeRoot(string root)
        {
            return Path.GetFullPath(root).TrimEnd('\\') + "\\";
        }

        public static bool IsUnder(string path, string root)
        {
            return path != null && root != null && path.StartsWith(NormalizeRoot(root), StringComparison.OrdinalIgnoreCase);
        }
    }

    // Looks for keys. Values are read into memory only long enough to recognize and fingerprint them,
    // then dropped: they are never stored, logged, shown or handed to the window.
    public sealed class Scanner
    {
        const int MaxDepth = 6;
        const int MaxEntriesPerRoot = 150000;
        const long MaxEnvFileBytes = 512 * 1024;
        const long MaxHistoryBytes = 32L * 1024 * 1024;
        static readonly TimeSpan GitRecheck = TimeSpan.FromMinutes(5);

        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".hg", ".svn", "bin", "obj", "dist", "build", "out", ".next", ".nuxt", ".svelte-kit",
            ".venv", "venv", "__pycache__", ".cache", ".gradle", "target", "Library", "Temp", "Logs", ".idea", ".vs",
            "vendor", "packages", "bower_components", ".terraform", ".turbo", "coverage", ".parcel-cache"
        };

        sealed class FileEntry { public long Length; public DateTime Mtime; public List<Sighting> Found; }
        sealed class GitEntry { public string State; public DateTime CheckedAt; public DateTime FileMtime; }
        sealed class HistEntry { public long Length; public DateTime Mtime; public List<string> Fingerprints; public int Lines; }

        readonly byte[] pepper;
        readonly Dictionary<string, FileEntry> fileCache = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, GitEntry> gitCache = new Dictionary<string, GitEntry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HistEntry> histCache = new Dictionary<string, HistEntry>(StringComparer.OrdinalIgnoreCase);
        List<string> dotenvFiles = new List<string>();
        List<string> walkedRoots = new List<string>();
        string gitExe;
        bool gitSearched;

        public Scanner(byte[] pepper)
        {
            if (pepper == null || pepper.Length < 32) throw new ArgumentException("pepper");
            this.pepper = (byte[])pepper.Clone();
        }

        public string Fingerprint(string value)
        {
            using (var h = new HMACSHA256(pepper))
            {
                var mac = h.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(mac[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public ScanResult Scan(IList<string> roots, bool walk, bool recheckGit)
        {
            var r = new ScanResult();
            r.At = DateTime.UtcNow;
            ScanEnv(Registry.CurrentUser, "Environment", Scopes.User, r);
            ScanEnv(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", Scopes.System, r);

            var wanted = new List<string>();
            foreach (var root in roots ?? new string[0])
            {
                try { if (Directory.Exists(root)) wanted.Add(Ids.NormalizeRoot(root)); } catch (Exception) { }
            }
            if (walk || !SameRoots(wanted, walkedRoots))
            {
                var files = new List<string>();
                foreach (var root in wanted) Walk(root, files);
                dotenvFiles = files;
                walkedRoots = wanted;
                var live = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                foreach (var stale in fileCache.Keys.Where(k => !live.Contains(k)).ToList()) fileCache.Remove(stale);
            }
            r.CoveredRoots.AddRange(walkedRoots);
            foreach (var file in dotenvFiles) ScanFile(file, recheckGit, r);
            ScanHistory(r);
            return r;
        }

        static bool SameRoots(List<string> a, List<string> b)
        {
            return a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));
        }

        void ScanEnv(RegistryKey hive, string subkey, string scope, ScanResult r)
        {
            try
            {
                using (var k = hive.OpenSubKey(subkey, false))
                {
                    if (k != null)
                    {
                        foreach (var name in k.GetValueNames())
                        {
                            if (string.IsNullOrEmpty(name)) continue;
                            var kind = k.GetValueKind(name);
                            if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) continue;
                            var value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (!Providers.IsCandidate(name, value, false)) continue;
                            var v = Providers.Unquote(value);
                            var s = new Sighting();
                            s.Id = Ids.ForEnv(scope, name);
                            s.Name = name;
                            s.Scope = scope;
                            s.Location = "";
                            s.ProviderId = Providers.Detect(name, v).Id;
                            s.Fingerprint = Fingerprint(v);
                            r.Sightings.Add(s);
                        }
                    }
                }
                r.CoveredScopes.Add(scope);
            }
            catch (Exception) { /* unreadable this time: leave the scope uncovered so nothing is marked removed */ }
        }

        static void Walk(string root, List<string> files)
        {
            int budget = MaxEntriesPerRoot;
            var stack = new Stack<KeyValuePair<DirectoryInfo, int>>();
            stack.Push(new KeyValuePair<DirectoryInfo, int>(new DirectoryInfo(root), 0));
            while (stack.Count > 0 && budget > 0)
            {
                var item = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = item.Key.EnumerateFileSystemInfos().ToList(); }
                catch (Exception) { continue; }
                foreach (var e in entries)
                {
                    if (--budget <= 0) break;
                    FileAttributes attrs;
                    try { attrs = e.Attributes; } catch (Exception) { continue; }
                    // Never follow junctions or symlinks: they can loop or lead outside the folder.
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attrs & FileAttributes.Directory) != 0)
                    {
                        if (item.Value < MaxDepth && !SkipDirs.Contains(e.Name)) stack.Push(new KeyValuePair<DirectoryInfo, int>((DirectoryInfo)e, item.Value + 1));
                    }
                    else if (Dotenv.IsDotenvFileName(e.Name))
                    {
                        files.Add(e.FullName);
                    }
                }
            }
        }

        void ScanFile(string path, bool recheckGit, ScanResult r)
        {
            FileInfo fi;
            try
            {
                fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > MaxEnvFileBytes) return;
            }
            catch (Exception) { return; }

            FileEntry entry;
            if (!fileCache.TryGetValue(path, out entry) || entry.Length != fi.Length || entry.Mtime != fi.LastWriteTimeUtc)
            {
                entry = new FileEntry();
                entry.Length = fi.Length;
                entry.Mtime = fi.LastWriteTimeUtc;
                entry.Found = new List<Sighting>();
                bool example = Dotenv.IsExampleFileName(fi.Name);
                string text;
                try { text = ReadShared(path); }
                catch (Exception) { return; }
                foreach (var e in Dotenv.Parse(text))
                {
                    if (!Providers.IsCandidate(e.Name, e.Value, example)) continue;
                    var s = new Sighting();
                    s.Id = Ids.ForFile(path, e.Name);
                    s.Name = e.Name;
                    s.Scope = Scopes.File;
                    s.Location = path;
                    s.ProviderId = Providers.Detect(e.Name, e.Value).Id;
                    s.Fingerprint = Fingerprint(e.Value);
                    s.ExampleFile = example;
                    s.OneDrive = IsInOneDrive(path);
                    entry.Found.Add(s);
                }
                text = null;
                fileCache[path] = entry;
            }
            if (entry.Found.Count == 0) return;
            var git = GitState(path, fi.LastWriteTimeUtc, recheckGit);
            foreach (var s in entry.Found)
            {
                s.Git = git;
                r.Sightings.Add(s);
            }
        }

        static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                return sr.ReadToEnd();
        }

        static bool IsInOneDrive(string path)
        {
            foreach (var v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var dir = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(dir) && Ids.IsUnder(path, dir)) return true;
            }
            return false;
        }

        // ---- git: is this .env file ignored, or already committed? ----

        string GitState(string path, DateTime mtime, bool recheck)
        {
            GitEntry g;
            if (!recheck && gitCache.TryGetValue(path, out g) && g.FileMtime == mtime && DateTime.UtcNow - g.CheckedAt < GitRecheck) return g.State;
            g = new GitEntry();
            g.State = CheckGit(path);
            g.CheckedAt = DateTime.UtcNow;
            g.FileMtime = mtime;
            gitCache[path] = g;
            return g.State;
        }

        string CheckGit(string path)
        {
            var repo = FindRepoRoot(Path.GetDirectoryName(path));
            if (repo == null) return GitStates.NoRepo;
            var exe = FindGit();
            if (exe == null) return GitStates.Unknown;
            var rel = path.Substring(repo.Length).TrimStart('\\').Replace('\\', '/');
            var repoArg = repo.Replace('\\', '/');
            if (RunGit(exe, repoArg, "ls-files --error-unmatch -- \"" + rel + "\"") == 0) return GitStates.Tracked;
            int code = RunGit(exe, repoArg, "check-ignore -q -- \"" + rel + "\"");
            if (code == 0) return GitStates.Ignored;
            if (code == 1) return GitStates.NotIgnored;
            return GitStates.Unknown;
        }

        static string FindRepoRoot(string dir)
        {
            while (!string.IsNullOrEmpty(dir))
            {
                var dotgit = Path.Combine(dir, ".git");
                if (Directory.Exists(dotgit) || File.Exists(dotgit)) return dir.TrimEnd('\\');
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        string FindGit()
        {
            if (gitSearched) return gitExe;
            gitSearched = true;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    if (dir.Trim().Length == 0 || !Path.IsPathRooted(dir.Trim())) continue;
                    var candidate = Path.Combine(dir.Trim().Trim('"'), "git.exe");
                    if (File.Exists(candidate)) { gitExe = candidate; break; }
                }
                catch (Exception) { }
            }
            return gitExe;
        }

        static int RunGit(string exe, string repo, string args)
        {
            var psi = new ProcessStartInfo(exe, "-c core.fsmonitor=false -c core.hooksPath=NUL -C \"" + repo + "\" " + args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.Close();
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch (Exception) { } return -1; }
                    return p.ExitCode;
                }
            }
            catch (Exception) { return -1; }
        }

        // ---- shell history: keys typed into commands get saved there in plain text ----

        public static List<string> HistoryFiles()
        {
            var list = new List<string>();
            try
            {
                var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\PowerShell\PSReadLine");
                if (Directory.Exists(ps)) list.AddRange(Directory.GetFiles(ps, "*_history.txt"));
            }
            catch (Exception) { }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var n in new[] { ".bash_history", ".zsh_history" })
            {
                var p = Path.Combine(home, n);
                if (File.Exists(p)) list.Add(p);
            }
            return list;
        }

        public static string HistoryLabel(string path)
        {
            var n = Path.GetFileName(path);
            if (n.Equals("ConsoleHost_history.txt", StringComparison.OrdinalIgnoreCase)) return "PowerShell history";
            if (n.EndsWith("_history.txt", StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - 12) + " PowerShell history";
            if (n.Equals(".bash_history", StringComparison.OrdinalIgnoreCase)) return "Git Bash history";
            if (n.Equals(".zsh_history", StringComparison.OrdinalIgnoreCase)) return "zsh history";
            return n;
        }

        void ScanHistory(ScanResult r)
        {
            foreach (var path in HistoryFiles())
            {
                FileInfo fi;
                try { fi = new FileInfo(path); if (!fi.Exists || fi.Length > MaxHistoryBytes) continue; }
                catch (Exception) { continue; }
                HistEntry h;
                if (!histCache.TryGetValue(path, out h) || h.Length != fi.Length || h.Mtime != fi.LastWriteTimeUtc)
                {
                    h = new HistEntry();
                    h.Length = fi.Length;
                    h.Mtime = fi.LastWriteTimeUtc;
                    h.Fingerprints = new List<string>();
                    string text;
                    try { text = ReadShared(path); } catch (Exception) { continue; }
                    foreach (var line in text.Split('\n'))
                    {
                        bool hit = false;
                        foreach (var t in Providers.Tokens(line))
                        {
                            if (!Providers.LooksLikeRealKey(t)) continue;
                            hit = true;
                            var fp = Fingerprint(Providers.Unquote(t));
                            if (!h.Fingerprints.Contains(fp)) h.Fingerprints.Add(fp);
                        }
                        if (hit) h.Lines++;
                    }
                    text = null;
                    histCache[path] = h;
                }
                r.HistoryKeyLines += h.Lines;
                var label = HistoryLabel(path);
                foreach (var fp in h.Fingerprints)
                {
                    List<string> where;
                    if (!r.HistoryHits.TryGetValue(fp, out where)) r.HistoryHits[fp] = where = new List<string>();
                    if (!where.Contains(label)) where.Add(label);
                }
            }
        }

        // Rewrites each history file without the lines that hold a key. Only runs when the person asks.
        public static int ScrubHistory()
        {
            int removed = 0;
            foreach (var path in HistoryFiles())
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Length > MaxHistoryBytes) continue;
                    var text = ReadShared(path);
                    var lines = text.Split('\n');
                    var kept = new List<string>(lines.Length);
                    int here = 0;
                    foreach (var line in lines)
                    {
                        if (Providers.TextContainsKey(line)) here++;
                        else kept.Add(line);
                    }
                    if (here == 0) continue;
                    var tmp = path + ".fairy-tmp";
                    File.WriteAllText(tmp, string.Join("\n", kept), new UTF8Encoding(false));
                    File.Replace(tmp, path, null, true);
                    removed += here;
                }
                catch (Exception) { }
            }
            return removed;
        }
    }
}
