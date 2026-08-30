using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One icon button inside a list drill-in row: move up, move down, or delete. It lives beside
    /// the field rather than inside it — its container (`Buttons`) is a sibling of the field's own
    /// (`EditField`), not the button itself a sibling of the field's frame. CK's own idiom for an
    /// affordance beside a text input (RadicalMenuOptionTextInput.radicalMenuOptionToggleVisibility
    /// is the vanilla case), and the reason ADR-005 gave the frame the width of the FIELD instead of
    /// the whole row.
    ///
    /// ONE type with a role, not three types. That is deliberately the opposite call to ADR-005,
    /// which split ListAddRow off ListDetailItem — there, three fields (kind, rowIndex, readOnly)
    /// had to agree with nothing enforcing it, i.e. reconcilable state that can drift. Here a single
    /// serialized field selects a constant and is reconciled with nothing, while all three buttons
    /// share frame, focus marker, collider lifecycle and height reporting. Three classes would be
    /// three copies of the collider overrides below, and keeping THOSE in step is exactly the
    /// failure ADR-005 set out to make unrepresentable.
    /// </summary>
    public sealed class ListRowButton : RadicalMenuOption
    {
        public enum Role
        {
            MoveUp,
            MoveDown,
            Delete,
        }

        [SerializeField]
        private Role role;

        // The resting frame, and the source of both the layout height and the click area — the same
        // division of labour ListDetailItem and ListAddRow already use. field_border from ui_chrome.
        [SerializeField]
        private SpriteRenderer fieldBorder;

        // The glyph. SetDisabled swaps its sprite between _iconNormal and iconDisabled; the frame
        // stays lit so the button remains locatable while disabled.
        [SerializeField]
        private SpriteRenderer icon;

        // The glyph shown while this button cannot be used. Assigned in the prefab for the two
        // arrows and left null on the delete button, which is never disabled.
        //
        // A SECOND SPRITE, not a tint on the first. Tinting pixel art muddies it — every pixel
        // shifts toward the tint colour and the hand-placed shading flattens — whereas a drawn
        // disabled state keeps its own contrast. CK tints because its menu options are text, and a
        // PugText glyph has a single flat colour to change; an icon does not.
        [SerializeField]
        private Sprite iconDisabled;

        // Captured in Awake rather than serialized a second time: the resting sprite is already on
        // the renderer, and a duplicate field could be pointed somewhere else by accident.
        private Sprite _iconNormal;

        private bool _disabled;

        // Shown while this button is the selected element. Re-declared here for the same reason
        // ListAddRow re-declares it: `selectedMarker` belongs to RadicalMenuOptionTextInput, which
        // this class does not derive from, and without it a controller user has nothing telling
        // them where they are.
        [SerializeField]
        private GameObject selectedMarker;

        private ListDetailItem _row;

        public Role ButtonRole => role;

        protected override void Awake()
        {
            base.Awake();
            if (icon != null)
                _iconNormal = icon.sprite;
        }

        // Deliberately NOT OptionActiveState.GRAYED_OUT. That state bundles four effects — tint,
        // click blocking, staying in the layout, and being SKIPPED by navigation — and the fourth is
        // broken on this screen: SelectIndexInDirection asks GetAdjacentUIElement BEFORE filtering,
        // so a locked neighbour yields no match and navigation stalls instead of stepping over. That
        // applies only on the UIElement path, which is the path this screen has used since
        // 2026-08-24. Taking the look without the skip is also the better answer on its own terms: a
        // button that cannot be reached cannot explain why it does nothing.
        public void SetDisabled(bool disabled)
        {
            _disabled = disabled;
            if (icon == null)
                return;
            // A button with no disabled sprite keeps its own — the delete button, which is never
            // disabled anyway. Falling back to the resting sprite rather than blanking the renderer
            // means a missing assignment shows as "does not grey out", not as an invisible button.
            var wanted = disabled && iconDisabled != null ? iconDisabled : _iconNormal;
            if (wanted != null)
                icon.sprite = wanted;
        }

        public void Bind(ListDetailItem row)
        {
            _row = row;
            if (selectedMarker != null)
                selectedMarker.SetActive(false);
        }

        // ACTIVE only for a live instance — and the test is `activeInHierarchy`, NOT `activeSelf`.
        //
        // The two sibling row types get away with `activeSelf` because each is switched off (or,
        // for a cloned row, created inactive) as itself — never as a child of something else that
        // is. A button is a CHILD of the row template: its own `activeSelf` stays true while the
        // template above it is off, so `activeSelf` reports a button that is not on screen as a
        // live menu option.
        //
        // That matters because `RadicalMenu.Awake` collects options with
        // `GetComponentsInChildren(includeInactive: true, menuOptions)` — the template's own buttons
        // land in `menuOptions`, and `Activate()` then calls `OnParentMenuActivation()` and
        // `SetActive(flag)` on them as if they were rows of the open list.
        public override OptionActiveState GetActiveStateInCurrentScene() =>
            gameObject.activeInHierarchy ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

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

        // This button is a genuine menuOptions entry — CK's own default isMenuOption applies (an
        // earlier design opted out of it and paid for that; see docs/adrs/008-list-row-buttons.md,
        // "Pros and Cons of the Options"). CK addresses it directly: RadicalMenu.ActivateSelectedIndex
        // is literally menuOptions[selectedIndex].OnActivated(), and a click reaches it the same
        // way — UIMouse calls LeftClick() (declared on UIelement, Pug.Other:357895) on
        // currentSelectedUIElement (Pug.Other:356024), which for a focused menu option now also
        // fires CK's own selection machinery (see ListDetailScreen's own navigation rewrite). No
        // forwarding is needed on either the row's side or this one: CK's selectedIndex names this
        // button directly, so no other class ever sees this activation. Move/delete are called
        // straight from here instead of through a screen-side dispatcher, since there is no longer
        // a row in the middle to relay through.
        // A disabled edge arrow must stay SELECTABLE but stop being ACTIVATABLE. The two are
        // separate tests in CK: IsSelectionEnabled (Pug.Other:343106) asks only about enabled,
        // activeInHierarchy and GRAYED_OUT, so refusing activation here costs no reachability —
        // which matters, because a greyed neighbour is a dead end rather than a skip on this
        // navigation path, and an unreachable arrow would strand the whole column below it.
        //
        // Without this, CK plays its activation receipt for a press that does nothing
        // (MenuManager.UpdateInputAndApplyToCurrentMenu, :269883, gated on
        // CanActivateCurrentOption). The same gate feeds the footer hint, so this also stops
        // offering SELECT on a control that cannot act.
        public override bool CanBeActivated() => !_disabled && base.CanBeActivated();

        public override void OnActivated()
        {
            base.OnActivated();
            if (_disabled)
                return;
            if (_row == null || _row.Owner == null)
                return;
            switch (role)
            {
                // Pass the role so the rebuild lands the selection back on THIS arrow: reordering is
                // usually repeated, and walking the selection off the arrow after every press would
                // cost a sideways step each time.
                case Role.MoveUp:
                    _row.Owner.MoveRow(_row.RowIndex, -1, Role.MoveUp);
                    break;
                case Role.MoveDown:
                    _row.Owner.MoveRow(_row.RowIndex, +1, Role.MoveDown);
                    break;
                case Role.Delete:
                    _row.Owner.RequestDelete(_row.RowIndex);
                    break;
            }
        }

        // CK creates the click collider from RENDERED TEXT: the base InitClickCollider (Pug.Other:343161)
        // only makes one once labelText or valueText ends up set, through three routes —
        // a prefab-authored serialized field (both are public, :343056/:343058), RadicalMenuOption's
        // own OnValidate auto-filling from a child named "Label"/"Value" (:343074-343075), or a
        // GetComponent<PugText> fallback on the same GameObject when labelText is still null
        // (:343163). A button whose subtree is three SpriteRenderers (Border, SelectedMarker, Glyph)
        // comes up empty on all three, so it must build its own collider instead.
        protected override void InitClickCollider()
        {
            if (clickCollider != null)
                return;
            clickCollider = gameObject.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
        }

        // Deliberately does NOT call base. The base branch (Pug.Other:343179-343181) picks valueText
        // when labelText is null, so with BOTH null it dereferences null — today unreachable only
        // because no collider exists in that case, which the override above has just changed.
        // Sizing from the frame is also what the two sibling row types do, for the same reason: the
        // frame is what the player sees and aims at.
        protected override void UpdateClickCollider()
        {
            if (clickCollider == null)
                return;
            ModSettingsScreen.FitColliderToFrame(clickCollider, fieldBorder);
            // base would also do this; skipping base means doing it here.
            clickCollider.enabled = GetActiveStateInCurrentScene() == OptionActiveState.ACTIVE;
        }
    }
}
