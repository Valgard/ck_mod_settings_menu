using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Component on the section-box template prefab. Exposes the box's header
    /// PugText and the transform under which widgets are placed, so ModSettingsScreen
    /// wires them by serialized reference (robust) rather than by Find() paths.
    /// </summary>
    public sealed class SectionBox : MonoBehaviour
    {
        public PugText header; // "DisplayName" heading
        public PugText hint; // optional dimmed sub-line under the heading
        public PugText lockNote; // line under the hint naming why the section withholds settings; null means the template has none
        public Transform widgetContainer; // parent for the widget rows
    }
}
