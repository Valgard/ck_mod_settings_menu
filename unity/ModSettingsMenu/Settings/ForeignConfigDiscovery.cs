using System.Collections.Generic;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// GMCM-style generic discovery: enumerates every CoreLib ConfigFile created by ANY mod
    /// (ConfigFile.AllConfigFilesReadOnly), skips the ones MSM owns (its consumers + its own
    /// toggle file), and turns each remaining file into a foreign ModSection whose SettingDefs
    /// wrap the live ConfigEntryBase - so the existing SectionBox/SettingWidget render path drives
    /// them unchanged. Widget kind is inferred from SettingType + AcceptableValues + Scope.
    /// Nothing here touches System.IO or reflection-emit (sandbox-clean).
    /// </summary>
    internal static class ForeignConfigDiscovery
    {
        /// <summary>A fresh set of foreign sections for the current menu open. NOT registered in
        /// ModSettings - the screen merges these into its per-open render list.</summary>
        public static List<ModSection> Discover()
        {
            var result = new List<ModSection>();
            foreach (var cf in ConfigFile.AllConfigFilesReadOnly)
            {
                if (cf == null || cf.Entries.Count == 0)
                    continue;
                if (ConfigStore.IsOwn(cf))
                    continue; // MSM's own + every API-integrated consumer
                if (IsCoreLibInternal(cf))
                    continue; // best-effort: hide CoreLib's own config
                var section = BuildSection(cf);
                if (section.Settings.Count > 0)
                    result.Add(section);
            }
            return result;
        }

        private static bool IsCoreLibInternal(ConfigFile cf) => OwnerFromPath(cf.ConfigFilePath).Equals("CoreLib", System.StringComparison.OrdinalIgnoreCase);

        // "PlacementPlus/PlacementPlus.cfg" -> "PlacementPlus". The owner's real displayName is
        // private on ConfigFile (reflection is banned), so the path's first segment is the label.
        private static string OwnerFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "Unknown";
            int slash = path.IndexOfAny(new[] { '/', '\\' });
            return slash > 0 ? path.Substring(0, slash) : path;
        }

        private static ModSection BuildSection(ConfigFile cf)
        {
            var section = new ModSection
            {
                ModId = cf.ConfigFilePath,
                DisplayName = OwnerFromPath(cf.ConfigFilePath),
                Foreign = true,
                OptionSort = OptionSort.ByKey, // Dictionary order isn't meaningful; key order is stable
            };
            foreach (var kv in cf.Entries)
            {
                var def = BuildDef(cf.ConfigFilePath, kv.Key, kv.Value);
                if (def != null)
                    section.Settings.Add(def);
            }
            return section;
        }

        // Widget-kind inference cascade (first match wins). See ADR-001 (the discovery base). Every
        // kind gets its own native widget regardless of read-only-ness — SettingDef.ReadOnly (view-
        // only, or a server/admin setting this player can't change, incl. at the title where there
        // is no player) rides alongside Kind rather than collapsing it to Info; SettingWidget /
        // ListDetailItem check ReadOnly to decide whether the row responds to input, not Kind.
        private static SettingDef BuildDef(string configFilePath, ConfigDefinition definition, ConfigEntryBase e)
        {
            string key = definition.Key;
            var d = new SettingDef
            {
                Key = key,
                Term = key, // no foreign loc term -> Loc.T(key, key) falls back to the raw key
                Entry = e,
                Foreign = true,
                RequiresRestart = e.Scope != null && e.Scope.requireReload,
                ReadOnly = IsReadOnly(e.Scope),
            };

            var t = e.SettingType;

            // 1. bool -> Toggle.
            if (t == typeof(bool))
            {
                d.Kind = SettingKind.Toggle;
                return d;
            }

            // 2. enum -> Choice over the enum names (Toml serializes an enum as its name, so
            //    Get/SetSerializedValue round-trip these tokens exactly).
            if (t.IsEnum)
            {
                d.Kind = SettingKind.Choice;
                d.Tokens = System.Enum.GetNames(t);
                return d;
            }

            var av = e.Description != null ? e.Description.AcceptableValues : null;

            // 3a. int with a handled range -> bounded Stepper (clean integer display; MSM's own path).
            if (t == typeof(int) && TryRange(av, out float imin, out float imax))
            {
                d.Kind = SettingKind.Stepper;
                d.Min = imin;
                d.Max = imax;
                return d;
            }

            // 3b. float with a handled range -> Slider (Number display).
            if (t == typeof(float) && TryRange(av, out float fmin, out float fmax))
            {
                d.Kind = SettingKind.Slider;
                d.Min = fmin;
                d.Max = fmax;
                d.Step = (fmax - fmin) > 0f ? (fmax - fmin) / 20f : 1f;
                d.Display = SliderDisplay.Number;
                return d;
            }

            // 4. Any other AcceptableValues constraint we don't render editable in v1 (AcceptableValueList,
            //    or a range of an unhandled numeric type) -> read-only Info regardless of scope — there is
            //    no editable widget for this shape at all, not just "not allowed to touch it right now".
            if (av != null)
            {
                d.Kind = SettingKind.Info;
                d.ReadOnly = true;
                return d;
            }

            // 5. Bare numeric, no constraint -> unbounded Stepper.
            if (t == typeof(int))
            {
                d.Kind = SettingKind.Stepper;
                d.Unbounded = true;
                return d;
            }
            if (t == typeof(float))
            {
                d.Kind = SettingKind.Stepper;
                d.Unbounded = true;
                float mag = System.Math.Abs((float)System.Convert.ToDouble(e.DefaultValue));
                d.Step = mag < 1f ? 0.05f : 1f; // heuristic; small defaults step finely
                return d;
            }

            // 6. string -> a genuine comma-list routes to the dedicated list widget (drill-in), read-only
            //    or not (ListDetailItem/ListDetailScreen render every row inert when ReadOnly is set); any
            //    other string (prose, single value, empty) falls back to a read-only Info row regardless
            //    of scope — there's no editable widget for free-text prose in this slice (the
            //    format-override toggle that would add one is still out of scope, spec §5). The
            //    classification lives HERE (not a per-render view in ListWidget): the heuristic picks the
            //    WIDGET KIND, on every Discover() call (i.e. every menu open) — so an entry edited down
            //    below the heuristic's own >=2-token threshold would otherwise flip back to Info on the
            //    very next open. ListKindStore remembers every entry BuildDef ever classified as a
            //    genuine list, so that classification sticks even after later edits shrink it to 1 or 0
            //    tokens (see ListKindStore's own doc comment for the full history).
            if (t == typeof(string))
            {
                string sval = e.BoxedValue?.ToString() ?? "";
                string id = configFilePath + "|" + definition.Section + "|" + key;
                bool isList = HeuristicSaysList(sval) || ListKindStore.WasEverList(id);
                if (isList)
                    ListKindStore.MarkAsList(id);
                d.Kind = isList ? SettingKind.List : SettingKind.Info;
                if (!isList)
                    d.ReadOnly = true;
                return d;
            }

            // 7. everything else (unhandled type) -> read-only Info regardless of scope.
            d.Kind = SettingKind.Info;
            d.ReadOnly = true;
            return d;
        }

        private static bool IsReadOnly(ConfigScope scope)
        {
            if (scope == null)
                return false;
            if (scope.accessLevel == ConfigAccessLevel.ViewOnly)
                return true;
            if (scope.accessLevel == ConfigAccessLevel.Client)
                return false;
            // Server/Admin: Changeable() reads Manager.main.player; at the title screen there is no
            // player, so be conservative (read-only) rather than risk an NRE.
            if (Manager.main == null || Manager.main.player == null)
                return true;
            return !scope.Changeable();
        }

        private static bool TryRange(AcceptableValueBase av, out float min, out float max)
        {
            if (av is AcceptableValueRange<int> ri)
            {
                min = ri.MinValue;
                max = ri.MaxValue;
                return true;
            }
            if (av is AcceptableValueRange<float> rf)
            {
                min = rf.MinValue;
                max = rf.MaxValue;
                return true;
            }
            min = 0f;
            max = 0f;
            return false;
        }

        /// <summary>Classifies a foreign string for BuildDef routing: a list iff there are >=2 non-empty
        /// comma tokens and every token is "compact" (<=32 chars, no '.'); a single-token or prose string
        /// is not a list. This picks the WIDGET KIND at discovery (list widget vs. read-only Info). A
        /// genuine misclassification of a foreign mod's own string (prose that happens to look
        /// list-shaped, or vice versa) still has no user recourse — the format-override toggle that
        /// would let a player correct it is explicitly out of scope for this slice (see
        /// docs/specs/2026-07-28-list-widget-editing-design.md §5). ListKindStore (see BuildDef) covers
        /// a narrower, different case: an entry ALREADY confirmed as a list staying one after our own
        /// editing shrinks it below this heuristic's own threshold.</summary>
        public static bool HeuristicSaysList(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            int nonEmpty = 0;
            foreach (var raw in value.Split(','))
            {
                var tok = raw.Trim();
                if (tok.Length == 0)
                    continue;
                if (tok.Length > 32 || tok.IndexOf('.') >= 0)
                    return false;
                nonEmpty++;
            }
            return nonEmpty >= 2;
        }
    }
}
