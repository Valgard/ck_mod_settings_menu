using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One icon button inside a list drill-in row: move up, move down, or delete. A sibling of the
    /// field frame rather than a child of it — CK's own idiom for an affordance beside a text input
    /// (RadicalMenuOptionTextInput.radicalMenuOptionToggleVisibility is the vanilla case), and the
    /// reason ADR-005 gave the frame the width of the FIELD instead of the whole row.
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
        // The two sibling row types get away with `activeSelf` because THEY are the object the
        // template switches off. A button is a CHILD of that template: its own `activeSelf` stays
        // true while the template above it is off, so `activeSelf` reports a button that is not on
        // screen as a live menu option.
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
            if (_row != null)
            {
                _row.FocusedSlot = role;
                if (_row.Owner != null)
                    _row.Owner.NoteFocusedSlot(role);
            }
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
            if (_disabled)
                return;
            if (_row != null && _row.Owner != null)
                _row.Owner.OnRowButtonActivated(_row, role);
        }

        // CK creates the click collider from RENDERED TEXT: InitClickCollider only makes one when
        // labelText or valueText is set (Pug.Other ~343161), and it is a `protected` field with no
        // [SerializeField], so it cannot be authored in the prefab either. A button that is only a
        // picture therefore has neither — it must make its own.
        protected override void InitClickCollider()
        {
            if (clickCollider != null)
                return;
            clickCollider = gameObject.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
        }

        // Deliberately does NOT call base. The base branch (~343174) picks valueText when labelText
        // is null, so with BOTH null it dereferences null — today unreachable only because no
        // collider exists in that case, which the override above has just changed. Sizing from the
        // frame is also what the two sibling row types do, for the same reason: the frame is what
        // the player sees and aims at.
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
