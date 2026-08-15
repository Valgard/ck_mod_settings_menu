using ModSettingsMenu.Settings;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// A menu row that belongs to exactly one section (a consumer's box or a discovered
    /// mod's box). Gives the screen both directions a section-scoped reset needs: from the
    /// selected option to its section (for the hint bar), and from a section to every one of
    /// its rows (to re-render after the bulk write). Implemented by both row classes because
    /// they share no base beyond RadicalMenuOption.
    /// </summary>
    internal interface ISectionRow
    {
        ModSection Section { get; }

        /// <summary>Re-read the underlying value(s) and redraw this row's texts.</summary>
        void Refresh();
    }
}
