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

        /// <summary>The CoreLib section every declaration binds into until the first
        /// <see cref="Group"/> call. Named rather than repeated: Task 2's adoption asks whether the
        /// current section still IS this one, and a second literal would let the two drift.</summary>
        internal const string DefaultSection = "Settings";

        // The section later declarations bind into, and the group they may inherit a stranded value
        // from. Both change together in Group() and nowhere else.
        private string _currentSection = DefaultSection;
        private string _movedFrom;

        // True when the widget declared LAST failed to bind and therefore added no SettingDef.
        //
        // RequiresRestart() addresses "the most recently declared setting" positionally, as
        // _section.Settings[Count - 1]. Before binds were guarded, a failure threw and the chain
        // never reached the modifier at all; now the chain continues, so without this flag
        // `.Choice(…).RequiresRestart()` would mark whatever was declared BEFORE the failed
        // Choice — a setting that applies immediately, now demanding a restart, with nothing in
        // the log connecting the two.
        private bool _lastDeclarationFailed;

        internal SectionBuilder(ModSection section, ConfigFile file)
        {
            _section = section;
            _file = file;
        }

        // Every widget method binds through here, because ConfigFile.Bind is not the harmless
        // lookup it looks like: it ends in `if (SaveOnConfigSet) Save()`, i.e. a full serialize and
        // an API.ConfigFilesystem write, on the first bind of each key in a session. That write has
        // no exception handling anywhere along it — the Wine filesystem faults this project carries
        // six IL patches for land exactly here.
        //
        // Unguarded, such a fault unwinds out of the widget method, so the consumer's remaining
        // builder chain and its Build() never run and its WHOLE section vanishes from the menu —
        // because one setting could not be written. Bind also casts the existing entry unchecked
        // (`return (ConfigEntry<T>)rawEntry`), so a consumer that declares one key twice with
        // different types gets an InvalidCastException with the same blast radius; naming the key
        // turns that from an unattributed stack trace into a one-line fix.
        //
        // A failed bind yields null, and the caller then registers NO row and hands back a detached
        // handle carrying the declared default. The setting is absent rather than broken: the
        // consumer keeps running on its own default, and the log says which one and why.
        private ConfigEntry<T> BindGuarded<T>(string key, T def, ConfigDescription description)
        {
            try
            {
                return _file.Bind(_currentSection, key, def, description);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[ModSettingsMenu] Could not bind setting '{key}' for '{_section.ModId}'; it is left out of the menu and the mod keeps its own default: {ex}"
                );
                return null;
            }
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
            var entry = BindGuarded(key, def, new ConfigDescription(key));
            if (entry == null)
            {
                handle = new SettingHandle<bool>(def);
                _lastDeclarationFailed = true;
                return this;
            }
            handle = new SettingHandle<bool>(entry);
            _lastDeclarationFailed = false;
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
            var entry = BindGuarded(key, def, new ConfigDescription(key, new AcceptableValueRange<float>(min, max)));
            if (entry == null)
            {
                handle = new SettingHandle<float>(def);
                _lastDeclarationFailed = true;
                return this;
            }
            handle = new SettingHandle<float>(entry);
            _lastDeclarationFailed = false;
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
        /// displayed text + persistence key is <see cref="ChoiceToken.Of(object, System.Type)"/> (the
        /// "token"); Phase 5 localizes via a derived term, falling back to the token. Prefer an enum for
        /// T (self-documenting tokens). Values must have distinct tokens.
        ///
        /// Through ChoiceToken rather than ToString() so a numeric T renders invariantly. A token is the
        /// localization leaf key a consumer writes into its yaml, and ToString() would make that key —
        /// and the stored .cfg value — depend on the machine's decimal separator, so the same
        /// declaration would need a different yaml per host and a stored value would stop matching its
        /// own token list when the locale changed. Enums and strings render identically either way, and
        /// a T that CoreLib cannot convert falls back to ToString(), so this narrows nothing.
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
                tokens[i] = ChoiceToken.Of(values[i], typeof(T));
            // Store a string token (arbitrary T needs no CoreLib converter); validate it stays valid.
            var entry = BindGuarded(key, ChoiceToken.Of(def, typeof(T)), new ConfigDescription(key, new AcceptableValueList<string>(tokens)));
            if (entry == null)
            {
                handle = new SettingHandle<T>(def);
                _lastDeclarationFailed = true;
                return this;
            }
            T FromToken(string t)
            {
                for (int i = 0; i < values.Length; i++)
                    if (tokens[i] == t)
                        return values[i];
                return def; // unknown/removed token → default
            }
            handle = new SettingHandle<T>(entry, FromToken, v => ChoiceToken.Of(v, typeof(T)));
            _lastDeclarationFailed = false;
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
            var entry = BindGuarded(key, def, new ConfigDescription(key, new AcceptableValueRange<int>(min, max)));
            if (entry == null)
            {
                handle = new SettingHandle<int>(def);
                _lastDeclarationFailed = true;
                return this;
            }
            handle = new SettingHandle<int>(entry);
            _lastDeclarationFailed = false;
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
            WarnAboutDefaultsThatWillNotSurvive(key, defaults, editing);
            var entry = BindGuarded(key, declared, new ConfigDescription(key));
            if (entry == null)
            {
                handle = new SettingHandle<string[]>(ListTokenizer.Tokenize(declared).ToArray());
                _lastDeclarationFailed = true;
                return this;
            }
            if (ListAccess.ReconcilesDefaults(editing))
                ReconcileWithDefaults(entry, declared, key, editing);
            // AFTER the reconcile, and against the STORED value: that is what the player will
            // actually open, so it is the honest thing to test.
            //
            // It is NOT a way to catch a failed reconcile write. CoreLib assigns the field before it
            // saves (see ReconcileWithDefaults' own catch below), so a throwing write still leaves
            // the reconciled, non-empty value in memory. Since ReconcilesDefaults is the exact
            // complement of CanAdd, the reconcile always runs at these levels and its result
            // contains every declared token — so this fires exactly when the declaration was empty,
            // same as testing `declared` did. Kept in this form because it asks about the thing
            // being described rather than about an input that usually matches it.
            if (!ListAccess.CanAdd(editing) && ListTokenizer.Tokenize(entry.Value).Count == 0)
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' is {editing} and has no entries — its editor cannot show one or gain one, so the row will refuse to open."
                );
            handle = new SettingHandle<string[]>(entry, s => ListTokenizer.Tokenize(s).ToArray(), v => ListTokenizer.Join(v));
            _lastDeclarationFailed = false;
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
        private static void WarnAboutDefaultsThatWillNotSurvive(string key, string[] defaults, ListEditing editing)
        {
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
                // Matched case-INSENSITIVELY, and the declared spelling is the one kept. A player
                // who hand-edits the .cfg (the only way to touch these entries outside the menu)
                // would otherwise have their entry dropped as "no longer declared" and the declared
                // one appended at the end — losing the position this level exists to let them
                // choose, which is the opposite of what the README promises them.
                foreach (var token in stored)
                {
                    var match = FindIgnoringCase(declaredTokens, token);
                    if (match != null && !reconciled.Contains(match))
                        reconciled.Add(match);
                }
                foreach (var token in declaredTokens)
                {
                    if (!reconciled.Contains(token))
                        reconciled.Add(token);
                }
            }
            else
            {
                // Same second loop as above, without the stored-order pass that seeds it. The two
                // branches differ ONLY in whether the player's order gets a say — not in how
                // membership is built, and not in whether duplicates collapse. A bare AddRange here
                // would keep them, contradicting the duplicate warning WarnAboutDefaultsThatWillNotSurvive
                // emits for exactly these levels.
                foreach (var token in declaredTokens)
                {
                    if (!reconciled.Contains(token))
                        reconciled.Add(token);
                }
            }
            // Compared as TOKENS, not as the joined string: the two differ for a value a player
            // hand-formatted ("Alpha, Beta"), and rewriting that would change their file to say the
            // same thing differently. CoreLib's own setter no-ops on an equal value, so this guard
            // is about not producing a different-looking equal value in the first place.
            //
            // It preserves formatting only while nothing else changes. Once a single token differs,
            // the write goes through Join and normalises the whole value, so a player's spacing goes
            // with it. That is accepted: the alternative is editing their string in place, which
            // needs a formatting model this format does not have.
            if (SameTokens(stored, reconciled))
                return;
            var dropped = new List<string>();
            foreach (var token in stored)
            {
                if (FindIgnoringCase(reconciled, token) == null)
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
            // Logged AFTER the write so the wording can reflect what actually happened — but
            // ALWAYS, including on the failure path. CoreLib assigns the field before it saves, so a
            // failed write still means these entries are gone for this session, and the error above
            // says the next successful save persists that silently. Withholding the list there would
            // announce a pending deletion while hiding what is being deleted, in the one case where
            // the file still holds it and someone could copy it back out.
            if (dropped.Count > 0)
                Debug.LogWarning(
                    $"[ModSettingsMenu] List '{key}' is declared {editing}, so its membership is the mod's: dropped {dropped.Count} stored "
                        + $"entr{(dropped.Count == 1 ? "y" : "ies")} it no longer declares ({string.Join(", ", dropped.ToArray())}). "
                        + "If this list used to be FreeText, or was hand-edited, those were the player's own."
                        + (persisted ? "" : " The write above failed, so the file may still hold them until the next save.")
                );
        }

        // The declared spelling of a token that matches ignoring case, or null. Used so a
        // differently-cased stored entry re-anchors to its declared position instead of being
        // dropped and re-appended.
        private static string FindIgnoringCase(List<string> tokens, string token)
        {
            foreach (var candidate in tokens)
            {
                if (string.Equals(candidate, token, System.StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
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

        /// <summary>
        /// A full-width heading between this section's settings, for grouping a long list of them.
        /// It holds no value: nothing is bound, nothing persists, and there is no handle — which is
        /// why this is the one declaration that cannot fail the way the others can.
        ///
        /// <paramref name="key"/> is a localisation key, resolved as "&lt;ModId&gt;-Config/&lt;key&gt;"
        /// exactly like every other row's label and falling back to the raw key when no term
        /// resolves. Deliberately no literal-text parameter: a readable fallback would hide a
        /// missing term, and a visible raw key is the same diagnosis every other row already gives.
        ///
        /// The row is skipped by navigation because it never enters the screen's menuOptions —
        /// Core Keeper's own answer for the category headings in its key-rebinding screen.
        ///
        /// Under <see cref="SortOptions"/> ByKey/ByLabel a label is a SEGMENT BOUNDARY: the settings
        /// between two labels are sorted among themselves and the labels stay where they were
        /// declared. A label states an order, so a sort that reordered across it would answer the
        /// same question twice.
        /// </summary>
        public SectionBuilder Label(string key)
        {
            _lastDeclarationFailed = false;
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Label,
                    Term = Term(key),
                }
            );
            return this;
        }

        /// <summary>
        /// Starts a group: renders a heading row exactly as <see cref="Label"/> does, AND binds every
        /// declaration after it into the CoreLib section <paramref name="key"/> instead of
        /// <see cref="DefaultSection"/>. Both effects are named in the method name on purpose — the
        /// second one changes the layout of the mod's own .cfg, and a caller must be able to see that
        /// at the call site.
        ///
        /// <paramref name="key"/> is both the loc key of the heading and the section name in the
        /// file, so it must satisfy CoreLib's rules for a section name; an unusable one is refused
        /// whole (no heading, no section change) rather than half-applied.
        ///
        /// <paramref name="movedFrom"/> names the group this group's settings used to live in, for a
        /// consumer that renames one. Without it a rename would leave every value behind under the
        /// old section: CoreLib keeps such a line but reads it under a definition nothing asks for
        /// again. Moving OUT of <see cref="DefaultSection"/> needs no declaration — see the adoption
        /// in BindGuarded, which knows that section is MSM's own history.
        /// </summary>
        public SectionBuilder Group(string key, string movedFrom = null)
        {
            _lastDeclarationFailed = false;
            if (!IsUsableSectionName(key, "group name"))
                return this;
            if (movedFrom != null && !IsUsableSectionName(movedFrom, "movedFrom group"))
                movedFrom = null;
            if (movedFrom == key)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] '{_section.ModId}' declares the group '{key}' as moved from itself; there is nothing to move, so the group is kept and the migration dropped."
                );
                movedFrom = null;
            }
            _currentSection = key;
            _movedFrom = movedFrom;
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.Label,
                    Term = Term(key),
                }
            );
            return this;
        }

        /// <summary>Marks the most-recently-declared setting as requiring a game restart to take effect.
        /// When such a setting is changed in the menu, leaving the Mod settings screen raises CK's own
        /// "restart to apply mod changes" prompt (Cancel/Yes → relaunch). Chain it right after the widget:
        /// <c>.Choice(out h, "key", …).RequiresRestart()</c>. Use for bake-time / load-time settings whose
        /// live value only matters at the next bake/launch (e.g. recipe rewrites).</summary>
        public SectionBuilder RequiresRestart()
        {
            int n = _section.Settings.Count;
            // Refuse rather than reach past a failed declaration: Settings[Count - 1] would be the
            // setting declared BEFORE the one that failed, so this modifier would silently attach
            // to an unrelated value and demand a restart for a change that applies immediately.
            //
            // ⚠️ Checked FIRST, and the order is load-bearing — the two guards below look mutually
            // exclusive and are not. Label() clears this flag, so "the last declaration is a label"
            // and "the last declaration failed" seem unable to hold together. They can: a
            // declaration that fails AFTER a label sets the flag again while adding no SettingDef,
            // leaving that label as Settings[n - 1]. Both guards then match, and only this one names
            // the cause the consumer has to act on — the label is merely what happens to be last.
            if (_lastDeclarationFailed)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] RequiresRestart() ignored for '{_section.ModId}': the setting it follows could not be bound (see the error above), and marking the one before it would be wrong."
                );
                return this;
            }
            // A label is not a setting: it holds no value, so nothing about it can change and
            // nothing could ever require a restart. Refuse rather than mark it — this modifier
            // addresses the last declaration POSITIONALLY, so accepting it here would silently
            // attach a restart demand to a row that can never trigger one, and the consumer would
            // have no way to see that their intended setting never got the flag.
            if (n > 0 && _section.Settings[n - 1].Kind == SettingKind.Label)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] RequiresRestart() ignored for '{_section.ModId}': it follows the label '{_section.Settings[n - 1].Key}', which holds no value and can never change. Chain it directly after the setting it belongs to."
                );
                return this;
            }
            if (n > 0)
                _section.Settings[n - 1].RequiresRestart = true;
            return this;
        }

        public void Build()
        {
            WarnAboutDuplicateKeys();
            ModSettings.Register(_section);
        }

        // Two rows in one section carrying the same key resolve to the SAME loc term
        // (MsmTerms.Label is the single schema for a label and a setting alike), so they render the
        // same text and neither is identifiable from the screen. CoreLib catches nothing here: a
        // label never binds at all, and two settings that bind the same key with the same type get
        // the same entry handed back without complaint.
        //
        // Checked once at Build() rather than in each declaration method, so it catches the
        // collision in both directions — label after setting, and setting after label — from one
        // place instead of six.
        private void WarnAboutDuplicateKeys()
        {
            var seen = new List<string>();
            var duplicated = new List<string>();
            foreach (var def in _section.Settings)
            {
                if (def.Key == null)
                    continue;
                if (seen.Contains(def.Key))
                {
                    if (!duplicated.Contains(def.Key))
                        duplicated.Add(def.Key);
                }
                else
                {
                    seen.Add(def.Key);
                }
            }
            if (duplicated.Count > 0)
                Debug.LogWarning(
                    $"[ModSettingsMenu] '{_section.ModId}' declares {duplicated.Count} key(s) more than once ({string.Join(", ", duplicated.ToArray())}). Each resolves to a single term, so those rows show the same text and cannot be told apart on screen."
                );
        }

        // CoreLib's ConfigDefinition constructor rejects these, and an empty name would write a
        // bare "[]" heading into the .cfg. Checked here rather than left to the bind because a bad
        // name would otherwise fail once PER SETTING in the group, and BindGuarded would drop each
        // of them from the menu separately — a typo in one group name costing every row it holds.
        private static readonly char[] InvalidSectionChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        private bool IsUsableSectionName(string value, string what)
        {
            if (string.IsNullOrEmpty(value))
            {
                Debug.LogWarning($"[ModSettingsMenu] '{_section.ModId}' declared a {what} that is empty; the declaration is ignored.");
                return false;
            }
            if (value != value.Trim())
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] '{_section.ModId}' declared the {what} '{value}' with leading or trailing whitespace, which CoreLib refuses; the declaration is ignored."
                );
                return false;
            }
            int bad = value.IndexOfAny(InvalidSectionChars);
            if (bad >= 0)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] '{_section.ModId}' declared the {what} '{value}', which CoreLib refuses because of the character '{value[bad]}'; the declaration is ignored."
                );
                return false;
            }
            return true;
        }

        private string Term(string key) => MsmTerms.Label(_section.ModId, key);
    }
}
