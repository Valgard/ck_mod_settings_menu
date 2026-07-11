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
            _section.Settings.Add(new SettingDef
            {
                Key = key, Kind = SettingKind.Toggle, Term = Term(key), Entry = entry
            });
            return this;
        }

        public SectionBuilder Slider(out SettingHandle<float> handle, string key,
            float min, float max, float def, float step, SliderDisplay display = SliderDisplay.Steps)
        {
            var entry = _file.Bind("Settings", key, def,
                new ConfigDescription(key, new AcceptableValueRange<float>(min, max)));
            handle = new SettingHandle<float>(entry);
            _section.Settings.Add(new SettingDef
            {
                Key = key, Kind = SettingKind.Slider, Term = Term(key),
                Min = min, Max = max, Step = step > 0f ? step : (max - min), Display = display, Entry = entry
            });
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
            for (int i = 0; i < values.Length; i++) tokens[i] = values[i].ToString();
            // Store a string token (arbitrary T needs no CoreLib converter); validate it stays valid.
            var entry = _file.Bind("Settings", key, def.ToString(),
                new ConfigDescription(key, new AcceptableValueList<string>(tokens)));
            T FromToken(string t)
            {
                for (int i = 0; i < values.Length; i++)
                    if (tokens[i] == t) return values[i];
                return def; // unknown/removed token → default
            }
            handle = new SettingHandle<T>(entry, FromToken, v => v.ToString());
            _section.Settings.Add(new SettingDef
            {
                Key = key, Kind = SettingKind.Choice, Term = Term(key), Tokens = tokens, Entry = entry
            });
            return this;
        }

        public SectionBuilder Stepper(out SettingHandle<int> handle, string key, int min, int max, int def)
        {
            var entry = _file.Bind("Settings", key, def,
                new ConfigDescription(key, new AcceptableValueRange<int>(min, max)));
            handle = new SettingHandle<int>(entry);
            _section.Settings.Add(new SettingDef
            {
                Key = key, Kind = SettingKind.Stepper, Term = Term(key), Min = min, Max = max, Entry = entry
            });
            return this;
        }

        public void Build() => ModSettings.Register(_section);

        private string Term(string key) => $"{_section.ModId}-Config/{key}";
    }
}
