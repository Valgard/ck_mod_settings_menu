using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>Serialized refs on the detail-screen prefab: the title PugText, the scrollable
    /// item container (a LinearLayout), and the two row templates.</summary>
    public sealed class ListDetailBox : MonoBehaviour
    {
        public PugText title;
        public Transform itemContainer;

        // One entry of the list: a framed, editable text field (ListDetailItem).
        public GameObject itemTemplate;

        // The trailing "add an entry" button — a LIVE object inside itemContainer, not a template.
        // There is only ever one, so cloning it per rebuild would mean destroying and re-rendering
        // an unchanging object on every keystroke that commits. Its own type keeps it out of the
        // teardown: RebuildRows removes the rows it created (ListDetailItem) and leaves everything
        // else alone.
        public ListAddRow addRow;
    }
}
