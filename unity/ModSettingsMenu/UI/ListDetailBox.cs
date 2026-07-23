using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>Serialized refs on the detail-screen prefab: the title PugText, the scrollable
    /// item container (a LinearLayout), and the per-item read-only row template.</summary>
    public sealed class ListDetailBox : MonoBehaviour
    {
        public PugText title;
        public Transform itemContainer;
        public GameObject itemTemplate;
    }
}
