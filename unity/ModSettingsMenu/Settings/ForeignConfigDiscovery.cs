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
                // Dictionary order isn't meaningful; key order is stable — within a group, since
                // SortWithinSegments treats a section heading as a segment boundary and sorts only
                // what lies between two of them, never across one.
                OptionSort = OptionSort.ByKey,
            };
            // Snapshot before walking. CoreLib's own source says it cannot lock this dictionary, and a
            // mod that binds a setting while the screen builds would throw from MoveNext — which
            // happens between iterations and so escapes the per-entry guard below entirely.
            List<KeyValuePair<ConfigDefinition, ConfigEntryBase>> entries;
            try
            {
                entries = new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>(cf.Entries);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[ModSettingsMenu] reading the entries of '{cf.ConfigFilePath}' threw — omitting that mod: {ex}");
                return section;
            }

            // Composed after the guard above, not in the initializer: Populate() has none of its
            // own, so anything thrown before that try reaches it and empties the settings screen
            // for every mod at once. Compose is total today (see GmcmTerms), and a null HeadingTerm
            // degrades to DisplayName — so the early return above stays correct either way.
            section.HeadingTerm = GmcmTerms.File(cf.ConfigFilePath);

            // Grouped by the section each entry declares, so a mod that structured its own .cfg has
            // that structure on screen. Ordinal keys, because CoreLib's sections are case-sensitive
            // and two that differ only in case are two sections; the DISPLAY order below is
            // case-insensitive, because that is a reading order and not an identity.
            var groups = new Dictionary<string, List<SettingDef>>(System.StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var kv in entries)
            {
                SettingDef def;
                try
                {
                    def = BuildDef(cf.ConfigFilePath, kv.Key, kv.Value);
                }
                catch (System.Exception ex)
                {
                    // AcceptableValueBase is a public abstract class, so ToDescriptionString() and
                    // IsValid() may be a third party's override — TryTokens calls both, on ANY av.
                    // Classification did already read virtual members before (TryRange takes MinValue /
                    // MaxValue, both virtual), but only after an `is AcceptableValueRange<int>` match,
                    // so never on an arbitrary foreign class. That is what changed, and this guard came
                    // with it. One bad entry costs its own row; without it,
                    // the throw would leave Populate() and empty the screen for every mod at once.
                    UnityEngine.Debug.LogError(
                        $"[ModSettingsMenu] building the row for '{kv.Key.Key}' from '{cf.ConfigFilePath}' threw — omitting that row: {ex}"
                    );
                    continue;
                }
                if (def == null)
                    continue;
                string sectionName = kv.Key.Section ?? "";
                if (!groups.TryGetValue(sectionName, out var rows))
                {
                    rows = new List<SettingDef>();
                    groups[sectionName] = rows;
                    order.Add(sectionName);
                }
                rows.Add(def);
            }

            order.Sort(System.StringComparer.OrdinalIgnoreCase);
            // One section is the common case, and its heading would sit directly under the box
            // heading naming the same mod — so it is left out. A CONSUMER's single group is kept, in
            // contrast: there the group is declared, and a declaration is honoured. Discovery infers.
            bool withHeadings = order.Count > 1;
            string owner = OwnerFromPath(cf.ConfigFilePath);
            foreach (var sectionName in order)
            {
                // CoreLib's Reload files every line before the file's first [Header] under
                // currentSection = "" — reachable here, and not the same as "no sections at all"
                // (that case never reaches this branch, since a single section leaves withHeadings
                // false). Naming a heading for it would run Label()'s own chain on an empty key:
                // MsmTerms and GmcmTerms both compose a term from it, neither term is one an author
                // ever ships a translation for, and the raw-key fallback is empty too — Loc.TFirstOf
                // treats an empty fallback as no fallback at all — so the row would land on
                // SettingDef.Label()'s "(unnamed)" placeholder. A heading that names nothing, sitting
                // above rows that do have names, is worse than no heading — so this group gets none.
                // It still counts toward withHeadings above: a reader genuinely sees two areas
                // whether or not the first one is captioned, and it sorts first regardless ("" is
                // ordinal-least), so leaving its heading out costs nothing but the caption.
                if (withHeadings && sectionName.Length > 0)
                    section.Settings.Add(HeadingFor(cf.ConfigFilePath, owner, sectionName));
                section.Settings.AddRange(groups[sectionName]);
            }
            return section;
        }

        // A heading carries no value, so its SettingDef has no Entry — the one kind of def that
        // does not (see SettingKind.Label). It needs no new resolution code: Term / GmcmTerm / Key
        // are exactly the three stages SettingDef.Label() already tries, so a section heading reads
        // under MSM's schema, then GMCM's, then as the raw section name from the file.
        private static SettingDef HeadingFor(string configFilePath, string owner, string sectionName) =>
            new SettingDef
            {
                Key = sectionName,
                Kind = SettingKind.Label,
                Term = MsmTerms.Label(owner, sectionName),
                GmcmTerm = GmcmTerms.Section(configFilePath, sectionName),
                Foreign = true,
            };

        // Widget-kind inference cascade (first match wins). See ADR-001 (the discovery base). Every
        // kind gets its own native widget regardless of read-only-ness — SettingDef.ReadOnly (view-
        // only, or a server/admin setting this player can't change, incl. at the title where there
        // is no player) rides alongside Kind rather than collapsing it to Info; SettingWidget /
        // ListDetailItem check ReadOnly to decide whether the row responds to input, not Kind.
        private static SettingDef BuildDef(string configFilePath, ConfigDefinition definition, ConfigEntryBase e)
        {
            string key = definition.Key;
            // Identifies this entry across menu opens — used by ListKindStore below, and to keep a
            // classification warning from repeating on every open.
            string id = configFilePath + "|" + definition.Section + "|" + key;
            var d = new SettingDef
            {
                Key = key,
                // Two terms are tried for the label before the raw key is shown (SettingDef.Label):
                // MSM's own schema, so an author who never took the dependency can still be read by
                // name here, then GMCM's, so a mod already carrying ITS terms is read rather than
                // ignored. The third field feeds ValueLabel instead — a Choice's per-option text.
                //
                // MSM's schema takes an OWNER, and here that owner is the config file's first path
                // segment, because a ConfigFile does not expose the owning mod's name (see
                // OwnerFromPath above; reflection is banned). The published schema names
                // Metadata.name, so the two agree exactly while a mod follows CoreLib's own
                // "<ModName>/<file>.cfg" convention — which is what ConfigStore.ForMod writes and
                // what every mod surveyed does — and diverge for one that names its folder something
                // else. Such a mod's own MSM-schema terms would not be found; its GMCM ones still
                // are, since those are composed from the same path.
                //
                // All three are assigned unconditionally. Not because it is free — each Compose
                // allocates, and the value prefix is read only for a Choice — but because Kind is
                // not known at construction, and the alternative is repeating the assignment down
                // this cascade's nine returns.
                Term = MsmTerms.Label(OwnerFromPath(configFilePath), key),
                GmcmTerm = GmcmTerms.Label(configFilePath, definition.Section, key),
                GmcmValueTermPrefix = GmcmTerms.ValueBase(configFilePath, definition.Section, key),
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

            // 2. enum -> Choice over the enum names. Toml renders an enum as its name and parses one
            //    back, and Enum.ToString() is the same name — so these tokens survive both the widget's
            //    read (ChoiceToken.Of, which names enums explicitly for exactly this reason) and its
            //    converted write, with no per-type code path. This
            //    case returns before `av` is read at all, so an enum never reaches TryTokens whatever
            //    constraint it carries — that is the reason that holds. (AcceptableValueList could not
            //    hold one anyway, its T being IEquatable, which no enum satisfies. But a third party
            //    may attach any OTHER AcceptableValueBase subclass to an enum entry, so that is the
            //    weaker of the two arguments and not the one to lean on.)
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
            //     declared Choice uses. See TryTokens for why most sets are read exactly and the rest
            //     reconstructed, and for what happens when the reconstruction cannot be trusted.
            if (av != null && TryTokens(av, e, id, out string[] tokens))
            {
                d.Kind = SettingKind.Choice;
                d.Tokens = tokens;
                return d;
            }

            // 4. A constraint whose value set could not be established -> read-only Info regardless of
            //    scope: a range of an unhandled numeric type, a constraint whose description this file
            //    does not recognise (a third party's own AcceptableValueBase subclass), or a list whose
            //    tokens did not survive TryTokens. The first two are the designed route and silent; the
            //    third is a degradation and says so in the log. Either way there is no editable widget
            //    for this shape at all, not just "not allowed to touch it right now".
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
        /// Most sets are read exactly and the rest are reconstructed, and the split is about how many
        /// types a pattern-match can name: <see cref="AcceptableValueList{T}"/> exposes its values
        /// through a generic property, so `av is AcceptableValueList&lt;X&gt;` reaches them for any X
        /// written down — which ReadExactValues does for every type <see cref="TomlTypeConverter"/> has a
        /// converter for. That set is not a sample of a larger one: a converter is a PRECONDITION, and
        /// enforced earlier than here, since CoreLib's own ConfigFile.Bind throws for a T it cannot
        /// convert. An entry whose type is outside that table cannot exist to reach this file at all.
        ///
        /// What is left over is a type someone registered through TomlTypeConverter.AddConverter — in
        /// the table at runtime, absent from the cascade, which is written against the shipped table.
        /// For that the fallback parses ToDescriptionString() — the same human-readable line CoreLib
        /// writes into the .cfg: "# Acceptable values: a, b, c". Nothing checks the cascade against that
        /// table (CoreLib's TomlTypeConverter), and nothing needs to: a converter added there and not
        /// here falls to the reconstruction instead of being read exactly, which is a loss of precision
        /// rather than a break — so this correspondence goes imprecise with age, never wrong.
        ///
        /// Reading the property through PugMod's own `API.Reflection` was the obvious alternative to
        /// both, and it does not work — measured in game, not reasoned about (MSM-27, since closed).
        /// The source compiles and the mod loads (`safetyCheck=True`), because `PugMod.MemberInfo` and
        /// `API.Reflection` are on none of the sandbox's deny lists. The refusal is a second, later gate:
        /// `ModAPIReflection.GetValue` runs `InvokeChecker.CheckType` on the member's DECLARING type
        /// (Pug.Other:392657), which admits only assemblies named Pug/Unity/SpriteInstancing/I2/Rewired
        /// — and CoreLib's is named `CoreLib`. It throws `Not allowed to access AcceptableValues`. A
        /// non-generic control on the same run (`AcceptableValueBase.ValueType`) was refused too, so the
        /// type parameter was never the obstacle; the assembly is. See docs/ck/sandbox.md.
        ///
        /// That line is documentation, not a serialization format, so the parse checks its own work at
        /// two levels. Per token: it counts only if <see cref="TomlTypeConverter"/> can turn it back
        /// into a value AND the constraint answers IsValid for it, and one failure discards the whole
        /// set rather than offering a partial one. Per set: no value may repeat, and the result must
        /// contain the value the entry currently holds.
        ///
        /// None of the three is sufficient alone, and it is worth knowing why rather than trusting the
        /// count. The per-token pair misses a split whose fragments happen to be acceptable values.
        /// The held-value check misses a split that left the held value intact as one of those
        /// fragments. The duplicate check is what covers that last case, because a description is a
        /// join over a set and a faithful reading of one cannot repeat itself. Together they are a
        /// strong filter, not a proof: this reads a human-readable line. The real guarantee is the exact
        /// path above — this is what is left where that path cannot reach.
        ///
        /// An enum cannot reach the fallback at all: AcceptableValueList constrains T to
        /// IEquatable&lt;T&gt;, which no enum implements (CS0315), so no mod can restrict one this way.
        /// Enums are Choices already, from their member names, one case earlier.</summary>
        private static bool TryTokens(AcceptableValueBase av, ConfigEntryBase e, string id, out string[] tokens)
        {
            tokens = null;
            var settingType = e.SettingType;

            var exact = ReadExactValues(av, out System.Array values);
            // Reported rather than passed over in silence, because there is no honest shape here at all:
            // AcceptableValueList's own constructor rejects both null and an empty array, so anything
            // arriving this way came from a subclass whose `virtual AcceptableValues` answers abnormally
            // — the same dishonesty the held-value check below treats as a degradation. It also used to
            // be audible: for every type but string this fell to the parse, where an empty array
            // described itself as a blank entry and a null threw out of CoreLib into BuildSection's
            // guard. Going exact must not cost the foreign author that signal, since the row they see is
            // a read-only one either way.
            if (exact == ExactRead.Unusable)
                return Degraded(id, values == null ? "its constraint reports no list of values at all" : "the list of values it accepts is empty");

            if (exact == ExactRead.Usable)
            {
                // Rendered into a NEW array, never handed through. `AcceptableValues` is the foreign
                // mod's live constraint array, and SettingDef.Tokens is a public field on a def that a
                // public surface does reach: SettingWidget.Section → ModSection.Settings → Tokens, all
                // public, for as long as the screen is built. MSM is a framework other mods reference,
                // so "nothing writes into it" is a property of today's code rather than something this
                // can rely on — and a write there would edit another mod's declared value set.
                var rendered = new string[values.Length];
                for (int i = 0; i < rendered.Length; i++)
                {
                    // A blank option is indistinguishable from an unset row, and cycling onto one writes
                    // it into what, for a real mod, is its own config file. A null is worse — it never
                    // matches the row's own read, so the row can never settle on it: it snaps to the
                    // first token instead, and if the null IS the first token nothing moves at all.
                    // Whitespace counts as blank, because the reconstruction trims before it measures
                    // and the two paths have to refuse the same values, not merely similar ones.
                    string token = ChoiceToken.Of(values.GetValue(i), settingType);
                    if (string.IsNullOrWhiteSpace(token))
                        return Degraded(id, "one of the values it accepts is blank");
                    rendered[i] = token;
                }
                // Reads as a tautology — Clamp guarantees the held value is acceptable — but only while
                // AcceptableValues answers honestly, and it is `virtual`: a subclass may return an array
                // that its own Clamp and IsValid never consult. Without this the row would read idx < 0,
                // snap to the first token, and overwrite a foreign mod's value on the first keypress. A
                // check that cannot reject an honest set costs nothing to keep.
                if (System.Array.IndexOf(rendered, ChoiceToken.Of(e)) < 0)
                    return Degraded(id, "the values it accepts do not include the one it currently holds");
                // Note where this does NOT go on to: a recognised set that failed either check above
                // returned rather than falling through to the reconstruction. The description would
                // hand back the very same values, and the parse could accept what the exact read
                // refused — one entry judged twice, by the weaker of the two tests.
                tokens = rendered;
                return true;
            }

            // AcceptableValueRange describes itself as "# Acceptable value range: From x to y", so the
            // prefix alone tells CoreLib's two shipped constraints apart. A third party's own subclass
            // is judged by whether its description happens to match this one — which is the honest
            // reading of a format that exists for a human opening the .cfg. Not a degradation, so it is
            // silent: this is the designed route for every unhandled range, the RangeDouble fixture
            // included, and a warning here would fire on a healthy config at every menu open.
            const string prefix = "# Acceptable values: ";
            string description = av.ToDescriptionString();
            if (description == null || !description.StartsWith(prefix, System.StringComparison.Ordinal))
                return false;

            // A type with no converter is a shape MSM cannot reconstruct rather than a failure, and
            // asking once here leaves the catch below with exactly one meaning: this token did not parse.
            if (!TomlTypeConverter.CanConvert(settingType))
                return false;

            var parts = description.Substring(prefix.Length).Split(',');
            var accepted = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                string token = part.Trim();
                if (token.Length == 0)
                    return Degraded(id, "its list of values has a blank entry");
                object value;
                try
                {
                    value = TomlTypeConverter.ConvertToValue(token, settingType);
                }
                catch (System.Exception ex)
                {
                    // Narrow by construction, not by filter: the type is known convertible, so what is
                    // left is this token, a foreign converter registered through
                    // TomlTypeConverter.AddConverter, or a genuine fault worth seeing named.
                    // The setting's type is deliberately not named here. Type.Name is inherited from
                    // System.Reflection.MemberInfo, so reading it emits a call into a denied namespace
                    // and the mod fails the load-time security verification — while System.Type itself,
                    // typeof(...) and Type.IsEnum are all fine. The Unity build cannot catch this: it
                    // compiles against the real assemblies, and the check runs in the game.
                    // The whole exception, not just its message: the foreign-converter case is somebody
                    // else's bug, and a bug reported without a stack is a rumour. The common case is a
                    // FormatException whose ToString() is three lines.
                    return Degraded(id, $"\"{token}\" did not read back as the type the setting stores ({ex})");
                }
                if (!av.IsValid(value))
                    return Degraded(id, $"\"{token}\" read back, but the setting's own constraint rejects it");
                // The VALUE's canonical token, not the fragment that produced it. The description is
                // written in the current culture and read back invariantly, so a token taken verbatim
                // from it would not survive the widget's own round trip. Converting and re-rendering is
                // free here — the conversion happened one line up, for the validation. It also makes the
                // duplicate check below see two spellings of one value (0.5 and 0.50) as the repeat
                // they are.
                string rendered = ChoiceToken.Of(value, settingType);
                // Blankness is tested TWICE and on two different strings, which looks redundant and is
                // not. Above, on the fragment, so the message can name what the description actually
                // said. Here, on what will be stored — a different string since the line above, and the
                // one the exact path refuses at its own blank check. Without this the two paths would
                // refuse merely similar values rather than the same ones: `\t` is a two-character
                // fragment that passes the length test and unescapes into whitespace.
                if (string.IsNullOrWhiteSpace(rendered))
                    return Degraded(id, $"\"{token}\" reads back as a blank value");
                accepted.Add(rendered);
            }

            // Two set-level checks, because the per-token ones cannot see a split. The reason they
            // cannot is worth naming: ToDescriptionString() renders with x.ToString() and no format
            // provider while the FLOAT converters back are pinned to InvariantInfo, so on a comma-decimal
            // culture a set of (0.5, 5, 0) is described as "0,5, 5, 0" and splits into "0", "5", "5",
            // "0" — fragments that each convert AND validate, because 0 and 5 really are members.
            var reconstructed = accepted.ToArray();

            // 1. No value may repeat. A description is built by joining a set, so a faithful reading
            //    has no duplicates; a split produces them almost by construction (above, "5" twice).
            //    A constraint that genuinely lists the same value twice is refused too, which costs it
            //    an Info row and is the right trade for catching this.
            for (int i = 1; i < reconstructed.Length; i++)
                if (System.Array.IndexOf(reconstructed, reconstructed[i], 0, i) >= 0)
                    return Degraded(id, $"it lists \"{reconstructed[i]}\" more than once, so reading its values back cannot have been faithful");

            // 2. The set must contain the value the entry is holding. CoreLib clamps at construction
            //    and on every write (ConfigEntryBase → AcceptableValueBase.Clamp), so a bound entry's
            //    value IS one of the acceptable ones. That holds for CoreLib's own two constraints; a
            //    third party whose Clamp enforces nothing can cost a legitimate Choice a read-only row
            //    here, which is the safe direction and deliberately chosen. It catches a split that ate
            //    the held value —
            //    but NOT one that left it intact as a fragment: with the set above and a held value of
            //    5, "5" is present and this check passes. That is what check 1 is for, and why neither
            //    alone is enough.
            if (System.Array.IndexOf(reconstructed, ChoiceToken.Of(e)) < 0)
                return Degraded(id, "the values it lists do not include the one it currently holds, so reading them back cannot have been faithful");

            tokens = reconstructed;
            return true;
        }

        // Entries already reported on, so a rejection is stated once rather than at every menu open.
        // Same shape as ListKindStore's per-entry memory, and for the same reason: Discover() re-runs
        // per open, so anything said per entry has to be said once or it becomes log noise.
        private static readonly HashSet<string> _degradationsReported = new HashSet<string>();

        /// <summary>Reports a value set MSM recognised but could not trust, and returns false so the
        /// caller leaves the entry read-only. Worth saying out loud because the player's symptom —
        /// a setting they can see and cannot change — is identical to the designed outcome for a
        /// shape MSM never claimed to render, and only the log can tell a mod author which they hit.</summary>
        private static bool Degraded(string id, string reason)
        {
            if (_degradationsReported.Add(id))
                UnityEngine.Debug.LogWarning($"[ModSettingsMenu] '{id}' states a fixed set of values, but {reason} — showing it as a read-only row instead.");
            return false;
        }

        /// <summary>What an exact read of a constraint's values came to. Three outcomes rather than two,
        /// because the caller has three things to do and a bool carries two of them: only
        /// <see cref="NotRecognised"/> may fall through to the reconstruction, and reading "recognised"
        /// off a non-null array — which was the earlier shape — silently sends the case this enum exists
        /// for down the very path it must not take.</summary>
        private enum ExactRead
        {
            /// <summary>Not one of the shapes ReadExactValues names. The reconstruction is next.</summary>
            NotRecognised,

            /// <summary>One of those shapes, but its values are unusable — the array came back null or
            /// empty, which its own constructor forbids. The caller reports it and stops; it must NOT
            /// consult the description, which is built from this same array.</summary>
            Unusable,

            /// <summary>One of those shapes, with values to render.</summary>
            Usable,
        }

        /// <summary>The values of a constrained set MSM can read exactly, handed back as
        /// <see cref="System.Array"/> so the element type is named once here and nowhere after.
        ///
        /// Not named Try…: that prefix promises "true ⇒ the out parameter is good", and the whole point
        /// here is that recognising a shape and getting usable values out of it are separate answers.
        ///
        /// The types are exactly <see cref="TomlTypeConverter"/>'s, minus two that cannot arrive: bool
        /// becomes a Toggle in BuildDef before `av` is read at all, and no enum can be an
        /// AcceptableValueList's T (its IEquatable&lt;T&gt; constraint, CS0315). Spelling them out one
        /// by one IS the mechanism — `av is AcceptableValueList&lt;X&gt;` needs X written down — and the
        /// set is closed rather than sampled, because CoreLib's ConfigFile.Bind refuses a type it cannot
        /// convert. Same shape as TryRange below, for the same reason.
        ///
        /// A mis-wired arm cannot compile: each `is` introduces its own pattern variable, so reusing one
        /// from an earlier arm is a definite-assignment error. What compiles silently is a MISSING arm,
        /// and its cost is small — that type falls to the reconstruction.</summary>
        private static ExactRead ReadExactValues(AcceptableValueBase av, out System.Array values)
        {
            values = null;
            if (av is AcceptableValueList<string> vString)
                values = vString.AcceptableValues;
            else if (av is AcceptableValueList<int> vInt)
                values = vInt.AcceptableValues;
            else if (av is AcceptableValueList<float> vFloat)
                values = vFloat.AcceptableValues;
            else if (av is AcceptableValueList<double> vDouble)
                values = vDouble.AcceptableValues;
            else if (av is AcceptableValueList<long> vLong)
                values = vLong.AcceptableValues;
            else if (av is AcceptableValueList<decimal> vDecimal)
                values = vDecimal.AcceptableValues;
            else if (av is AcceptableValueList<byte> vByte)
                values = vByte.AcceptableValues;
            else if (av is AcceptableValueList<sbyte> vSByte)
                values = vSByte.AcceptableValues;
            else if (av is AcceptableValueList<short> vShort)
                values = vShort.AcceptableValues;
            else if (av is AcceptableValueList<ushort> vUShort)
                values = vUShort.AcceptableValues;
            else if (av is AcceptableValueList<uint> vUInt)
                values = vUInt.AcceptableValues;
            else if (av is AcceptableValueList<ulong> vULong)
                values = vULong.AcceptableValues;
            else
                return ExactRead.NotRecognised;
            // Recognition is the cascade having matched, NOT `values != null`. AcceptableValues is
            // virtual, so a subclass may answer null — and reading that as "not one of these shapes"
            // would send it to the parse, whose first act is ToDescriptionString(): CoreLib's own
            // implementation is AcceptableValues.Select(…), which throws on null from inside another
            // mod's type. BuildSection's per-entry guard does catch that, so the cost is the row rather
            // than the screen — but a lost row is still worse than the read-only one this buys, and it
            // arrives with a stack trace instead of a sentence naming the mod.
            return values == null || values.Length == 0 ? ExactRead.Unusable : ExactRead.Usable;
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
