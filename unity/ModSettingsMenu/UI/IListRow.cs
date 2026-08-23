namespace ModSettingsMenu.UI
{
    /// <summary>
    /// A row of the list drill-in that knows how tall it wants to be.
    ///
    /// Exists because the two row types sit at DIFFERENT points of CK's own hierarchy
    /// (<c>ListDetailItem : RadicalMenuOptionTextInput : RadicalMenuOption</c> versus
    /// <c>ListAddRow : RadicalMenuOption</c>), so a shared base class would have to be inserted into
    /// Pug.Other, which a mod cannot do. An interface goes around single inheritance, and the repo
    /// already does exactly this for the same reason one screen up: <c>ISectionRow</c> lets
    /// <c>ModSettingsScreen</c> treat <c>SettingWidget</c> and <c>ListWidget</c> alike.
    ///
    /// What it buys is not tidiness: <c>ListDetailScreen.RenderContent</c> used to ask each type by
    /// name and fall through to <c>0</c>. A row that answered neither question kept the prefab's
    /// <c>renderHeightPixels</c> of 0, which the LinearLayout collapses to nothing — a row that is
    /// simply not there, with no warning. Adding a row kind now cannot produce that silently; it
    /// either implements this or does not compile into the container's measuring loop.
    /// </summary>
    internal interface IListRow
    {
        /// <summary>Layout height in pixels, measured from the row's frame rather than its text.</summary>
        int RowHeightPx { get; }
    }
}
