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
    /// simply not there, with no warning.
    ///
    /// This is not a compile-time guarantee — <c>GetComponent&lt;IListRow&gt;()</c> compiles and
    /// returns null for any type that does not implement it, exactly like asking by name did. What
    /// changes is that the measuring loop now checks for that null and logs, naming the object,
    /// instead of letting <c>?? 0</c> reproduce the same silent collapse under a different name.
    /// </summary>
    internal interface IListRow
    {
        /// <summary>Layout height in pixels, measured from the row's frame rather than its text.</summary>
        int RowHeightPx { get; }
    }
}
