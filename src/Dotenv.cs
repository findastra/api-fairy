using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ApiFairy
{
    public struct EnvEntry
    {
        public string Name;
        public string Value;
    }

    // A small .env reader: NAME=value lines, optional "export", quotes and trailing comments.
    public static class Dotenv
    {
        static readonly Regex NameRx = Providers.Rx(@"^[A-Za-z_][A-Za-z0-9_.\-]{0,127}$", RegexOptions.None);

        public static List<EnvEntry> Parse(string text)
        {
            var list = new List<EnvEntry>();
            if (string.IsNullOrEmpty(text)) return list;
            if (text[0] == '﻿') text = text.Substring(1);
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line.Substring(7).TrimStart();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var name = line.Substring(0, eq).Trim();
                if (!Providers.SafeMatch(NameRx, name)) continue;
                var rest = line.Substring(eq + 1).Trim();
                string value;
                if (rest.Length > 0 && (rest[0] == '"' || rest[0] == '\''))
                {
                    int close = rest.IndexOf(rest[0], 1);
                    value = close > 0 ? rest.Substring(1, close - 1) : rest.Substring(1);
                }
                else
                {
                    int hash = rest.IndexOf(" #", StringComparison.Ordinal);
                    value = (hash >= 0 ? rest.Substring(0, hash) : rest).Trim();
                }
                var e = new EnvEntry();
                e.Name = name;
                e.Value = value;
                list.Add(e);
            }
            return list;
        }

        public static bool IsDotenvFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var n = fileName.ToLowerInvariant();
            return n == ".env" || n == ".envrc" || n.StartsWith(".env.", StringComparison.Ordinal) || (n.EndsWith(".env", StringComparison.Ordinal) && n.Length > 4);
        }

        // Example files are usually committed on purpose, so a real key in one is a leak waiting to happen.
        public static bool IsExampleFileName(string fileName)
        {
            var n = (fileName ?? "").ToLowerInvariant();
            return n.Contains("example") || n.Contains("sample") || n.Contains("template") || n.Contains(".dist") || n.Contains("defaults") || n.Contains("schema");
        }
    }
}
