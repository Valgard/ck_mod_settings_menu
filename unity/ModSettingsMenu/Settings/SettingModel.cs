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
    /// SettingDef.EffectiveEditing, never SettingDef.DeclaredEditing directly.
    ///
    /// NEVER persist or [SerializeField] a value of this enum. Nothing does today, and the planned
    /// insertion of a picker level BETWEEN FreeText and OrderOnly depends on that: the numeric
    /// values shift, so a stored one would silently come back as a different level.
    ///
    /// Do not test against a member here to decide what the UI offers — ask ListAccess. Each
    /// capability is a separate question, and most of them are decided by FreeText-ness alone today
    /// purely because there are only three levels — which is what makes them look interchangeable
    /// and is exactly why they are not.</summary>
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
    /// What each <see cref="ListEditing"/> level permits, as one named question per capability.
    ///
    /// This exists because the four decisions are NOT the same question, even though three of them
    /// currently read as `!= FreeText`: whether a row can be typed into, whether the add row exists,
    /// whether entries can be reordered, and whether one can be deleted. Written inline, each site
    /// looked like a test of the level rather than of a capability, and a reader could not tell
    /// which was meant.
    ///
    /// The cost of that was measured rather than imagined. Adding the planned picker level — which
    /// CAN add entries but is NOT typed into — silently flips three of the five answers below:
    /// CanAdd and CanDelete would say no where the level means yes, and ReconcilesDefaults would say
    /// yes where it means no. That last one is the damaging one: <see cref="SectionBuilder"/>'s
    /// ReconcileWithDefaults runs at every bind, so it would resurrect entries a player deleted on
    /// purpose, every launch. All three would compile cleanly, and no test in this repo would fail,
    /// because verification here is a person walking the menu.
    ///
    /// So a new level is filled in here, in one screenful, instead of being audited across the
    /// codebase. Deliberately expression-bodied one-liners rather than a switch: the point is that
    /// all five answers are visible at once.
    /// </summary>
    internal static class ListAccess
    {
        /// <summary>The row is a text field the player edits.</summary>
        internal static bool CanType(ListEditing level) => level == ListEditing.FreeText;

        /// <summary>The drill-in offers a way to author a new entry.</summary>
        internal static bool CanAdd(ListEditing level) => level == ListEditing.FreeText;

        /// <summary>Entries can be moved up and down.</summary>
        internal static bool CanReorder(ListEditing level) => level != ListEditing.ReadOnly;

        /// <summary>An entry can be removed. Never offered where it could not be added back — the
        /// only recovery would be the section-wide reset, which takes every other setting of that
        /// mod with it.</summary>
        internal static bool CanDelete(ListEditing level) => CanAdd(level);

        /// <summary>Whether the stored value is reconciled against the consumer's declared defaults
        /// at bind. Derived from CanAdd rather than restated: the reconciliation exists precisely
        /// because a player who cannot author an entry can neither gain one the consumer adds later
        /// nor drop one it removes — where they CAN, the stored value is theirs and must be left
        /// alone.</summary>
        internal static bool ReconcilesDefaults(ListEditing level) => !CanAdd(level);
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

        // true → this def came from discovery rather than from a consumer's SectionBuilder call. A
        // statement of provenance and nothing more: no rendering reads it any more. It used to decide
        // how a Choice was read and written, back when every foreign Choice was an enum and the two
        // were coextensive; that now follows the entry's SettingType, which is what actually differs
        // (SettingWidget.Adjust). Two neighbouring behaviours look like they might still be its doing
        // and are not — the raw label is Term = key (ForeignConfigDiscovery) reaching Loc.T's
        // fallback, and the "(detected)" heading is ModSection.Foreign, a different field on a
        // different type. Do not add a reader here expecting either.
        public bool Foreign;
        public bool Unbounded; // Stepper only: skip the Min/Max clamp (a foreign numeric with no range)

        // true → this row's own Kind still renders natively, but the widget (or, for List, the
        // drill-in) must not respond to input: either a genuine permission lock (view-only/
        // server-locked and not this session's host) or, hard-coded true regardless of scope, a
        // Kind == Info fallback where no editable widget exists for the value's shape at all.
        // (Leading, not trailing: CSharpier reflows a multi-line trailing comment into the gap
        // before the next member, which made its tail read as documentation for Editing.)
        public bool ReadOnly;

        /// <summary>List only: the level the CONSUMER asked for, before any permission lock.
        /// Read <see cref="EffectiveEditing"/> instead — this one is only half the answer.
        ///
        /// Named "Declared" rather than plain "Editing" so that neither member is the short,
        /// obvious one: with a bare name, reading the wrong member compiles AND returns the same
        /// answer for every list that has no permission lock, so the mistake survives testing until
        /// the first consumer combines a declared level with a locked scope.</summary>
        public ListEditing DeclaredEditing { get; internal set; }

        /// <summary>What the drill-in may actually offer, once a permission lock is taken into
        /// account. ReadOnly demotes every declaration to ReadOnly; nothing ever promotes.
        ///
        /// It returns the same enum the consumer declares rather than a second, parallel one,
        /// because there is no state here the declaration cannot already express — a second type
        /// would only create the question of which of the two a given call site meant.
        ///
        /// Computed, not folded once at construction: ForeignConfigDiscovery.Discover() re-runs on
        /// every screen build, so ReadOnly is recomputed per open (a Server-scoped setting is locked
        /// at the title screen and editable in a session) and a snapshot would go stale.</summary>
        public ListEditing EffectiveEditing => ReadOnly ? ListEditing.ReadOnly : DeclaredEditing;

        /// <summary>List only: the drill-in would have no rows AND no way to gain one, so opening it
        /// can only produce an empty screen. Empty is not merely useless there — with no menu options
        /// CK dereferences menuOptions[-1] in three separate places, one of them inside Activate()
        /// before any key is pressed (see ListDetailScreen.Open).
        ///
        /// Lives here because two places need the same answer and must not drift: ListWidget, to stop
        /// offering a row that does nothing, and ListDetailScreen.Open, to refuse the push. A second
        /// copy of this condition is precisely the failure this branch has already paid for twice.</summary>
        internal bool ListDetailWouldBeEmpty =>
            Kind == SettingKind.List
            && !ListAccess.CanAdd(EffectiveEditing)
            && ListTokenizer.Tokenize(Entry != null && Entry.BoxedValue != null ? Entry.BoxedValue.ToString() : "").Count == 0;
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
