namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The list widget's far-right toggle icon as a CK-native clickable control: a left-click flips
    /// the row between the comma-split list view and the plain string, delegating to the parent
    /// <see cref="ListWidget"/>. Follows the ItemChecklist ButtonUIElement recipe — a 3D BoxCollider
    /// (CK's UIMouse raycasts in 3D against the UI layer) defines the click area, and the base's
    /// spritesShownUnpressed/spritesShownPressed lists are left EMPTY so ButtonUIElement.LateUpdate
    /// does not toggle the icon SpriteRenderer's GameObject off. The collider sits slightly forward
    /// in Z so it wins the raycast over the row. Mouse only for now; keyboard/controller activation
    /// of the icon comes later (the row itself deliberately no longer toggles).
    /// </summary>
    public sealed class ListToggleButton : ButtonUIElement
    {
        private ListWidget _widget;

        protected override void Awake()
        {
            base.Awake();
            _widget = GetComponentInParent<ListWidget>();
        }

        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            if (!canBeClicked) return;
            base.OnLeftClicked(mod1, mod2);
            (_widget != null ? _widget : GetComponentInParent<ListWidget>())?.ToggleView();
        }
    }
}
