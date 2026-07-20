using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Component on the list-widget template prefab. Exposes the row's label, the toggle-state value
    /// text, the item container (a LinearLayout that holds one read-only line per list item — and the
    /// future edit UI), and the per-item line template, by serialized reference (robust vs Find()).
    /// </summary>
    public sealed class ListWidgetBox : MonoBehaviour
    {
        public PugText label;            // the option name (left)
        public Transform itemContainer;  // value-column LinearLayout: list items (list) or 1 plain line
        public GameObject itemTemplate;  // inactive PugText row cloned once per item
        public SpriteRenderer toggleIcon;// 2-state toggle sprite, far-right on line 1
        public Sprite listIcon;          // toggleIcon sprite in list view
        public Sprite plainIcon;         // toggleIcon sprite in plain view
    }
}
