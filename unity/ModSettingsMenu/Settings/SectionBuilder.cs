using CoreLib.Data.Configuration;

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
        /// </summary>
        public SectionBuilder List(out SettingHandle<string[]> handle, string key, string[] defaults, ListEditing editing = ListEditing.FreeText)
        {
            var entry = _file.Bind("Settings", key, ListTokenizer.Join(defaults), new ConfigDescription(key));
            // Where the player cannot add entries, a default the consumer declares LATER would
            // otherwise never reach them — their stored value predates it and they have no way to
            // type it in. Merging at bind (rather than at render) keeps the file, the handle and
            // the screen telling the same story from the first frame. Deliberately NOT done for
            // FreeText: there, the same code would resurrect an entry the player deleted on
            // purpose, every single launch.
            if (editing != ListEditing.FreeText)
                AppendMissingDefaults(entry, defaults);
            handle = new SettingHandle<string[]>(entry, s => ListTokenizer.Tokenize(s).ToArray(), v => ListTokenizer.Join(v));
            _section.Settings.Add(
                new SettingDef
                {
                    Key = key,
                    Kind = SettingKind.List,
                    Term = Term(key),
                    Entry = entry,
                    Editing = editing,
                }
            );
            return this;
        }

        // Appends every declared default the stored value does not already carry, in declaration
        // order, and writes back only if something was actually missing — an unconditional write
        // would touch the config file on every launch and, through CoreLib's SaveOnConfigSet, save
        // it too. Order among the existing entries is the player's and is never rearranged.
        private static void AppendMissingDefaults(ConfigEntry<string> entry, string[] defaults)
        {
            if (defaults == null)
                return;
            var tokens = ListTokenizer.Tokenize(entry.Value);
            int before = tokens.Count;
            foreach (var raw in defaults)
            {
                var token = ListTokenizer.Sanitize(raw);
                if (token.Length > 0 && !tokens.Contains(token))
                    tokens.Add(token);
            }
            if (tokens.Count != before)
                entry.Value = ListTokenizer.Join(tokens);
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
