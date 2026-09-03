using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Component on the label template prefab — a full-width heading row inside a section box.
    ///
    /// Deliberately a plain MonoBehaviour: NOT a RadicalMenuOption, and not even a UIelement. That
    /// is what keeps it out of navigation, and it is a stronger guarantee than any flag would be —
    /// UIelement.isMenuOption is virtual with a default of false (Pug.Other:357841) and is
    /// overridden in exactly one place in the entire game, RadicalMenuOption (Pug.Other:343070). A
    /// plain MonoBehaviour therefore cannot enter a menu's menuOptions at all, so nothing has to
    /// remember to keep it out.
    ///
    /// Core Keeper solves the same problem the same way: ControlMapping_CategoryLabel
    /// (Pug.ControlMapping:1803), the category heading in the key-rebinding screen, is a
    /// MonoBehaviour with PugText children, and ControlMappingMenu adds its real rows to
    /// menuOptions while adding its category labels to nothing.
    ///
    /// It implements neither ISectionRow nor IListRow, and should not be given either: there is no
    /// value to refresh, and the section-scoped reset has nothing to write here.
    /// </summary>
    public sealed class LabelRow : MonoBehaviour
    {
        public PugText text; // the heading itself — the only thing this row has
    }
}
