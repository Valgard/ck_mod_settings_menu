using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Restores one section's settings to the defaults their owning mod declared at Bind().
    /// Section-scoped by design: one ModSection is one CoreLib ConfigFile is one owning mod, so
    /// a reset is one file, one owner, and one confirmable sentence. Discovered (foreign)
    /// sections are included on purpose — a reset only ever writes back the value that mod
    /// itself declared, so unlike the list-editing path it can never invent or lose a value.
    /// A non-editable row is always skipped — ask SettingDef.IsEditable, the single conjunction of
    /// the two reasons a row can be inert: a genuine permission lock (SettingDef.Locked — view-only
    /// / server-locked and not this session's host) and a Kind == Info fallback where no editable
    /// widget exists for the value's shape at all — the latter is writable, but this menu never
    /// showed it as such.
    ///
    /// A list declared ListEditing.ReadOnly is a THIRD kind of row the menu shows as unchangeable,
    /// and it is deliberately NOT skipped: SettingDef.Locked stays false for it, because the lock
    /// is the consumer's declaration rather than a permission. So a reset restores it, which is
    /// right — the value is the mod's to define, the reset writes back exactly what the mod
    /// declared, and it is the only way a stale entry in such a list can ever clear. Two rows that
    /// look equally uneditable on screen therefore behave differently here, by design.
    /// </summary>
    internal static class SectionReset
    {
        /// <summary>True when this section has at least one entry a reset could write.</summary>
        internal static bool CanReset(ModSection section)
        {
            if (section == null)
                return false;
            foreach (var def in section.Settings)
                if (IsInScope(def))
                    return true;
            return false;
        }

        /// <summary>
        /// Writes every in-scope entry back to its declared default.
        /// Returns true when at least one RequiresRestart setting actually changed, so the
        /// caller can raise the restart flag. Deliberately returns that instead of setting
        /// ModSettingsScreen.RestartPending itself — Settings must not depend on UI.
        /// </summary>
        internal static bool ApplyAndCheckRestart(ModSection section)
        {
            if (section == null)
                return false;
            bool restartRelevantChange = false;
            ConfigEntryBase written = null;
            foreach (var def in section.Settings)
            {
                if (!IsInScope(def))
                    continue;
                var entry = def.Entry;
                var before = entry.BoxedValue;
                // CoreLib's ConfigEntry<T>.Value setter clamps to any AcceptableValue*, returns
                // early when the value is already equal, and raises SettingChanged — which is what
                // drives every consumer's SettingHandle<T>.OnChanged. So nothing here has to notify
                // or de-duplicate by hand. Persisting is the exception: see below.
                entry.BoxedValue = entry.DefaultValue;
                written = entry;
                if (def.RequiresRestart && !object.Equals(before, entry.BoxedValue))
                    restartRelevantChange = true;
            }
            // One save for the whole reset, after the loop: a section is one ConfigFile, so the last
            // entry written reaches it. CoreLib's own per-write save hangs on SaveOnConfigSet, which
            // another mod can switch off (ConfigStore.Persist).
            ConfigStore.Persist(written);
            return restartRelevantChange;
        }

        // Ask IsEditable, not Locked or Kind separately: that property is the one place the
        // conjunction of the two exclusions lives now. This method is not where that lesson was
        // learned — the ReadOnly split (9b26405) carried both halves through here from the start.
        // It was SettingWidget, whose three guards each re-derived the pair and dropped the
        // structural half, so a discovered Client-scoped Info row rendered as interactive
        // (83e559b). Re-deriving the conjunction is the mistake; where it is re-derived only
        // decides which symptom appears. An Info row is writable — but this menu has never shown
        // it as changeable, so writing it on a reset would be a value changing where the player
        // was told nothing can.
        private static bool IsInScope(SettingDef def) => def != null && def.IsEditable;
    }
}
