using System.Collections.Generic;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>Widget type a setting renders as (consumed by the menu UI).</summary>
    public enum SettingKind { Toggle, Slider, Stepper, Choice }

    /// <summary>How a Slider renders its value.</summary>
    public enum SliderDisplay { Steps, Number, Percent }

    /// <summary>How the options WITHIN a section are ordered in the menu. Default AsDeclared keeps
    /// the consumer's builder-chain order; ByKey/ByLabel sort alphabetically by the raw key / the
    /// localized label (`Loc.T(term, key)`, so ByLabel re-sorts per active language).</summary>
    public enum OptionSort { AsDeclared, ByKey, ByLabel }

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
        public float Step = 1f;        // Slider only: increment per ←/→ (bar segments = (Max-Min)/Step)
        public SliderDisplay Display;  // Slider only
        public string[] Tokens;        // Choice only: ordered value.ToString() list (cycle order)
        public ConfigEntryBase Entry;  // live handle; widget reads/writes via BoxedValue
    }

    /// <summary>
    /// One consumer mod's registered section. The menu renders DisplayName as the
    /// heading (never the internal ModId), an optional hint, then a box of widgets.
    /// </summary>
    public sealed class ModSection
    {
        public string ModId;           // Metadata.name — internal id + term prefix
        public string DisplayName;     // Metadata.displayName — shown heading
        public string HintTerm;        // "<ModId>-Config/_hint" (loc term, resolved in Phase 3)
        public string HintText;        // optional literal hint shown under the heading (pre-loc)
        public OptionSort OptionSort = OptionSort.AsDeclared;  // order of options within this box
        public readonly List<SettingDef> Settings = new List<SettingDef>();
    }
}
