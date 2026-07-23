using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The list drill-in detail screen: a pushed RadicalMenu showing one comma-list in full — a
    /// title plus one navigable read-only row per item, scrollable. Controller/keyboard navigation
    /// walks the rows and scroll-follow reaches the bottom (the overflow fix). Read-only in v1; the
    /// rows are the future home of per-token editing.
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class ListDetailScreen : RadicalMenu, IScrollable
    {
        public ListDetailBox box;

        private static SettingDef _pending;   // seeded by Open() before PushMenu resolves this instance

        public static void Open(SettingDef def)
        {
            _pending = def;
            Manager.menu.PushMenu(ModSettingsMenuMod.ListDetailMenuType);
        }

        public override void Activate()
        {
            Populate();
            base.Activate();
            RenderContent();
        }

        private void Populate()
        {
            // TASK 5: build title + one row per token from _pending.Entry.BoxedValue, add each to
            // menuOptions (navigable), mirroring ModSettingsScreen.Populate's row instantiation.
        }

        private void RenderContent()
        {
            // TASK 5: render the item container's LinearLayout after activation (build-then-render).
        }

        // IScrollable — window height from the item container's layout (feeds scroll clipping).
        public void UpdateContainingElements(float scroll) { }
        public bool IsBottomElementSelected() => false;
        public bool IsTopElementSelected() => false;
        public float GetCurrentWindowHeight() => 0f;   // TASK 5: layout render height
    }
}
