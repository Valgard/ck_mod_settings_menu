using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Component on the list-widget template prefab. Exposes the compact row's parts by serialized
    /// reference (robust vs Find()): the option label, a value preview, and the drill affordance.
    /// </summary>
    public sealed class ListWidgetBox : MonoBehaviour
    {
        public PugText label; // the option name (left)
        public PugText preview; // compact value preview (right): "first, second, +N"
        public SpriteRenderer drillIcon; // the "▸" affordance (opens the detail screen); may be null in v1 prefab
    }
}
