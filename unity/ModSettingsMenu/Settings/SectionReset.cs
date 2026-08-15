namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Restores one section's settings to the defaults their owning mod declared at Bind().
    /// Section-scoped by design: one ModSection is one CoreLib ConfigFile is one owning mod, so
    /// a reset is one file, one owner, and one confirmable sentence. Discovered (foreign)
    /// sections are included on purpose — a reset only ever writes back the value that mod
    /// itself declared, so unlike the list-editing path it can never invent or lose a value.
    /// ReadOnly entries are skipped: view-only / server-locked is not writable at all.
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
        internal static bool Apply(ModSection section)
        {
            bool restartRelevantChange = false;
            if (section == null)
                return false;
            foreach (var def in section.Settings)
            {
                if (!IsInScope(def))
                    continue;
                var entry = def.Entry;
                var before = entry.BoxedValue;
                // CoreLib's ConfigEntry<T>.Value setter clamps to any AcceptableValue*, returns
                // early when the value is already equal, auto-saves (SaveOnConfigSet) and raises
                // SettingChanged — which is what drives every consumer's SettingHandle<T>.OnChanged.
                // So nothing here has to notify, persist or de-duplicate by hand.
                entry.BoxedValue = entry.DefaultValue;
                if (def.RequiresRestart && !object.Equals(before, entry.BoxedValue))
                    restartRelevantChange = true;
            }
            return restartRelevantChange;
        }

        private static bool IsInScope(SettingDef def) => def != null && !def.ReadOnly && def.Entry != null;
    }
}
