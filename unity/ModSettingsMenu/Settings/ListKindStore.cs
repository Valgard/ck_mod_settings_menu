using System;
using System.Collections.Generic;
using System.Text;
using CoreLib.Data.Configuration;
using PugMod;
using UnityEngine;

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
    /// an edit drops it below the heuristic's own threshold. Format: UTF-8, one id per line
    /// (presence = true; nothing else is ever stored). UTF-8 rather than ASCII: an id is normally a
    /// plain "ModId/Section/Key" string, but it is built from a foreign mod's own section/key names
    /// (ForeignConfigDiscovery), which this store does not control — ASCII would silently mangle any
    /// non-ASCII byte to '?' on write, risking two distinct foreign ids colliding onto the same
    /// stored line. UTF-8 is a strict superset for the ASCII ids every entry has had so far, so this
    /// is not a format break.
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
            try
            {
                if (!API.ConfigFilesystem.FileExists(FilePath))
                    return;
                var text = Encoding.UTF8.GetString(API.ConfigFilesystem.Read(FilePath));
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length > 0)
                        _cache.Add(line);
                }
            }
            catch (Exception ex)
            {
                // A read failure here is low-stakes by design: this store is sticky MEMORY, not the
                // list's own data — losing it only means an entry edited below the ForeignConfigDiscovery
                // heuristic's 2-token threshold reclassifies to Info on the next open (the exact,
                // already-documented ADR-002 limitation this store exists to soften), never data loss on
                // the setting itself. Falling back to an empty cache (as if the file never existed) is
                // therefore the correct, safe degradation — just log it so a real, recurring filesystem
                // fault (Wine, disk full) doesn't go unnoticed.
                Debug.LogWarning("[ModSettingsMenu] ListKindStore failed to load '" + FilePath + "': " + ex.Message);
                _cache.Clear();
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
            try
            {
                var dir = ConfigFile.GetDirectoryName(FilePath); // reuse CoreLib's path helper
                if (!string.IsNullOrEmpty(dir))
                    API.ConfigFilesystem.CreateDirectory(dir);
                var sb = new StringBuilder();
                foreach (var key in _cache)
                    sb.Append(key).Append('\n');
                API.ConfigFilesystem.Write(FilePath, Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch (Exception ex)
            {
                // A write failure here means this id's "was a genuine list" memory doesn't persist past
                // this session (same low-stakes limitation as the read side above) — but _cache.Add(id)
                // already ran, so THIS session still treats it correctly; only a future launch would see
                // the un-flagged behavior. Not worth making the store read-only over (unlike the
                // possession-ledger family's stores, nothing here can silently lose already-stored data —
                // this file only ever grows a set of ids). Log so a recurring fault is visible.
                Debug.LogWarning("[ModSettingsMenu] ListKindStore failed to save '" + FilePath + "': " + ex.Message);
            }
        }
    }
}
