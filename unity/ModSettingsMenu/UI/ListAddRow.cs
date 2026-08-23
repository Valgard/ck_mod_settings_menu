using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The trailing add button of the list drill-in: activating it appends an empty row to the
    /// screen's row list and selects that row. Its caption is the loc term
    /// <c>ModSettingsMenu-UI/ListAddButton</c>, resolved by the prefab rather than by this class —
    /// naming the term instead of the rendered wording, which has already drifted once.
    ///
    /// A plain <c>RadicalMenuOption</c>, deliberately NOT a <c>ListDetailItem</c>. The two used to
    /// share one component, separated only by a row-kind field, and the entire text-input machinery
    /// rode along on a button that can never accept text. That cost more than it saved:
    /// <list type="bullet">
    /// <item>the commit path took a row index of <c>-1</c> as a normal case, so its guard had to
    /// stay silent and could no longer tell "this is the button" from "this row lost its index" —
    /// the second case being a user's edit vanishing without a word;</item>
    /// <item>three fields had to agree (kind, index, readOnly) with nothing enforcing it.</item>
    /// </list>
    /// (An earlier version of this list also claimed the shared button could fire a commit while
    /// being torn down. It could not: bound <c>readOnly</c>, it never became
    /// <c>activeInputField</c>, so the transition that triggers a commit never armed. That hazard is
    /// real but belongs to token rows, and is handled by disabling doomed rows before detaching
    /// them.)
    /// Splitting the type makes all three unrepresentable rather than merely guarded:
    /// <c>ListDetailScreen.OnRowTextCommitted</c> takes a <c>ListDetailItem</c>, and this class
    /// simply is not one.
    /// </summary>
    public sealed class ListAddRow : RadicalMenuOption, IListRow
    {
        // The caption lives in the INHERITED RadicalMenuOption.labelText, not in a field of our own,
        // because RadicalMenuOption.InitClickCollider only creates a click collider when labelText
        // or valueText is set — a private field would render fine and leave the button unclickable.
        // (Selection tinting is NOT a reason: PugTextEffect binds to the PugText on its own
        // GameObject and RadicalMenuOption.Awake collects the effects from the children, so tinting
        // works regardless of where labelText points.)

        // The focus frame, shown while this button is the selected option.
        //
        // Re-declared here because `selectedMarker` belongs to RadicalMenuOptionTextInput, which this
        // class deliberately does NOT derive from — so splitting the button off the text row would
        // otherwise have cost it its selection affordance, leaving only CK's text tinting. That is
        // vanilla-normal for a menu option, but it would be weaker than the rows directly above it,
        // and weakest exactly where it matters most: controller and keyboard navigation, where this
        // frame is the only thing saying where you are.
        //
        // Note the division of labour, which the rows draw the same way: the FIELD frame means "you
        // can type here" and a button has none; the FOCUS frame means "this is selected" and every
        // navigable row deserves one.
        [SerializeField]
        private GameObject selectedMarker;

        // The resting frame. CK's own joinButton carries border + selectedBorder with the SAME
        // SPRITES and the same height as its text fields, scaled to its own width (6.38 vs 11.5) —
        // so a frame there means "interactive element", not "type here". This button follows that:
        // same sprites, its own width, and the clickable area derived from it as a row's is.
        [SerializeField]
        private SpriteRenderer fieldBorder;

        // Layout height in pixels, from the frame for the same reason ListDetailItem takes it from
        // there: the button is as tall as the frame drawn around it.
        public int RowHeightPx => fieldBorder != null ? ModSettingsScreen.FrameHeightPx(fieldBorder) : ModSettingsScreen.RowHeightPx(labelText);

        // The one piece of row state in this screen WITHOUT a session stamp, unlike ListDetailItem's
        // _generation. That is safe here for two reasons worth writing down, because the whole point
        // of the generation work was that a row's session must be unrepresentably wrong:
        //   * this button is a LIVE prefab object, not a clone, and the screen is a singleton — so
        //     _owner can only ever point at the one instance that owns it, in any session;
        //   * RebuildRows re-Binds it on every rebuild, so it is refreshed at least as often as a
        //     cloned row is created.
        // A read-only list skips Bind entirely (the button is switched off), which leaves _owner
        // stale-but-identical — and the button is not in menuOptions there, so nothing can activate
        // it anyway.
        private ListDetailScreen _owner;

        public void Bind(ListDetailScreen owner)
        {
            _owner = owner;
            // Start hidden — the base class does this for the rows in its own Awake, and this button
            // would otherwise show its focus frame before ever being selected.
            if (selectedMarker != null)
                selectedMarker.SetActive(false);
            // The CAPTION is not set here. It lives in the prefab as the loc term itself
            // (ModSettingsMenu-UI/ListAddButton) on a PugText with localize + renderOnStart, and
            // PugText resolves and renders it on its own — Start() renders once, OnEnable() re-renders
            // whenever the glyphs no longer match. Setting it from code would mean resolving the term
            // here and then having to switch localize off so the already-resolved text is not looked
            // up a second time; leaving it in the prefab avoids both steps and puts the button's
            // wording where someone editing the button can see it.
            //
            // This is only safe because the button is a LIVE object rather than a clone: the
            // localize-inherited-from-template trap that ListDetailScreen.AddItem documents applies
            // to cloned rows, whose text is assigned at runtime anyway.
            if (labelText == null)
                Debug.LogWarning(
                    "[ModSettingsMenu] ListAddRow has no labelText assigned — it renders blank and has no click collider (CK only creates one when labelText or valueText is set, and that decision was already made in Awake)."
                );
        }

        // ACTIVE only for a live (cloned, SetActive(true)) instance — the inactive prefab template
        // must report INACTIVE, or RadicalMenu's includeInactive option scan navigates to it too.
        // Same reasoning as ListDetailItem's override.
        public override OptionActiveState GetActiveStateInCurrentScene() => gameObject.activeSelf ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        // Mirrors what RadicalMenuOptionTextInput does for the rows: show the marker on select, hide
        // it on deselect. None of that row's hover-suppression logic applies here — this button can
        // never be the active input field, so there is no edit for a stray mouse move to disturb.
        public override void OnSelected()
        {
            base.OnSelected();
            if (selectedMarker != null)
                selectedMarker.SetActive(true);
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            if (selectedMarker != null)
                selectedMarker.SetActive(false);
        }

        public override void OnActivated()
        {
            base.OnActivated();
            _owner?.AddEmptyRow();
        }

        // Derive the clickable area from the frame, exactly as ListDetailItem does — the frame is
        // what the player sees and aims at, and reading it means an Editor resize needs no code
        // change. The base class would otherwise size the collider to the caption alone, leaving
        // most of the button dead to the mouse.
        protected override void UpdateClickCollider()
        {
            base.UpdateClickCollider();
            ModSettingsScreen.FitColliderToFrame(clickCollider, fieldBorder);
        }
    }
}
