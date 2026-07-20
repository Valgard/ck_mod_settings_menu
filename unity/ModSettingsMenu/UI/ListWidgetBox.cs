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
        public PugText label;          // the setting key (left)
        public PugText toggleValue;    // on/off state of "show as list" (right, like a Toggle row)
        public Transform itemContainer;// LinearLayout parent for the item lines
        public GameObject itemTemplate;// inactive PugText row cloned once per item
    }
}
