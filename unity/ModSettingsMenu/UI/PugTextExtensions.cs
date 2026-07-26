namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Shared render helper for the menu's screens (ModSettingsScreen/ListWidget/ListDetailScreen):
    /// sets a non-localized string and forces an immediate render. Colour + maskInteraction are NOT
    /// set here — PugFont.Render paints every glyph from the PugText's own (prefab-authored) style
    /// (bright header, dimmed hint, VisibleInsideMask for scroll clipping), so callers get the right
    /// look for free. Tolerates a null/destroyed PugText (some prefab refs are optional), so it is
    /// safe to call even on an unwired reference.
    /// </summary>
    internal static class PugTextExtensions
    {
        public static void RenderPlain(this PugText pt, string s)
        {
            if (pt == null)
                return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
        }
    }
}
