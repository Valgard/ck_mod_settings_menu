using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Component on the section-box template prefab. Exposes the box's header
    /// PugText and the transform under which widgets are placed, so SettingsMenu
    /// wires them by serialized reference (robust) rather than by Find() paths.
    /// </summary>
    public sealed class SectionBox : MonoBehaviour
    {
        public PugText header;            // "DisplayName" heading
        public PugText hint;              // optional dimmed sub-line under the heading
        public Transform widgetContainer; // parent for the widget rows
    }
}
