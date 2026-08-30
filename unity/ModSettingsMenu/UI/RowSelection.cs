namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Where the selection must land after a rebuild — a row, and optionally which control inside
    /// it. The screen destroys and recreates every row on each rebuild (see ADR-005 — destroying a
    /// row is the only thing that resets PugTextEffectMenuOption.isValueText), so anything meant to
    /// survive one has to be carried across explicitly.
    ///
    /// Absence — "keep the previous numeric slot, clamped" — is a null <c>RowSelection?</c>, not a
    /// value of this type. An earlier version encoded absence as <c>Row == -1</c> instead, which put
    /// two different ideas on one field: <c>Slot == null</c> is already a complete instruction ("land
    /// on the row's own field"), while <c>-1</c> was the absence of any instruction, and a type that
    /// answers both questions reads as inconsistent rather than merely economical. It also meant
    /// <c>default(RowSelection)</c> — <c>Row = 0, Slot = null</c> — was a plausible-looking "land on
    /// row 0's field" rather than "nothing." Nothing constructs a bare <c>default</c> today, but a
    /// readonly struct whose zero value is a valid-but-wrong instruction is exactly the shape of bug
    /// this design has already been bitten by once (see FocusedSlot, in the type's own history).
    ///
    /// The Slot here is NOT the old FocusedSlot, and the difference is the whole point of the
    /// UIElement-navigation rebuild. FocusedSlot was standing state on the screen — "which column
    /// is the navigation in" — consulted on every selection and therefore able to go stale, leak
    /// into an unrelated list, or fight the pointer. This is a one-shot instruction produced by the
    /// action that caused the rebuild and consumed by that same rebuild. Nothing reads it
    /// afterwards, so it cannot describe a state that has since changed.
    ///
    /// Only an action that was itself triggered from an in-row button sets Slot, and only so the
    /// player stays on that button: pressing ↑ four times in a row must move one entry up four
    /// times, not move it once and then walk the selection away from the arrow. Which column a
    /// *further* navigation step reaches is a different question and is answered by neighbour
    /// wiring (ListDetailScreen.ChainRowsForUIElementNavigation), never by this.
    /// </summary>
    internal readonly struct RowSelection
    {
        /// <summary>Index into _rows. Always valid — a caller that has nothing to land on passes a
        /// null <c>RowSelection?</c> instead of encoding "nothing" into this field.</summary>
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
    }
}
