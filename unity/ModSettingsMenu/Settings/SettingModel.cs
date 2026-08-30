using System.Collections.Generic;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>Widget type a setting renders as (consumed by the menu UI).</summary>
    public enum SettingKind
    {
        Toggle,
        Slider,
        Stepper,
        Choice,
        Info,
        List,
    }

    /// <summary>How a Slider renders its value.</summary>
    public enum SliderDisplay
    {
        Steps,
        Number,
        Percent,
    }

    /// <summary>How the options WITHIN a section are ordered in the menu. Default AsDeclared keeps
    /// the consumer's builder-chain order; ByKey/ByLabel sort alphabetically by the raw key / the
    /// localized label (`Loc.T(term, key)`, so ByLabel re-sorts per active language).</summary>
    public enum OptionSort
    {
        AsDeclared,
        ByKey,
        ByLabel,
    }

    /// <summary>What a player may do to a List setting's entries, declared by the consumer and
    /// ordered from most to least. Each value answers one question — where does a new entry come
    /// from? — which is why a picker variant would slot in beside these rather than needing a
    /// second dimension.
    ///
    /// FreeText is the default and the shape ForeignConfigDiscovery produces: a discovered list is
    /// a comma string whose entries the heuristic knows nothing about, so nothing narrower would
    /// be honest about it.
    ///
    /// This is the CONSUMER's declaration, not the effective state — a permission lock
    /// (SettingDef.ReadOnly) demotes any of these to ReadOnly at render time. Ask
    /// SettingDef.EffectiveEditing, never this field directly.</summary>
    public enum ListEditing
    {
        /// <summary>The player types entries: edit, add, delete, reorder.</summary>
        FreeText,

        /// <summary>No entries come or go; the player only reorders the ones that are there.</summary>
        OrderOnly,

        /// <summary>Display only — every row is inert.</summary>
        ReadOnly,
    }

    /// <summary>
    /// Non-generic descriptor of one registered setting. Carries everything the
    /// Phase-2b menu needs to render + drive it: the derived loc term, the widget
    /// kind, numeric bounds (Slider/Stepper), and the live CoreLib ConfigEntry
    /// (read/write via the type-agnostic ConfigEntryBase surface).
    /// </summary>
    public sealed class SettingDef
    {
        public string Key; // e.g. "xpMultiplier"
        public SettingKind Kind;
        public string Term; // e.g. "FasterTalents-Config/xpMultiplier"
        public float Min; // Slider/Stepper only (ignored for Toggle)
        public float Max; // Slider/Stepper only (ignored for Toggle)
        public float Step = 1f; // Slider only: increment per ←/→ (bar segments = (Max-Min)/Step)
        public SliderDisplay Display; // Slider only
        public string[] Tokens; // Choice only: ordered value.ToString() list (cycle order)
        public ConfigEntryBase Entry; // live handle; widget reads/writes via BoxedValue
        public bool RequiresRestart; // true → changing this in the menu raises CK's restart prompt on leave
        public bool Foreign; // true → discovered (not API-registered): raw label, serialized Choice, marker
        public bool Unbounded; // Stepper only: skip the Min/Max clamp (a foreign numeric with no range)

        // true → this row's own Kind still renders natively, but the widget (or, for List, the
        // drill-in) must not respond to input: either a genuine permission lock (view-only/
        // server-locked and not this session's host) or, hard-coded true regardless of scope, a
        // Kind == Info fallback where no editable widget exists for the value's shape at all.
        // (Leading, not trailing: CSharpier reflows a multi-line trailing comment into the gap
        // before the next member, which made its tail read as documentation for Editing.)
        public bool ReadOnly;

        public ListEditing Editing; // List only: what the CONSUMER declared. Read EffectiveEditing, not this.

        /// <summary>What the drill-in may actually offer, once a permission lock is taken into
        /// account. ReadOnly demotes every declaration to ReadOnly; nothing ever promotes.
        ///
        /// It returns the same enum the consumer declares rather than a second, parallel one,
        /// because there is no state here the declaration cannot already express — a second type
        /// would only create the question of which of the two a given call site meant.</summary>
        public ListEditing EffectiveEditing => ReadOnly ? ListEditing.ReadOnly : Editing;
    }

    /// <summary>
    /// One consumer mod's registered section. The menu renders DisplayName as the
    /// heading (never the internal ModId), an optional hint, then a box of widgets.
    /// </summary>
    public sealed class ModSection
    {
        public string ModId; // Metadata.name — internal id + term prefix
        public string DisplayName; // Metadata.displayName — shown heading
        public string HintTerm; // "<ModId>-Config/_hint" (loc term, resolved in Phase 3)
        public string HintText; // optional literal hint shown under the heading (pre-loc)
        public OptionSort OptionSort = OptionSort.AsDeclared; // order of options within this box
        public bool Foreign; // true → auto-detected mod: heading gets the "(detected)" marker
        public readonly List<SettingDef> Settings = new List<SettingDef>();
    }
}
