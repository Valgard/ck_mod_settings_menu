namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Where the selection must land after a rebuild — a row, and optionally which control inside
    /// it. The screen destroys and recreates every row on each rebuild (see ADR-005 — destroying a
    /// row is the only thing that resets PugTextEffectMenuOption.isValueText), so anything meant to
    /// survive one has to be carried across explicitly.
    ///
    /// The Slot here is NOT the old FocusedSlot, and the difference is the whole point of the
    /// UIElement-navigation rebuild. FocusedSlot was standing state on the screen — "which column
    /// is the navigation in" — consulted on every selection and therefore able to go stale, leak
    /// into an unrelated list, or fight the pointer. This is a one-shot instruction produced by the
    /// action that caused the rebuild and consumed by that same rebuild. Nothing reads it
    /// afterwards, so it cannot describe a state that has since changed.
    ///
    /// Only an action that was itself triggered from an in-row button sets it, and only so the
    /// player stays on that button: pressing ↑ four times in a row must move one entry up four
    /// times, not move it once and then walk the selection away from the arrow. Which column a
    /// *further* navigation step reaches is a different question and is answered by neighbour
    /// wiring (ListDetailScreen.ChainRowsForUIElementNavigation), never by this.
    /// </summary>
    internal readonly struct RowSelection
    {
        /// <summary>Index into _rows, or -1 for "keep the same numeric slot, clamped".</summary>
        public readonly int Row;

        /// <summary>
        /// Which in-row control to land on, or null for the row's own field. A disabled edge arrow
        /// is still a valid target: it reports ACTIVE (it has to — a GRAYED_OUT neighbour is a dead
        /// end on this navigation path, not a skip), so landing on the ↑ of a row that has just
        /// become the first one leaves the selection where the player put it, inert but reachable.
        /// </summary>
        public readonly ListRowButton.Role? Slot;

        public RowSelection(int row, ListRowButton.Role? slot = null)
        {
            Row = row;
            Slot = slot;
        }

        /// <summary>No explicit target — the rebuild keeps the previous numeric slot.</summary>
        public static RowSelection None => new RowSelection(-1);

        public bool HasRow => Row >= 0;
    }
}
