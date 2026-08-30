using System;
using System.Collections.Generic;
using CoreLib.Data.Configuration;
using UnityEngine;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Fluent builder returned by ModSettings.Section(this). Each widget method
    /// binds a CoreLib ConfigEntry (persisted value or default loaded), hands back
    /// a typed SettingHandle via out, and records a SettingDef for the 2b menu.
    /// Build() registers the finished section.
    /// </summary>
    public sealed class SectionBuilder
    {
        private readonly ModSection _section;
        private readonly ConfigFile _file;

        internal SectionBuilder(ModSection section, ConfigFile file)
        {
            _section = section;
            _file = file;
        }

        /// <summary>Optional one-line hint shown under the section heading.</summary>
        public SectionBuilder Hint(string text)
        {
            _section.HintText = text;
            return this;
        }

        /// <summary>How the options in this section are ordered in the menu. Default AsDeclared keeps
        /// the builder-chain order; ByKey/ByLabel sort alphabetically by the raw key / localized label.</summary>
        public SectionBuilder SortOptions(OptionSort mode)
        {
            _section.OptionSort = mode;
            return this;
        }

        public SectionBuilder Toggle(out SettingHandle<bool> handle, string key, bool def)
        {
            var entry = _file.Bind("Settings", key, def, new ConfigDescription(key));
            handle = new SettingHandle<bool>(entry);
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Toggle,
                    Term = Term(key),
                    Entry = entry,
                }
            );
            return this;
        }

        public SectionBuilder Slider(
            out SettingHandle<float> handle,
            string key,
            float min,
            float max,
            float def,
            float step,
            SliderDisplay display = SliderDisplay.Steps
        )
        {
            var entry = _file.Bind("Settings", key, def, new ConfigDescription(key, new AcceptableValueRange<float>(min, max)));
            handle = new SettingHandle<float>(entry);
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Slider,
                    Term = Term(key),
                    Min = min,
                    Max = max,
                    Step = step > 0f ? step : (max - min),
                    Display = display,
                    Entry = entry,
                }
            );
            return this;
        }

        /// <summary>
        /// A discrete choice cycling a fixed, ordered set of values of any type T. The
        /// displayed text + persistence key is value.ToString() (the "token"); Phase 5
        /// localizes via a derived term, falling back to the token. Prefer an enum for T
        /// (self-documenting tokens). Values must have distinct ToString().
        /// </summary>
        public SectionBuilder Choice<T>(out SettingHandle<T> handle, string key, T[] values, T def)
        {
            // Empty/null values would make AcceptableValueList throw a cryptic ArgumentException at
            // bind. Fail gracefully with a clear message and degrade to a single (default) option.
            if (values == null || values.Length == 0)
            {
                UnityEngine.Debug.LogWarning($"[ModSettingsMenu] Choice '{key}' declared with no values — using the default only.");
                values = new[] { def };
            }
            var tokens = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                tokens[i] = values[i].ToString();
            // Store a string token (arbitrary T needs no CoreLib converter); validate it stays valid.
            var entry = _file.Bind("Settings", key, def.ToString(), new ConfigDescription(key, new AcceptableValueList<string>(tokens)));
            T FromToken(string t)
            {
                for (int i = 0; i < values.Length; i++)
                    if (tokens[i] == t)
                        return values[i];
                return def; // unknown/removed token → default
            }
            handle = new SettingHandle<T>(entry, FromToken, v => v.ToString());
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Choice,
                    Term = Term(key),
                    Tokens = tokens,
                    Entry = entry,
                }
            );
            return this;
        }

        public SectionBuilder Stepper(out SettingHandle<int> handle, string key, int min, int max, int def)
        {
            var entry = _file.Bind("Settings", key, def, new ConfigDescription(key, new AcceptableValueRange<int>(min, max)));
            handle = new SettingHandle<int>(entry);
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Stepper,
                    Term = Term(key),
                    Min = min,
                    Max = max,
                    Entry = entry,
                }
            );
            return this;
        }

        /// <summary>
        /// An ordered list of string entries, shown as a compact preview row that drills into a
        /// full-screen editor. <paramref name="editing"/> says what the player may do there; the
        /// default lets them type, add, delete and reorder freely.
        ///
        /// The value is stored as ONE comma-separated string, not a list type: that is the format
        /// <see cref="ListTokenizer"/> already defines for both directions, and the same one a
        /// discovered foreign list arrives in — so the drill-in needs no notion of where a list
        /// came from. Entries therefore cannot contain a comma; one typed into a row is stripped
        /// on commit, and Join strips it here too, so a default declared with one is stored the
        /// way it will read back rather than silently splitting in two.
        ///
        /// A blank entry is not a value: Tokenize drops it on read and Join drops it on write, so
        /// the handle never yields "" and a row left empty in the editor simply does not persist.
        ///
        /// Reading <c>handle.Value</c> decodes the stored string every time (split, list, array),
        /// where the numeric handles are plain field reads. So cache it and refresh on
        /// <c>OnChanged</c> rather than reading it inside a per-tick patch.
        /// </summary>
        public SectionBuilder List(out SettingHandle<string[]> handle, string key, string[] defaults, ListEditing editing = ListEditing.FreeText)
        {
            string declared = ListTokenizer.Join(defaults);
            WarnAboutUnusableDefaults(key, defaults, declared, editing);
            var entry = _file.Bind("Settings", key, declared, new ConfigDescription(key));
            if (ListAccess.ReconcilesDefaults(editing))
                ReconcileWithDefaults(entry, declared, key, editing);
            handle = new SettingHandle<string[]>(entry, s => ListTokenizer.Tokenize(s).ToArray(), v => ListTokenizer.Join(v));
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.List,
                    Term = Term(key),
                    Entry = entry,
                    DeclaredEditing = editing,
                }
            );
            return this;
        }

        // A declaration the player could never use, reported at the moment it is made rather than
        // discovered later as an empty screen. Matches what Choice does for a degenerate value set.
        //
        // The empty case is not merely useless, it is a crash: with no rows and no add row the
        // drill-in's menuOptions is empty, and CK's SelectIndexInDirection (Pug.Other:342744) then
        // calls SelectOptionIndex(DefaultOptionIndex = 0) without a count check and dereferences
        // menuOptions[0]. ListDetailScreen guards its own entry points against that; this warning
        // exists so the CAUSE is named in the log rather than only the effect.
        //
        // Comma and blank cases are warned about separately because they are silent rewrites: the
        // consumer's own constant stops matching its own stored value, and every symptom points
        // away from here.
        private static void WarnAboutUnusableDefaults(string key, string[] defaults, string declared, ListEditing editing)
        {
            if (!ListAccess.CanAdd(editing) && declared.Length == 0)
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' is declared {editing} with no usable defaults — its editor would have no entries and no way to gain one."
                );
            if (defaults == null)
                return;
            // Collected and reported per LIST, not per entry: an array whose elements all carry the
            // separator is one systematic mistake, and N identical lines every launch is how a
            // warning teaches people to scroll past warnings.
            var rewritten = new List<string>();
            var seen = new List<string>();
            var duplicated = new List<string>();
            int blanks = 0;
            foreach (var raw in defaults)
            {
                var token = ListTokenizer.Sanitize(raw);
                if (token.Length == 0)
                {
                    blanks++;
                    continue;
                }
                if (token != raw)
                    rewritten.Add($"\"{raw}\" -> \"{token}\"");
                // Only reported where it changes the outcome. At FreeText the stored value keeps
                // both copies; at the other levels ReconcileWithDefaults collapses them, which is
                // the one of its three rules that would otherwise operate without a trace.
                if (seen.Contains(token))
                    duplicated.Add(token);
                else
                    seen.Add(token);
            }
            if (blanks > 0)
                Debug.LogWarning($"[ModSettingsMenu] List '{key}' declares {blanks} blank or comma-only default(s) — they are dropped and will never appear.");
            if (rewritten.Count > 0)
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' stores {rewritten.Count} default(s) differently than declared, because an entry cannot contain a "
                        + $"comma and is trimmed ({string.Join("; ", rewritten.ToArray())}) — compare against the stored form, not the declared one."
                );
            if (duplicated.Count > 0 && !ListAccess.CanAdd(editing))
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' declares duplicate default(s) ({string.Join(", ", duplicated.ToArray())}) — at {editing} only the "
                        + "first of each is kept."
                );
        }

        // Brings a stored value back in line with what the consumer currently declares, in BOTH
        // directions, for the levels whose entries the player cannot author.
        //
        // Appending alone was wrong, and wrong in a way nothing could correct: a default the
        // consumer REMOVES in a later release stayed in every existing player's value forever — the
        // player has no delete button at these levels and the consumer has no way to reach into the
        // file, so the stored value drifted into the union of everything ever declared. Two players
        // on the same version then held different lists, and the consumer's own code received a
        // token it no longer has a case for.
        //
        // Each axis belongs to whoever can change it, which decides both halves:
        //
        // MEMBERSHIP is the consumer's at both levels, so the declared set wins. ORDER is the
        // player's only where they can actually reorder — at OrderOnly, where it is the entire point
        // of the level. At ReadOnly nobody can, so keeping the stored order would freeze whatever
        // the first launch happened to write and a consumer's later re-ordering would never reach an
        // existing player; there the declared order is the only one that means anything.
        //
        // FreeText is excluded from all of it for the mirror-image reason: the player owns
        // membership too, and this same code would resurrect an entry they deleted on purpose.
        //
        // ⚠️ NARROWING THE LEVEL IS DESTRUCTIVE, and nothing here can soften that. This keys on the
        // level declared THIS launch and has no record of the last one, so a consumer who ships a
        // key as FreeText and later re-declares it OrderOnly turns everything the player authored
        // into "not declared" and deletes it. Same for a player who hand-edits the .cfg of a list
        // they cannot add to. The log says so as plainly as it can; there is no state here to do
        // better with.
        private static void ReconcileWithDefaults(ConfigEntry<string> entry, string declared, string key, ListEditing editing)
        {
            var declaredTokens = ListTokenizer.Tokenize(declared);
            var stored = ListTokenizer.Tokenize(entry.Value);
            var reconciled = new List<string>();
            if (ListAccess.CanReorder(editing))
            {
                foreach (var token in stored)
                {
                    if (declaredTokens.Contains(token) && !reconciled.Contains(token))
                        reconciled.Add(token);
                }
                foreach (var token in declaredTokens)
                {
                    if (!reconciled.Contains(token))
                        reconciled.Add(token);
                }
            }
            else
            {
                reconciled.AddRange(declaredTokens);
            }
            // Compared as TOKENS, not as the joined string: the two differ for a value a player
            // hand-formatted ("Alpha, Beta"), and rewriting that would change their file to say the
            // same thing differently. CoreLib's own setter no-ops on an equal value, so this guard
            // is about not producing a different-looking equal value in the first place.
            if (SameTokens(stored, reconciled))
                return;
            var dropped = new List<string>();
            foreach (var token in stored)
            {
                if (!reconciled.Contains(token))
                    dropped.Add(token);
            }
            // This is the extra write MSM makes on top of Bind's own, and only it is guarded — Bind
            // itself ends in Save() (ConfigFile.cs) and is as unguarded here as it is in Toggle,
            // Slider, Choice and Stepper. So this catch does NOT make List() fault-proof; it keeps
            // MSM's own convenience write from being the thing that takes the consumer's remaining
            // builder chain and its Build() down with it.
            //
            // Only the filesystem can actually reach it: CoreLib wraps every SettingChanged
            // subscriber in its own try/catch (ConfigFile.cs), so a foreign handler throwing is
            // logged there, not propagated. Save() runs before those handlers and has no guard.
            //
            // Reported as an ERROR with the full exception, matching what the section-reset path
            // does for the identical failure: a Wine IOException and a type fault produce
            // indistinguishable one-liners from ex.Message alone, and this is the kind of thing that
            // arrives as a user's Player.log with no way to ask a follow-up question.
            bool persisted = true;
            try
            {
                entry.Value = ListTokenizer.Join(reconciled);
            }
            catch (Exception ex)
            {
                persisted = false;
                Debug.LogError(
                    $"[ModSettingsMenu] Could not persist the reconciled list '{key}'. This session uses the reconciled value — CoreLib assigns the "
                        + $"field before it saves — while the file may still hold the old one or be partly written, and the next successful save of "
                        + $"this mod's config will persist it with no further warning: {ex}"
                );
            }
            // Logged AFTER the write, so the log describes what happened rather than what was about
            // to be attempted — the two used to contradict each other on a failed write.
            if (dropped.Count > 0 && persisted)
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' is declared {editing}, so its membership is the mod's: dropped {dropped.Count} stored "
                        + $"entr{(dropped.Count == 1 ? "y" : "ies")} it no longer declares ({string.Join(", ", dropped.ToArray())}). "
                        + "If this list used to be FreeText, or was hand-edited, those were the player's own."
                );
        }

        private static bool SameTokens(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        /// <summary>Marks the most-recently-declared setting as requiring a game restart to take effect.
        /// When such a setting is changed in the menu, leaving the Mod settings screen raises CK's own
        /// "restart to apply mod changes" prompt (Cancel/Yes → relaunch). Chain it right after the widget:
        /// <c>.Choice(out h, "key", …).RequiresRestart()</c>. Use for bake-time / load-time settings whose
        /// live value only matters at the next bake/launch (e.g. recipe rewrites).</summary>
        public SectionBuilder RequiresRestart()
        {
            int n = _section.Settings.Count;
            if (n > 0)
                _section.Settings[n - 1].RequiresRestart = true;
            return this;
        }

        public void Build() => ModSettings.Register(_section);

        private string Term(string key) => $"{_section.ModId}-Config/{key}";
    }
}
