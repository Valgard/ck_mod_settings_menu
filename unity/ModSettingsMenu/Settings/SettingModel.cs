using System.Collections.Generic;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>Widget type a setting renders as (consumed by the Phase-2b menu UI).</summary>
    public enum SettingKind { Toggle, Slider, Stepper }

    /// <summary>
    /// Non-generic descriptor of one registered setting. Carries everything the
    /// Phase-2b menu needs to render + drive it: the derived loc term, the widget
    /// kind, numeric bounds (Slider/Stepper), and the live CoreLib ConfigEntry
    /// (read/write via the type-agnostic ConfigEntryBase surface).
    /// </summary>
    public sealed class SettingDef
    {
        public string Key;             // e.g. "xpMultiplier"
        public SettingKind Kind;
        public string Term;            // e.g. "FasterTalents-Config/xpMultiplier"
        public float Min;              // Slider/Stepper only (ignored for Toggle)
        public float Max;              // Slider/Stepper only (ignored for Toggle)
        public ConfigEntryBase Entry;  // live handle; 2b uses GetSerializedValue/SetSerializedValue
    }

    /// <summary>
    /// One consumer mod's registered section. The menu renders DisplayName as the
    /// heading (never the internal ModId), an optional hint, then a box of widgets.
    /// </summary>
    public sealed class ModSection
    {
        public string ModId;           // Metadata.name — internal id + term prefix
        public string DisplayName;     // Metadata.displayName — shown heading
        public string HintTerm;        // "<ModId>-Config/_hint" (opt-in render in 2b)
        public readonly List<SettingDef> Settings = new List<SettingDef>();
    }
}
