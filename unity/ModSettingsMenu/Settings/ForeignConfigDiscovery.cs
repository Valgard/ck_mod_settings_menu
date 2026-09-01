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

            // 3c. A closed set of acceptable values -> Choice, cycled by the same widget MSM's own
            //     declared Choice uses. See TryTokens for why one shape is exact and the rest are
            //     reconstructed, and for what happens when the reconstruction cannot be trusted.
            if (av != null && TryTokens(av, t, out string[] tokens))
            {
                d.Kind = SettingKind.Choice;
                d.Tokens = tokens;
                return d;
            }

            // 4. A constraint whose value set could not be established (a range of an unhandled numeric
            //    type, or a list whose tokens did not survive the round trip in TryTokens) -> read-only
            //    Info regardless of scope — there is no editable widget for this shape at all, not just
            //    "not allowed to touch it right now".
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

        /// <summary>The closed set of values a constrained entry accepts, as the tokens a Choice row
        /// cycles — or false when that set cannot be established, which leaves the caller with a
        /// read-only Info row.
        ///
        /// One shape is exact and the rest are reconstructed, and the split is not a judgement about
        /// them: <see cref="AcceptableValueList{T}"/> exposes its values through a generic property, so
        /// they are reachable only where the type argument is known at compile time. For string that
        /// is here. For any other T only reflection could reach them, which the Roslyn sandbox forbids
        /// (docs/ck/sandbox.md), so the fallback reads the human-readable line CoreLib writes into the
        /// .cfg instead — "# Acceptable values: a, b, c".
        ///
        /// That line is documentation, not a serialization format, so the parse checks its own work:
        /// a token counts only if <see cref="TomlTypeConverter"/> can turn it back into a value AND the
        /// constraint answers IsValid for it, and one failure discards the whole set rather than
        /// offering a partial one. Which is what makes reading it safe rather than hopeful — the line
        /// joins on ", ", so a value containing one arrives as fragments, and fragments fail IsValid.
        ///
        /// An enum cannot reach the fallback at all: AcceptableValueList constrains T to
        /// IEquatable&lt;T&gt;, which no enum implements (CS0315), so no mod can restrict one this way.
        /// Enums are Choices already, from their member names, one case earlier.</summary>
        private static bool TryTokens(AcceptableValueBase av, System.Type settingType, out string[] tokens)
        {
            tokens = null;

            if (av is AcceptableValueList<string> exact)
            {
                // Handed through rather than copied: nothing in this mod writes to SettingDef.Tokens,
                // and a copy would only mask it if something ever did. The array is the foreign mod's.
                tokens = exact.AcceptableValues;
                return tokens != null && tokens.Length > 0;
            }

            // AcceptableValueRange describes itself as "# Acceptable value range: From x to y", so the
            // prefix alone tells CoreLib's two shipped constraints apart. A third party's own subclass
            // is judged by whether its description happens to match this one — which is the honest
            // reading of a format that exists for a human opening the .cfg.
            const string prefix = "# Acceptable values: ";
            string description = av.ToDescriptionString();
            if (description == null || !description.StartsWith(prefix, System.StringComparison.Ordinal))
                return false;

            var parts = description.Substring(prefix.Length).Split(',');
            var accepted = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                string token = part.Trim();
                if (token.Length == 0)
                    return false;
                object value;
                try
                {
                    value = TomlTypeConverter.ConvertToValue(token, settingType);
                }
                catch
                {
                    return false; // no converter for this type, or this token is not one of its values
                }
                if (!av.IsValid(value))
                    return false;
                accepted.Add(token);
            }

            if (accepted.Count == 0)
                return false;
            tokens = accepted.ToArray();
            return true;
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
        /// docs/adrs/003-list-widget-editing.md). ListKindStore (see BuildDef) covers
        /// a narrower, different case: an entry ALREADY confirmed as a list staying one after our own
        /// editing shrinks it below this heuristic's own threshold.</summary>
        // Does this comma-separated string look like a LIST of values rather than one piece of
        // prose that happens to contain a comma? Two or more tokens, none of which reads like a
        // sentence fragment or a dotted value.
        //
        // **Changed 2026-08-23.** This used to reject any token over 32 characters. That threshold
        // came from ADR-002 without a derivation, and it turned out to test the wrong property:
        // "This is a long sentence, and another one" splits into tokens of 23 and 15 characters —
        // both under the limit, so prose passed — while a perfectly ordinary long identifier was
        // refused and fell through to a read-only Info row. Length is not what separates a list
        // from prose; INTERNAL WORD COUNT is.
        //
        // The new rule: a token may contain at most one space. Identifiers ("InventoryChest") and
        // two-word names ("Copper Ore") stay lists; anything with more internal spacing reads as
        // prose and is left as an Info row. That is deliberately not airtight — a three-word item
        // name would now be misjudged — but it errs toward NOT offering an editable drill-in, which
        // is the safer direction: a misclassified entry is written back comma-rejoined on commit
        // (see ADR-003's consequences and the roadmap's format-override item).
        //
        // The dot rule is unchanged and separate: it keeps decimals, versions and paths out.
        private const int MaxWhitespacePerToken = 1;

        public static bool HeuristicSaysList(string value)
        {
            var tokens = ListTokenizer.Tokenize(value);
            if (tokens.Count < 2)
                return false;
            foreach (var tok in tokens)
            {
                if (tok.IndexOf('.') >= 0)
                    return false;
                // Any whitespace counts, not just U+0020: a tab or a non-breaking space between two
                // words separates them exactly as visibly, and a foreign config file is hand-edited
                // text. Tokenize already trimmed the ends, so what is left here is internal.
                int gaps = 0;
                foreach (var c in tok)
                {
                    if (char.IsWhiteSpace(c) && ++gaps > MaxWhitespacePerToken)
                        return false;
                }
            }
            return true;
        }
    }
}
