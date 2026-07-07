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

        public SectionBuilder Slider(out SettingHandle<float> handle, string key, float min, float max, float def)
        {
            var entry = _file.Bind("Settings", key, def,
                new ConfigDescription(key, new AcceptableValueRange<float>(min, max)));
            handle = new SettingHandle<float>(entry);
            _section.Settings.Add(new SettingDef
            {
                Key = key, Kind = SettingKind.Slider, Term = Term(key), Min = min, Max = max, Entry = entry
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
