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

        /// <summary>A full-width heading between rows, for grouping a long section. Holds no value:
        /// its SettingDef carries no ConfigEntry, and it is the one kind that never becomes a menu
        /// option — see LabelRow for why that is structural rather than a setting.
        ///
        /// APPENDED, never inserted. This enum is public API; nothing persists a member today
        /// (ListKindStore stores id strings only), but a shifted value would be a silent break for
        /// anything that ever did.</summary>
        Label,
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
    /// localized label (<see cref="SettingDef.Label"/>, so ByLabel re-sorts per active
    /// language).</summary>
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

        /// <summary>Second term to try for the label, in a foreign convention this mod did not
        /// invent — see <see cref="GmcmTerms"/>. Null for a registered consumer, which has no
        /// second stage. Never read directly: ask <see cref="Label"/>.
        ///
        /// Internal, unlike its neighbours above, and deliberately: a consumer can reach any
        /// SettingDef through the public ModSettings.Sections, so a public field here would be a
        /// working back door — set it and Label() honours it — for an audience that cannot even
        /// see Label(). Nothing outside this assembly has a reason to write it; SectionBuilder
        /// offers no way to, and a registered consumer never reaches discovery at all.</summary>
        internal string GmcmTerm;

        /// <summary>Second stage for a Choice's per-option text: a PREFIX ending in '/', to which
        /// the token is appended. Null for a registered consumer.
        ///
        /// Named for what it is rather than as a term, because it is not one — concatenating it
        /// with a token is what produces a term, and a bare name would invite it being looked up
        /// as it stands. A field of its own rather than something derived from
        /// <see cref="GmcmTerm"/>, even though the derivation would work: the foreign schema puts
        /// the key on the other side of the slash for a value than for a label, and that rule
        /// belongs in the one class that knows the schema. Never read directly: ask
        /// <see cref="ValueLabel"/>.</summary>
        internal string GmcmValueTermPrefix;
        public float Min; // Slider/Stepper only (ignored for Toggle)
        public float Max; // Slider/Stepper only (ignored for Toggle)
        public float Step = 1f; // Slider only: increment per ←/→ (bar segments = (Max-Min)/Step)
        public SliderDisplay Display; // Slider only
        public string[] Tokens; // Choice only: ordered ChoiceToken.Of list (cycle order), from either path; MSM's own, never a foreign constraint's live array
        public ConfigEntryBase Entry; // live handle; widget reads/writes via BoxedValue
        public bool RequiresRestart; // true → changing this in the menu raises CK's restart prompt on leave

        // true → this def came from discovery rather than from a consumer's SectionBuilder call. A
        // statement of provenance and nothing more: no rendering reads it any more. It used to decide
        // how a Choice was read and written, back when every foreign Choice was an enum and the two
        // were coextensive; that now follows the entry's SettingType, which is what actually differs
        // (SettingWidget.Adjust). Two neighbouring behaviours look like they might still be its doing
        // and are not — a row falling back to its raw key is the term chain finding nothing at any
        // stage (SettingDef.Label), and the "(detected)" heading is ModSection.Foreign, a different
        // field on a different type. Do not add a reader here expecting either.
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

        /// <summary>This row's displayed name: MSM's own term, then the foreign one, then the raw
        /// key. Four places render a setting's name — the widget, the list row, the drill-in title
        /// and the ByLabel sort — and a chain assembled at each of them is a chain three of them
        /// can silently be missing. The sort is the one that hides it best: it would order by text
        /// nobody is shown.</summary>
        internal string Label()
        {
            string label = Loc.TFirstOf(Term, GmcmTerm, Key);
            if (!string.IsNullOrEmpty(label))
                return label;
            // Nothing MSM builds reaches here — a consumer's key comes from its own builder call.
            // A foreign one can: CoreLib's ConfigDefinition rejects whitespace and eight characters
            // but accepts an empty key, and with an empty value beside it the row would render at
            // Rect.zero — reachable by arrow key, invisible, unclickable. A placeholder keeps it on
            // screen, and the warning says why it has no name. Same call ForeignConfigDiscovery
            // already makes for a blank Choice token, which it refuses for the same reason.
            UnityEngine.Debug.LogWarning(
                $"[ModSettingsMenu] a setting under '{Term}' resolved to no name at any stage and has no key to fall back on — showing a placeholder so the row stays reachable."
            );
            return "(unnamed)";
        }

        /// <summary>The displayed text for one Choice token, by the same chain. Unlike
        /// <see cref="Label"/> this has a single call site, so drift is not the reason it lives
        /// here: the two schemas compose a per-option term differently — one appends the token to
        /// the label, the other to a prefix that moved the key — and a call site would have to
        /// know which is which to write it out.</summary>
        internal string ValueLabel(string token) => Loc.TFirstOf(Term + "/" + token, GmcmValueTermPrefix == null ? null : GmcmValueTermPrefix + token, token);
    }

    /// <summary>
    /// One consumer mod's registered section. The menu renders <see cref="Heading"/> — which
    /// resolves to DisplayName unless a discovered mod carries a term for it, and never to the
    /// internal ModId — then an optional hint, then a box of widgets.
    /// </summary>
    public sealed class ModSection
    {
        public string ModId; // Metadata.name — internal id + term prefix

        // Heading fallback, and not always Metadata.displayName: a discovered section carries the
        // config file's folder name here instead. Ask Heading(), which may translate it.
        public string DisplayName;
        public string HintTerm; // "<ModId>-Config/_hint" (loc term, resolved in Phase 3)
        public string HintText; // optional literal hint shown under the heading (pre-loc)
        public OptionSort OptionSort = OptionSort.AsDeclared; // order of options within this box
        public bool Foreign; // true → auto-detected mod: heading gets the "(detected)" marker
        public readonly List<SettingDef> Settings = new List<SettingDef>();

        /// <summary>Term to try for the heading before falling back to DisplayName, in the foreign
        /// convention of <see cref="GmcmTerms"/>. Null for a registered consumer.
        ///
        /// Two stages where a setting's label gets three: MSM has never published a term schema for
        /// a section heading — a registered consumer supplies the text itself — so there is no
        /// first stage for a foreign author to have aimed at. Inventing one here would add a
        /// lookup nothing can answer.
        ///
        /// Internal for the same reason as <see cref="SettingDef.GmcmTerm"/>: reachable from
        /// outside through ModSettings.Sections, with no use there.</summary>
        internal string HeadingTerm;

        /// <summary>The heading as rendered, before the caller adds the "(detected)" marker. Every
        /// place that shows this section's name asks here — the box, the alphabetical order of the
        /// boxes, and the reset confirmation, which must name the mod the player is looking at
        /// rather than the identifier underneath it.</summary>
        internal string Heading() => Loc.T(HeadingTerm, DisplayName);

        /// <summary>The hint line under the heading, or null when the section has none — the
        /// one caller guards with IsNullOrEmpty.
        /// Here rather than at the call site so that both of this type's rendered strings resolve
        /// in the same place — a reader should not have to learn which of them is the exception.</summary>
        internal string Hint() => Loc.T(HintTerm, HintText);
    }

    /// <summary>The one string space a Choice's tokens live in. Every place that renders, compares or
    /// stores one goes through here, because they have to agree and used to agree only by coincidence:
    /// <see cref="ForeignConfigDiscovery"/> and SectionBuilder build the token lists, SettingWidget reads
    /// the held value to find its index, the same widget displays it, and it writes the next one back.
    ///
    /// Two independent reasons, and either would be enough. A token is a localization leaf key —
    /// SettingDef.ValueLabel appends it to a term prefix — so it must not vary with the player's
    /// culture, or a consumer's yaml can only ever be right on one machine. And on the discovered path
    /// the token makes a round trip through a parser that is pinned to InvariantInfo while ToString() is
    /// not, which is the half a walkthrough can actually observe.
    ///
    /// One thing this deliberately does not do is validate the pair it is given. There is no moment at
    /// which it could: the exact path takes the value out of a System.Array and the type off the entry,
    /// so the two arrive from different objects. What keeps them coherent is CoreLib, whose
    /// ConfigEntryBase constructor refuses a constraint whose ValueType is not assignable to the
    /// setting's type — an invariant this file relies on and does not enforce. The single-argument
    /// overload below is the shape to prefer wherever an entry is at hand, because there the mistake
    /// cannot be made at all.</summary>
    internal static class ChoiceToken
    {
        /// <summary>The token for what an entry currently holds. Prefer this over the two-argument form:
        /// it takes the value and the type off the same object, so they cannot be mismatched.</summary>
        internal static string Of(ConfigEntryBase e) => Of(e?.BoxedValue, e?.SettingType);

        /// <summary>The token for a value that is not (yet) an entry's — a member of a constraint's own
        /// array, which is where the pair genuinely does come from two places.
        ///
        /// A DECLARED Choice always stores a string whatever T the consumer chose: SectionBuilder binds
        /// a ConfigEntry&lt;string&gt; over the tokens and converts back through the handle's own
        /// FromToken. So that branch is its own token and must not go near the converter, which escapes
        /// strings — the escaped form would then be compared, displayed and written.
        ///
        /// A DISCOVERED entry stores its own type, and only there does the round trip cross a parser:
        /// SettingWidget writes a chosen token back through TomlTypeConverter.ConvertToValue, which is
        /// pinned to InvariantInfo for every floating-point type, while ToString() renders in the
        /// current culture. On a comma-decimal culture that pair turns 0.5 into 5 — the comma reads as
        /// a group separator. Rendering through the converter puts both ends in the same space.
        ///
        /// Enums are named explicitly rather than left to the converter, which would answer identically:
        /// their tokens come from Enum.GetNames one case earlier in BuildDef, and tying them to CoreLib's
        /// converter table would make that agreement depend on a table this does not own.</summary>
        internal static string Of(object value, System.Type type)
        {
            if (value == null)
                return "";
            if (type == typeof(string) || type.IsEnum)
                return value.ToString();
            // A DECLARED Choice takes any T at all — it stores a string token, so nothing about it ever
            // reaches CoreLib's converter and a T outside that table is a legitimate declaration rather
            // than a mistake. Ask instead of assuming: converting would throw for exactly those, and
            // ToString() is both what such a declaration produced before and what its own FromToken
            // compares against. Every DISCOVERED entry does have a converter — the exact path accepts
            // only those types and the reconstruction asks CanConvert before it parses — so this line
            // never decides anything on that path.
            if (!TomlTypeConverter.CanConvert(type))
                return value.ToString();
            return TomlTypeConverter.ConvertToString(value, type);
        }
    }
}
