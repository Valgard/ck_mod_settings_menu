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
    /// inherited unchanged). Committing happens on OnDeselected (leaving the row) — see
    /// ListDetailScreen.OnRowTextCommitted for the actual persist-and-rebuild logic; this class
    /// only reports the event and its own current text.
    /// </summary>
    public sealed class ListDetailItem : RadicalMenuOptionTextInput
    {
        // Owning screen, wired by ListDetailScreen.AddItem right after Instantiate.
        public ListDetailScreen owner;

        // True only for the permanent trailing blank row ("+ Add"). Every other row is a real token.
        public bool isAddRow;

        // ACTIVE only for a live (cloned, SetActive(true)) row — the inactive prefab template must
        // report INACTIVE, else RadicalMenu's includeInactive option scan navigates to it too (the
        // template is the list's last prefab sibling). Unchanged from the read-only version.
        public override OptionActiveState GetActiveStateInCurrentScene() => gameObject.activeSelf ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            // Defensive: release a dangling activeInputField reference if this row is still it
            // (RadicalMenuOptionTextInput's own OnDeselected doesn't clear this itself) — harmless
            // no-op if the framework already released it via some other path.
            if (Manager.input.activeInputField == (object)this)
                Manager.input.SetActiveInputField(null);
            owner?.OnRowTextCommitted(this);
        }

        // RadicalMenuOptionTextInput.Update() overrides RadicalMenuOption.Update() WITHOUT calling
        // base.Update() — so UpdateClickCollider() (which lives in RadicalMenuOption.Update() and
        // resizes the click collider to the row's actual rendered text bounds) never runs, leaving
        // every row stuck at Unity's default freshly-added BoxCollider size (1x1x1, centered at
        // origin) regardless of content. Restore it explicitly. (Root-caused via Debug.Log
        // instrumentation showing clickCollider.size/center never changing across 20+ rows of
        // varying text length, while pugText.dimensions correctly reflected each row's real width.)
        protected override void Update()
        {
            base.Update();
            UpdateClickCollider();
        }
    }
}
