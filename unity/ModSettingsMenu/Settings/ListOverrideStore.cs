using System.Collections.Generic;
using System.Text;
using CoreLib.Data.Configuration;
using PugMod;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Per-entry "show as list" overrides for discovered foreign string configs. Persisted RAW via
    /// API.ConfigFilesystem (the sandbox-clean path CoreLib uses internally) at a dedicated file —
    /// NOT the settings .cfg and NOT a CoreLib ConfigFile (so it never appears in
    /// AllConfigFilesReadOnly / discovery). Format: ASCII, one "key=0|1" line per override.
    /// </summary>
    internal static class ListOverrideStore
    {
        private const string FilePath = "ModSettingsMenu/list-overrides";
        private static Dictionary<string, bool> _cache;

        private static void EnsureLoaded()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, bool>();
            if (!API.ConfigFilesystem.FileExists(FilePath)) return;
            var text = Encoding.ASCII.GetString(API.ConfigFilesystem.Read(FilePath));
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int eq = line.LastIndexOf('=');            // key may contain no '='; value is trailing 0/1
                if (eq <= 0) continue;
                _cache[line.Substring(0, eq)] = line.Substring(eq + 1) == "1";
            }
        }

        /// <summary>Null = no stored override (caller falls back to the heuristic).</summary>
        public static bool? Get(string key)
        {
            EnsureLoaded();
            return _cache.TryGetValue(key, out var v) ? v : (bool?)null;
        }

        public static void Set(string key, bool listView)
        {
            EnsureLoaded();
            _cache[key] = listView;
            var dir = ConfigFile.GetDirectoryName(FilePath);      // reuse CoreLib's path helper
            if (!string.IsNullOrEmpty(dir)) API.ConfigFilesystem.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var kv in _cache)
                sb.Append(kv.Key).Append('=').Append(kv.Value ? '1' : '0').Append('\n');
            API.ConfigFilesystem.Write(FilePath, Encoding.ASCII.GetBytes(sb.ToString()));
        }
    }
}
