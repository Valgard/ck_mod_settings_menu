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
                if (cf == null || cf.Entries.Count == 0) continue;
                if (ConfigStore.IsOwn(cf)) continue;            // MSM's own + every API-integrated consumer
                if (IsCoreLibInternal(cf)) continue;            // best-effort: hide CoreLib's own config
                var section = BuildSection(cf);
                if (section.Settings.Count > 0) result.Add(section);
            }
            return result;
        }

        private static bool IsCoreLibInternal(ConfigFile cf)
            => OwnerFromPath(cf.ConfigFilePath).Equals("CoreLib", System.StringComparison.OrdinalIgnoreCase);

        // "PlacementPlus/PlacementPlus.cfg" -> "PlacementPlus". The owner's real displayName is
        // private on ConfigFile (reflection is banned), so the path's first segment is the label.
        private static string OwnerFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
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
                OptionSort = OptionSort.ByKey,   // Dictionary order isn't meaningful; key order is stable
            };
            foreach (var kv in cf.Entries)
            {
                var def = BuildDef(kv.Key.Key, kv.Value);
                if (def != null) section.Settings.Add(def);
            }
            return section;
        }

        // Widget-kind inference cascade (first match wins). See the design spec section 5.
        private static SettingDef BuildDef(string key, ConfigEntryBase e)
        {
            var d = new SettingDef
            {
                Key = key,
                Term = key,                 // no foreign loc term -> Loc.T(key, key) falls back to the raw key
                Entry = e,
                Foreign = true,
                RequiresRestart = e.Scope != null && e.Scope.requireReload,
            };

            // 1. View-only, or a server/admin setting this player can't change -> read-only.
            if (IsReadOnly(e.Scope)) { d.Kind = SettingKind.Info; return d; }

            var t = e.SettingType;

            // 2. bool -> Toggle.
            if (t == typeof(bool)) { d.Kind = SettingKind.Toggle; return d; }

            // 3. enum -> Choice over the enum names (Toml serializes an enum as its name, so
            //    Get/SetSerializedValue round-trip these tokens exactly).
            if (t.IsEnum) { d.Kind = SettingKind.Choice; d.Tokens = System.Enum.GetNames(t); return d; }

            var av = e.Description != null ? e.Description.AcceptableValues : null;

            // 4a. int with a handled range -> bounded Stepper (clean integer display; MSM's own path).
            if (t == typeof(int) && TryRange(av, out float imin, out float imax))
            { d.Kind = SettingKind.Stepper; d.Min = imin; d.Max = imax; return d; }

            // 4b. float with a handled range -> Slider (Number display).
            if (t == typeof(float) && TryRange(av, out float fmin, out float fmax))
            {
                d.Kind = SettingKind.Slider; d.Min = fmin; d.Max = fmax;
                d.Step = (fmax - fmin) > 0f ? (fmax - fmin) / 20f : 1f;
                d.Display = SliderDisplay.Number;
                return d;
            }

            // 5. Any other AcceptableValues constraint we don't render editable in v1
            //    (AcceptableValueList, or a range of an unhandled numeric type) -> read-only.
            if (av != null) { d.Kind = SettingKind.Info; return d; }

            // 6. Bare numeric, no constraint -> unbounded Stepper.
            if (t == typeof(int)) { d.Kind = SettingKind.Stepper; d.Unbounded = true; return d; }
            if (t == typeof(float))
            {
                d.Kind = SettingKind.Stepper; d.Unbounded = true;
                float mag = System.Math.Abs((float)System.Convert.ToDouble(e.DefaultValue));
                d.Step = mag < 1f ? 0.05f : 1f;   // heuristic; small defaults step finely
                return d;
            }

            // 7. string and everything else -> read-only.
            d.Kind = SettingKind.Info;
            return d;
        }

        private static bool IsReadOnly(ConfigScope scope)
        {
            if (scope == null) return false;
            if (scope.accessLevel == ConfigAccessLevel.ViewOnly) return true;
            if (scope.accessLevel == ConfigAccessLevel.Client) return false;
            // Server/Admin: Changeable() reads Manager.main.player; at the title screen there is no
            // player, so be conservative (read-only) rather than risk an NRE.
            if (Manager.main == null || Manager.main.player == null) return true;
            return !scope.Changeable();
        }

        private static bool TryRange(AcceptableValueBase av, out float min, out float max)
        {
            if (av is AcceptableValueRange<int> ri) { min = ri.MinValue; max = ri.MaxValue; return true; }
            if (av is AcceptableValueRange<float> rf) { min = rf.MinValue; max = rf.MaxValue; return true; }
            min = 0f; max = 0f; return false;
        }
    }
}
