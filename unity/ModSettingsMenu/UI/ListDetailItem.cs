using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One navigable, EDITABLE row of the list drill-in detail screen. Built on CK's own
    /// RadicalMenuOptionTextInput — the same base class the character-name field
    /// (CharacterCustomizationOption_NameInput) uses — so it gets on-screen-keyboard support
    /// (controller sessions), focus/blink handling, and the read-vs-edit visual split for free:
    /// looks like a static value while merely navigated onto (OnSelected, inherited), and only
    /// captures real input once confirmed (OnActivated -> Manager.input.SetActiveInputField(this),
    /// inherited unchanged). Committing happens when this row STOPS being
    /// Manager.input.activeInputField (Update() below) — not on OnDeselected, which CK's own
    /// UIMouse also fires on mere hover; while a row holds activeInputField, clicking a DIFFERENT
    /// row is ignored (OnLeftClicked below) so only Enter/Escape (Deactivate) or the drill-in
    /// screen closing can end an edit. See ListDetailScreen.OnRowTextCommitted for the actual
    /// persist-and-rebuild logic; this class only reports the event and its own current text.
    /// </summary>
    public sealed class ListDetailItem : RadicalMenuOptionTextInput
    {
        // Owning screen, wired by ListDetailScreen.AddItem right after Instantiate.
        public ListDetailScreen owner;

        // True only for the permanent trailing blank row ("+ Add"). Every other row is a real token.
        public bool isAddRow;

        // True for a genuine read-only list (SettingDef.ReadOnly) — view/scroll/navigate like any
        // other row, but OnActivated below never enters edit mode. Wired by ListDetailScreen.AddItem
        // alongside owner/isAddRow.
        public bool readOnly;

        // ACTIVE only for a live (cloned, SetActive(true)) row — the inactive prefab template must
        // report INACTIVE, else RadicalMenu's includeInactive option scan navigates to it too (the
        // template is the list's last prefab sibling). Unchanged from the read-only version.
        public override OptionActiveState GetActiveStateInCurrentScene() => gameObject.activeSelf ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        // NOT called from here anymore — see Update() below. RadicalMenu.SelectOptionIndex calls
        // OnDeselected() on every navigation-away, INCLUDING mere mouse hover onto a different row
        // (CK's own UIMouse re-derives menu selection from a hover raycast every frame; hovering a
        // different RadicalMenuOption drives SelectOptionIndex exactly like arrow-key navigation
        // does). Committing here would end an active edit the instant the mouse passes over any
        // other row, even without a click.
        public override void OnDeselected(bool playEffect = true)
        {
            // Also suppress the visual deselect (base.OnDeselected hides selectedMarker) while
            // THIS row is still the active input field — that only happens on the hover-driven
            // call above, since a real "stop editing" (Enter/Escape/click-elsewhere) already
            // cleared activeInputField before RadicalMenu ever gets here. Keeps the row LOOKING
            // like the one being edited instead of flickering to "unselected" on every hover.
            if (Manager.input.activeInputField == (object)this)
                return;
            base.OnDeselected(playEffect);
        }

        // Mirrors OnDeselected above: suppress the hover-selected marker on THIS row while a
        // DIFFERENT row in this menu is the active input field, so hovering around while typing
        // elsewhere doesn't visually highlight whatever the mouse happens to pass over.
        public override void OnSelected()
        {
            if (Manager.input.activeInputField != null && Manager.input.activeInputField != (object)this)
                return;
            base.OnSelected();
        }

        // Distinguishes "actively editing" (SELECTED_VALUE_COLOR, vivid) from merely "selected/
        // navigated" (SELECTED_TEXT_COLOR, pale — the prefab's own isValueText = false default),
        // since our patches above now pin selectedIndex on this row throughout an edit, so a user
        // could no longer tell the two states apart just by looking.
        //
        // Flipping isValueText alone does not immediately recolor an already-selected row: the
        // color is only (re)applied at specific transitions (OnSelected/OnDeselected/the
        // unselected<->selected cooldown blend in PugTextEffectLateUpdate) — while IsSelected()
        // stays true every frame, which it now always does mid-edit, that method just runs the
        // glyph "dance" animation and returns without touching color at all. But EVERY keystroke
        // DOES pass back through one such transition: AppendString/RemoveCharBehindMarker/etc. all
        // call pugText.Render(), which (unless dontResetEffectsOnRender) calls
        // PugTextEffectMenuOption.ResetEffect -> OnSelected() again, re-reading isValueText fresh
        // each time. A first attempt that only called pugText.SetTempColor(...) once here got
        // silently overwritten back to the prefab default on the very next keystroke for exactly
        // this reason. So the field flip IS the right mechanism here (not a redundant one) — it's
        // just insufficient by itself for the very first frame, which OnSelected() below covers
        // explicitly. No revert on end-of-edit is needed: every path that ends an edit already
        // triggers ListDetailScreen.RebuildRows(), which destroys this (flipped) instance and
        // creates a fresh one starting back at the prefab's own isValueText = false.
        public override void OnActivated()
        {
            // A read-only list's rows are still navigable (GetActiveStateInCurrentScene stays
            // ACTIVE) so the player can view/scroll every token, but activating one must not enter
            // edit mode — base.OnActivated() is what calls Manager.input.SetActiveInputField(this);
            // skipping it here means activeInputField can never become a read-only row, so every
            // other guard in this class and MenuPatch's two Harmony prefixes (all keyed off
            // activeInputField) stay correctly inert without needing their own readOnly check.
            if (readOnly)
                return;
            base.OnActivated();
            // PugTextEffectMenuOption now lives on the "Text" child alongside the PugText it tints
            // (the ICL/SettingTemplate "Display" pattern) — GetComponentInChildren is safe here (unlike
            // for PugText itself) because this component type is unique in the row's hierarchy, so
            // there's no sibling-order ambiguity to worry about.
            var effect = GetComponentInChildren<PugTextEffectMenuOption>();
            if (effect != null)
            {
                effect.isValueText = true;
                effect.OnSelected();
            }
        }

        // Defense in depth: the actual click-away-during-edit fix lives in MenuPatch's Harmony
        // prefix on UIMouse.TrySelectNewElement (see there for why — the real mechanism is CK's
        // own code deactivating the old field BEFORE this ever runs, not this method itself). This
        // guard is what would run in an ordinary (non-hijacked) click while a different row is
        // still active, if that mechanism is ever bypassed some other way.
        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            if (Manager.input.activeInputField != null && Manager.input.activeInputField != (object)this)
                return;
            base.OnLeftClicked(mod1, mod2);
        }

        // Full row width instead of the base RadicalMenuOption behavior (text-rendered width only):
        // a text-width collider shrinks with every deleted character. If the mouse sits stationary
        // near the end of the text, backspacing can shrink the collider out from under it, and CK's
        // hover system reacts as if the mouse left the row entirely (see the Update() note below).
        // 25 matches maxWidth on this same row's text input further down the prefab (the widest a
        // token is ever allowed to render), so the collider always covers the full possible text —
        // not a guessed constant.
        protected override void UpdateClickCollider()
        {
            base.UpdateClickCollider();
            if (clickCollider == null)
                return;
            const float RowContentWidth = 25f;
            var size = clickCollider.size;
            var center = clickCollider.center;
            size.x = RowContentWidth;
            center.x = RowContentWidth / 2f; // text starts at local x=0 and grows rightward
            clickCollider.size = size;
            clickCollider.center = center;
        }

        // RadicalMenuOptionTextInput.Update() overrides RadicalMenuOption.Update() WITHOUT calling
        // base.Update() — so UpdateClickCollider() (which lives in RadicalMenuOption.Update() and
        // resizes the click collider to the row's actual rendered text bounds) never runs, leaving
        // every row stuck at Unity's default freshly-added BoxCollider size (1x1x1, centered at
        // origin) regardless of content. Restore it explicitly. (Root-caused via Debug.Log
        // instrumentation showing clickCollider.size/center never changing across 20+ rows of
        // varying text length, while pugText.dimensions correctly reflected each row's real width.)
        //
        // Manager.input.activeInputField and RadicalMenu.selectedIndex are two INDEPENDENT pieces
        // of state. Only Deactivate() (fired from Enter/Escape while typing) or a DIFFERENT row's
        // OnActivated() (fired from an explicit click, which calls Manager.input.
        // SetActiveInputField on itself) ever actually clear activeInputField away from this row;
        // mere mouse hover elsewhere only moves selectedIndex (via OnDeselected above, deliberately
        // left inert) and never touches activeInputField. So "activeInputField just stopped being
        // this row" is the one reliable "the user is genuinely done editing" signal — commit here,
        // once, on that transition, instead of on every hover-driven OnDeselected.
        private bool _wasActiveField;

        protected override void Update()
        {
            base.Update();
            UpdateClickCollider();

            bool isActiveField = Manager.input.activeInputField == (object)this;
            if (_wasActiveField && !isActiveField)
                owner?.OnRowTextCommitted(this);
            _wasActiveField = isActiveField;
        }
    }
}
