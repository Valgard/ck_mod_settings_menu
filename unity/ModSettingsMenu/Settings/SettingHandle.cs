using System;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Type-safe value handle the consumer holds. Wraps a CoreLib ConfigEntry&lt;T&gt;:
    /// reading Value returns the persisted/live value; setting it persists (CoreLib
    /// auto-saves) and raises OnChanged. Consumers replace their hardcoded ModConfig
    /// fields with these.
    /// </summary>
    public sealed class SettingHandle<T>
    {
        private readonly ConfigEntry<T> _entry;

        /// <summary>Fires after the value changes (menu edit, code set, or reload).</summary>
        public event Action<T> OnChanged;

        internal SettingHandle(ConfigEntry<T> entry)
        {
            _entry = entry;
            _entry.SettingChanged += (sender, args) => OnChanged?.Invoke(_entry.Value);
        }

        public T Value
        {
            get => _entry.Value;
            set => _entry.Value = value; // CoreLib clamps to AcceptableValueRange, auto-saves, raises SettingChanged
        }
    }
}
