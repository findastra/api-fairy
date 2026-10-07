using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ApiFairy
{
    public sealed class Provider
    {
        public readonly string Id;
        public readonly string Name;
        // Only these fixed https pages are ever opened; the window never supplies a URL.
        public readonly string ManageUrl;
        readonly Regex valuePattern;
        readonly Regex namePattern;

        public Provider(string id, string name, string manageUrl, string valuePattern, string namePattern)
        {
            Id = id;
            Name = name;
            ManageUrl = manageUrl;
            this.valuePattern = valuePattern == null ? null : Providers.Rx(valuePattern, RegexOptions.None);
            this.namePattern = namePattern == null ? null : Providers.Rx(namePattern, RegexOptions.IgnoreCase);
        }

        public bool MatchesValue(string value) { return valuePattern != null && Providers.SafeMatch(valuePattern, value); }
        public bool MatchesName(string name) { return namePattern != null && Providers.SafeMatch(namePattern, name); }
    }

    // Recognizes keys by their well-known shapes and by the names people give them.
    public static class Providers
    {
        static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

        internal static Regex Rx(string pattern, RegexOptions options)
        {
            return new Regex(pattern, options | RegexOptions.CultureInvariant, Timeout);
        }

        internal static bool SafeMatch(Regex rx, string text)
        {
            if (text == null) return false;
            try { return rx.IsMatch(text); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        // Order matters: the most specific value shapes come first.
        public static readonly List<Provider> All = new List<Provider>
        {
            new Provider("anthropic", "Anthropic", "https://console.anthropic.com/settings/keys", @"^sk-ant-[a-z]+\d*-[A-Za-z0-9_\-]{20,}$", @"ANTHROPIC|CLAUDE"),
            new Provider("openrouter", "OpenRouter", "https://openrouter.ai/settings/keys", @"^sk-or-v1-[A-Za-z0-9]{20,}$", @"OPENROUTER"),
            new Provider("openai", "OpenAI", "https://platform.openai.com/api-keys", @"^sk-((proj|svcacct|admin|None)-[A-Za-z0-9_\-]{20,}|[A-Za-z0-9]{32,})$", @"OPENAI"),
            new Provider("stripe", "Stripe", "https://dashboard.stripe.com/apikeys", @"^(sk|rk)_(live|test)_[A-Za-z0-9]{10,}$", @"STRIPE"),
            new Provider("elevenlabs", "ElevenLabs", "https://elevenlabs.io/app/settings/api-keys", @"^sk_[a-f0-9]{40,}$", @"ELEVEN"),
            new Provider("github", "GitHub", "https://github.com/settings/tokens", @"^(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})$", @"GITHUB|(^|_)GH_"),
            new Provider("gitlab", "GitLab", "https://gitlab.com/-/user_settings/personal_access_tokens", @"^glpat-[A-Za-z0-9_\-]{20,}$", @"GITLAB"),
            new Provider("gemini", "Google Gemini", "https://aistudio.google.com/apikey", null, @"GEMINI|GOOGLE_AI|GOOGLE_GENAI|GENAI"),
            new Provider("google", "Google Cloud", "https://console.cloud.google.com/apis/credentials", @"^AIza[0-9A-Za-z_\-]{35}$", @"GOOGLE|(^|_)GCP_|FIREBASE|YOUTUBE"),
            new Provider("huggingface", "Hugging Face", "https://huggingface.co/settings/tokens", @"^hf_[A-Za-z0-9]{30,}$", @"HUGGING|(^|_)HF_"),
            new Provider("replicate", "Replicate", "https://replicate.com/account/api-tokens", @"^r8_[A-Za-z0-9]{20,}$", @"REPLICATE"),
            new Provider("fal", "fal", "https://fal.ai/dashboard/keys", @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{32}$", @"(^|_)FAL(_|$)"),
            new Provider("bfl", "Black Forest Labs (Flux)", null, null, @"(^|_)BFL(_|$)|BLACK_FOREST|FLUX"),
            new Provider("groq", "Groq", "https://console.groq.com/keys", @"^gsk_[A-Za-z0-9]{20,}$", @"GROQ"),
            new Provider("xai", "xAI", "https://console.x.ai", @"^xai-[A-Za-z0-9]{20,}$", @"(^|_)XAI(_|$)|GROK"),
            new Provider("perplexity", "Perplexity", "https://www.perplexity.ai/settings/api", @"^pplx-[A-Za-z0-9]{20,}$", @"PERPLEXITY|PPLX"),
            new Provider("mistral", "Mistral", "https://console.mistral.ai/api-keys", null, @"MISTRAL"),
            new Provider("deepseek", "DeepSeek", "https://platform.deepseek.com/api_keys", null, @"DEEPSEEK"),
            new Provider("slack", "Slack", "https://api.slack.com/apps", @"^(xox[abposr]-[A-Za-z0-9\-]{10,}|https://hooks\.slack\.com/services/\S+)$", @"SLACK"),
            new Provider("discord", "Discord", "https://discord.com/developers/applications", @"^https://(ptb\.|canary\.)?discord(app)?\.com/api/webhooks/\S+$", @"DISCORD"),
            new Provider("aws", "Amazon Web Services", "https://console.aws.amazon.com/iam/home#/security_credentials", @"^(AKIA|ASIA)[0-9A-Z]{16}$", @"(^|_)AWS_"),
            new Provider("sendgrid", "SendGrid", "https://app.sendgrid.com/settings/api_keys", @"^SG\.[A-Za-z0-9_\-]{16,}\.[A-Za-z0-9_\-]{16,}$", @"SENDGRID"),
            new Provider("notion", "Notion", "https://www.notion.so/my-integrations", @"^(secret_|ntn_)[A-Za-z0-9]{30,}$", @"NOTION"),
            new Provider("linear", "Linear", "https://linear.app/settings/api", @"^lin_api_[A-Za-z0-9]{20,}$", @"LINEAR"),
            new Provider("cloudflare", "Cloudflare", "https://dash.cloudflare.com/profile/api-tokens", null, @"CLOUDFLARE|(^|_)CF_"),
            new Provider("vercel", "Vercel", "https://vercel.com/account/tokens", null, @"VERCEL"),
            new Provider("supabase", "Supabase", null, @"^sb_(secret|publishable)_[A-Za-z0-9_\-]{20,}$", @"SUPABASE"),
            new Provider("npm", "npm", null, @"^npm_[A-Za-z0-9]{36}$", @"(^|_)NPM_"),
            new Provider("database", "Database password", null, null, @"DATABASE|(^|_)DB_|POSTGRES|MYSQL|MONGO|REDIS"),
        };

        public static readonly Provider Other = new Provider("other", "Unknown provider", null, null, null);

        static readonly Regex SecretName = Rx(@"(^|[_.\-])(API_?KEY|APIKEY|KEY|KEYS|TOKEN|TOKENS|SECRET|SECRETS|PASSWORD|PASSWD|PASS|AUTH|CREDENTIAL|CREDENTIALS|PAT|BEARER|WEBHOOK|DSN|PRIVATE_KEY|ACCESS_KEY|CLIENT_SECRET|SIGNING_KEY)($|[_.\-])", RegexOptions.IgnoreCase);
        static readonly Regex NonSecretSuffix = Rx(@"_(URL|URI|HOST|HOSTNAME|PORT|PATH|FILE|DIR|FOLDER|REGION|ID|NAME|MODEL|VERSION|ENDPOINT|BASE|TYPE|MODE|ENABLED|TIMEOUT|EXPIRY|EXPIRES|TTL|COUNT|LIMIT|SIZE|HEADER)$", RegexOptions.IgnoreCase);
        static readonly Regex CredentialUrl = Rx(@"^[a-z][a-z0-9+.\-]*://[^/\s:@]+:[^/\s@]+@\S+$", RegexOptions.IgnoreCase);
        static readonly Regex PathLike = Rx(@"^([A-Za-z]:[\\/]|\\\\|[/~%.])", RegexOptions.None);
        static readonly Regex Filler = Rx(@"^(x+|\*+|0+|\.+|-+|_+|#+)$", RegexOptions.IgnoreCase);
        static readonly Regex PlaceholderWord = Rx(@"your|changeme|change_me|replace|example|placeholder|dummy|fake|todo|insert|xxxx|<|>|\$\{|\$\(|\.\.\.", RegexOptions.IgnoreCase);
        static readonly Regex TokenChars = Rx(@"^[A-Za-z0-9_\-.+/=:]+$", RegexOptions.None);

        public static Provider ById(string id)
        {
            if (id == null) return null;
            foreach (var p in All) if (p.Id == id) return p;
            return id == Other.Id ? Other : null;
        }

        public static Provider DetectByValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            foreach (var p in All) if (p.MatchesValue(value)) return p;
            return null;
        }

        public static Provider DetectByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var p in All) if (p.MatchesName(name)) return p;
            return null;
        }

        // The value's shape wins over the name, except that a generic Google key named for Gemini is a Gemini key.
        public static Provider Detect(string name, string value)
        {
            var byValue = DetectByValue(value);
            var byName = DetectByName(name);
            if (byValue != null && byName != null && byValue.Id == "google" && byName.Id == "gemini") return byName;
            if (byValue != null) return byValue;
            if (byName != null) return byName;
            if (SafeMatch(CredentialUrl, value)) return ById("database");
            return Other;
        }

        public static string Unquote(string value)
        {
            if (value == null) return null;
            var v = value.Trim();
            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0]) v = v.Substring(1, v.Length - 2).Trim();
            return v;
        }

        public static bool IsPlaceholder(string value)
        {
            var v = Unquote(value);
            if (string.IsNullOrEmpty(v) || v.Length < 8) return true;
            if (SafeMatch(Filler, v) || SafeMatch(PlaceholderWord, v)) return true;
            if (v.All(char.IsDigit)) return true;
            var lower = v.ToLowerInvariant();
            return lower == "true" || lower == "false" || lower == "null" || lower == "undefined";
        }

        public static bool LooksLikeCredentialUrl(string value)
        {
            var v = Unquote(value);
            return !string.IsNullOrEmpty(v) && SafeMatch(CredentialUrl, v);
        }

        // Strict test used for example files, shell history and the notes guard:
        // a known key shape, a URL carrying a password, or a long random-looking token.
        public static bool LooksLikeRealKey(string value)
        {
            var v = Unquote(value);
            if (string.IsNullOrEmpty(v) || v.Length < 16 || v.Length > 4096) return false;
            if (DetectByValue(v) != null) return !SafeMatch(PlaceholderWord, v);
            if (LooksLikeCredentialUrl(v)) return true;
            if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            if (v.Length < 32 || !SafeMatch(TokenChars, v)) return false;
            if (SafeMatch(PathLike, v) || v.Contains("/") && !v.Any(char.IsDigit)) return false;
            bool letters = v.Any(char.IsLetter), digits = v.Any(char.IsDigit);
            if (!letters || !digits) return false;
            if (IsPlaceholder(v)) return false;
            return Entropy(v) >= 3.6;
        }

        public static double Entropy(string s)
        {
            var counts = new Dictionary<char, int>();
            foreach (var c in s) { int n; counts.TryGetValue(c, out n); counts[c] = n + 1; }
            double h = 0;
            foreach (var n in counts.Values) { double p = (double)n / s.Length; h -= p * Math.Log(p, 2); }
            return h;
        }

        public static bool NameLooksSecret(string name) { return SafeMatch(SecretName, name); }
        public static bool NameLooksNonSecret(string name) { return SafeMatch(NonSecretSuffix, name); }

        // Should this NAME=value pair be tracked as a key?
        public static bool IsCandidate(string name, string value, bool strict)
        {
            var v = Unquote(value);
            if (string.IsNullOrEmpty(v)) return false;
            if (strict) return LooksLikeRealKey(v);
            if (DetectByValue(v) != null) return !SafeMatch(PlaceholderWord, v);
            if (LooksLikeCredentialUrl(v)) return true;
            if (IsPlaceholder(v)) return false;
            if (v.Any(char.IsWhiteSpace)) return false;
            if (SafeMatch(PathLike, v)) return false;
            if (NameLooksNonSecret(name)) return false;
            return NameLooksSecret(name);
        }

        // Splits free text (a history line, a note) into the pieces a key could hide in.
        public static IEnumerable<string> Tokens(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (var raw in text.Split(new[] { ' ', '\t', '\r', '\n', '"', '\'', '`', ',', ';', '(', ')', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return raw;
                int eq = raw.IndexOf('=');
                if (eq >= 0 && eq < raw.Length - 1) yield return raw.Substring(eq + 1);
            }
        }

        public static bool TextContainsKey(string text)
        {
            foreach (var t in Tokens(text)) if (LooksLikeRealKey(t)) return true;
            return false;
        }
    }
}
