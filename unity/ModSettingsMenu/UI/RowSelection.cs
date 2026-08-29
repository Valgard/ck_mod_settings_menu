namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Which row the selection must land on after a rebuild — an index into _rows, or "none" to
    /// keep the previous numeric slot (clamped). The screen destroys and recreates every row on
    /// each rebuild (see ADR-005 — destroying a row is the only thing that resets
    /// PugTextEffectMenuOption.isValueText), so anything meant to survive one has to be carried
    /// across explicitly.
    ///
    /// No longer carries which in-row control to land on. That was FocusedSlot's job, and
    /// FocusedSlot is gone: the in-row buttons are real, independently selectable menu options now
    /// (ListRowButton no longer opts out of isMenuOption), so a further keyboard/controller step
    /// finds the same column through neighbour wiring (ListDetailScreen.ChainRowsForUIElementNavigation)
    /// rather than through anything a rebuild has to remember. A rebuild-time target names a ROW
    /// and lands on its own field; it does not try to reselect the specific button that triggered
    /// the rebuild.
    /// </summary>
    internal readonly struct RowSelection
    {
        /// <summary>Index into _rows, or -1 for "keep the same numeric slot, clamped".</summary>
        public readonly int Row;

        public RowSelection(int row)
        {
            Row = row;
        }

        /// <summary>No explicit target — the rebuild keeps the previous numeric slot.</summary>
        public static RowSelection None => new RowSelection(-1);

        public bool HasRow => Row >= 0;
    }
}
