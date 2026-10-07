using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace ApiFairy
{
    // What API Fairy remembers about one key. There is deliberately no field for the key itself.
    public sealed class KeyRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public string Location { get; set; }
        public string ProviderId { get; set; }
        public string ProviderOverride { get; set; }
        public string Label { get; set; }
        public string UsedBy { get; set; }
        public string Limit { get; set; }
        public string Notes { get; set; }
        public string CreatedOn { get; set; }
        public string FirstSeen { get; set; }
        public string LastChanged { get; set; }
        public string GoneOn { get; set; }
        public string Fingerprint { get; set; }
        public string Git { get; set; }
        public bool ExampleFile { get; set; }
        public bool OneDrive { get; set; }
        public bool Present { get; set; }
        public bool Seen { get; set; }
        public bool Ignored { get; set; }
    }

    public sealed class AppSettings
    {
        public List<string> Folders { get; set; }
        public string FairyMode { get; set; }
        public bool FairyPlaced { get; set; }
        public double FairyLeft { get; set; }
        public double FairyTop { get; set; }
        public int RotateDays { get; set; }
        public bool Welcomed { get; set; }

        public AppSettings()
        {
            Folders = new List<string>();
            FairyMode = Modes.Perch;
            RotateDays = 180;
        }
    }

    public static class Modes
    {
        public const string Perch = "perch";
        public const string PopUp = "popup";
    }

    public sealed class StoreData
    {
        public int Version { get; set; }
        public string Pepper { get; set; }
        public AppSettings Settings { get; set; }
        public List<KeyRecord> Keys { get; set; }

        public StoreData()
        {
            Version = 1;
            Settings = new AppSettings();
            Keys = new List<KeyRecord>();
        }

        public KeyRecord Find(string id)
        {
            return id == null ? null : Keys.FirstOrDefault(k => k.Id == id);
        }

        public byte[] PepperBytes()
        {
            return Convert.FromBase64String(Pepper);
        }
    }

    // Notes are encrypted with Windows DPAPI for the signed-in account, so the file is unreadable
    // to other accounts and on other PCs (for example in a backup or a synced folder).
    public sealed class Store
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("AFAIRY01");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("API Fairy store v1");
        public const string FileName = "fairy.dat";

        public readonly string Dir;
        public StoreData Data { get; private set; }
        public string LoadProblem { get; private set; }

        Store(string dir) { Dir = dir; }

        public static string DefaultDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "API Fairy");
        }

        public string FilePath { get { return Path.Combine(Dir, FileName); } }

        public static Store Load(string dir)
        {
            var s = new Store(dir);
            EnsurePrivateDir(dir);
            if (File.Exists(s.FilePath))
            {
                try
                {
                    s.Data = Decode(File.ReadAllBytes(s.FilePath));
                }
                catch (Exception)
                {
                    // Keep the unreadable file aside instead of overwriting it, then start fresh.
                    var aside = s.FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Move(s.FilePath, aside); } catch (Exception) { }
                    s.LoadProblem = "I couldn't read my notes file, so I started fresh. The old file is kept as " + Path.GetFileName(aside) + ".";
                    s.Data = null;
                }
            }
            if (s.Data == null)
            {
                s.Data = new StoreData();
                var pepper = new byte[32];
                using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(pepper);
                s.Data.Pepper = Convert.ToBase64String(pepper);
                var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects");
                if (Directory.Exists(projects)) s.Data.Settings.Folders.Add(projects);
                s.Save();
            }
            return s;
        }

        public void Save()
        {
            var bytes = Encode(Data);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
            else File.Move(tmp, FilePath);
        }

        static JavaScriptSerializer Json()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = 16 * 1024 * 1024;
            j.RecursionLimit = 16;
            return j;
        }

        public static byte[] Encode(StoreData data)
        {
            var plain = Encoding.UTF8.GetBytes(Json().Serialize(data));
            try
            {
                var sealedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                var all = new byte[Magic.Length + sealedBytes.Length];
                Buffer.BlockCopy(Magic, 0, all, 0, Magic.Length);
                Buffer.BlockCopy(sealedBytes, 0, all, Magic.Length, sealedBytes.Length);
                return all;
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public static StoreData Decode(byte[] all)
        {
            if (all.Length <= Magic.Length || !all.Take(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("not an API Fairy file");
            var sealedBytes = new byte[all.Length - Magic.Length];
            Buffer.BlockCopy(all, Magic.Length, sealedBytes, 0, sealedBytes.Length);
            var plain = ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);
            try { return Sanitize(Json().Deserialize<StoreData>(Encoding.UTF8.GetString(plain))); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        static string Clip(string s, int max)
        {
            if (s == null) return null;
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // Never trust the file blindly: clamp sizes and fill gaps.
        static StoreData Sanitize(StoreData d)
        {
            if (d == null) throw new InvalidDataException("empty");
            byte[] pepper = Convert.FromBase64String(d.Pepper ?? "");
            if (pepper.Length < 32) throw new InvalidDataException("pepper");
            if (d.Settings == null) d.Settings = new AppSettings();
            var st = d.Settings;
            st.Folders = (st.Folders ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && f.Length < 400 && Path.IsPathRooted(f)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
            if (st.FairyMode != Modes.Perch && st.FairyMode != Modes.PopUp) st.FairyMode = Modes.Perch;
            if (double.IsNaN(st.FairyLeft) || double.IsNaN(st.FairyTop) || double.IsInfinity(st.FairyLeft) || double.IsInfinity(st.FairyTop)) st.FairyPlaced = false;
            if (st.RotateDays < 7 || st.RotateDays > 3650) st.RotateDays = 180;
            var seen = new HashSet<string>();
            d.Keys = (d.Keys ?? new List<KeyRecord>()).Where(k => k != null && !string.IsNullOrEmpty(k.Id) && k.Id.Length < 1200 && seen.Add(k.Id)).Take(5000).ToList();
            foreach (var k in d.Keys)
            {
                k.Name = Clip(k.Name ?? "", 200);
                k.Location = Clip(k.Location ?? "", 1000);
                k.Label = Clip(k.Label, 80);
                k.UsedBy = Clip(k.UsedBy, 200);
                k.Limit = Clip(k.Limit, 60);
                k.Notes = Clip(k.Notes, 2000);
                k.CreatedOn = Clip(k.CreatedOn, 10);
                k.Fingerprint = Clip(k.Fingerprint, 32);
                if (k.Scope != Scopes.User && k.Scope != Scopes.System && k.Scope != Scopes.File) k.Scope = Scopes.User;
            }
            return d;
        }

        // The folder is readable only by this Windows account (and SYSTEM).
        static void EnsurePrivateDir(string dir)
        {
            Directory.CreateDirectory(dir);
            try
            {
                var me = WindowsIdentity.GetCurrent().User;
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(dir).SetAccessControl(sec);
            }
            catch (Exception) { /* the default per-user folder permissions still apply */ }
        }
    }
}
