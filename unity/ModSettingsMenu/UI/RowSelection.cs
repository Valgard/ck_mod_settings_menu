namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Where the selection must land after a rebuild: which row, and which control inside it.
    ///
    /// A value type carrying both, rather than two loose fields on the screen, so "row without a
    /// slot" cannot exist as a half-built intermediate state. The screen destroys and recreates
    /// every row on each rebuild (see ADR-005 — destroying a row is the only thing that resets
    /// PugTextEffectMenuOption.isValueText), so anything meant to survive one has to be carried
    /// across explicitly. Before the in-row buttons that was a single int.
    /// </summary>
    internal readonly struct RowSelection
    {
        /// <summary>Index into menuOptions, or -1 for "keep the same numeric slot, clamped".</summary>
        public readonly int Row;

        /// <summary>Which in-row control to focus; null means the row's own text field.</summary>
        public readonly ListRowButton.Role? Slot;

        public RowSelection(int row, ListRowButton.Role? slot)
        {
            Row = row;
            Slot = slot;
        }

        /// <summary>No explicit target — the rebuild keeps the previous numeric slot.</summary>
        public static RowSelection None => new RowSelection(-1, null);

        public bool HasRow => Row >= 0;
    }
}
