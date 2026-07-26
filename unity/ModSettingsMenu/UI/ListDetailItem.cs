using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One navigable, read-only row of the list drill-in detail screen. It carries no value logic —
    /// its only job is to BE a RadicalMenuOption, so keyboard/controller navigation walks the list and
    /// CK's scroll-follow (ModSettingsScreen-style, via selectedIndex) reaches every item. The token
    /// text is rendered into this row's PugText by ListDetailScreen.Populate. Read-only in v1; this row
    /// is the future home of per-token editing.
    /// </summary>
    public sealed class ListDetailItem : RadicalMenuOption
    {
        // ACTIVE only for a live (cloned, SetActive(true)) row. The inactive prefab template must
        // report INACTIVE, else RadicalMenu's includeInactive option scan navigates to it too — a
        // phantom empty row at the end of the list (the template is the list's last prefab sibling).
        public override OptionActiveState GetActiveStateInCurrentScene() => gameObject.activeSelf ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;
    }
}
