using System.Collections.Generic;
using System.Text;
using CoreLib.Data.Configuration;
using PugMod;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Sticky "this foreign string was once a genuine list" memory, persisted RAW via
    /// API.ConfigFilesystem (mirrors the removed ListOverrideStore's technique, commit 5967604) —
    /// NOT the settings .cfg and NOT a CoreLib ConfigFile (never appears in
    /// AllConfigFilesReadOnly / discovery), so it never shows up as a settings row itself.
    ///
    /// ForeignConfigDiscovery.HeuristicSaysList requires >=2 non-empty tokens, and Discover() runs
    /// fresh on every menu open — so editing a list down to 0 or 1 tokens through this mod's own
    /// drill-in reclassifies the row from List back to Info on the very next open, silently losing
    /// editability for the rest of that entry's life (ADR-002 anticipated this exact instability
    /// returning "with editing"; the format-override toggle that would fully address it is still
    /// out of scope — see docs/specs/2026-07-28-list-widget-editing-design.md §5). Once BuildDef
    /// sees a genuine list for a given entry, marking it here keeps it classified as List even after
    /// an edit drops it below the heuristic's own threshold. Format: ASCII, one id per line
    /// (presence = true; nothing else is ever stored).
    /// </summary>
    internal static class ListKindStore
    {
        private const string FilePath = "ModSettingsMenu/list-kind-memory";
        private static HashSet<string> _cache;

        private static void EnsureLoaded()
        {
            if (_cache != null)
                return;
            _cache = new HashSet<string>();
            if (!API.ConfigFilesystem.FileExists(FilePath))
                return;
            var text = Encoding.ASCII.GetString(API.ConfigFilesystem.Read(FilePath));
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0)
                    _cache.Add(line);
            }
        }

        public static bool WasEverList(string id)
        {
            EnsureLoaded();
            return _cache.Contains(id);
        }

        public static void MarkAsList(string id)
        {
            EnsureLoaded();
            if (!_cache.Add(id))
                return; // already marked — no write needed
            var dir = ConfigFile.GetDirectoryName(FilePath); // reuse CoreLib's path helper
            if (!string.IsNullOrEmpty(dir))
                API.ConfigFilesystem.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var key in _cache)
                sb.Append(key).Append('\n');
            API.ConfigFilesystem.Write(FilePath, Encoding.ASCII.GetBytes(sb.ToString()));
        }
    }
}
