using System;
using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Type-safe value handle the consumer holds. Delegate-backed so it can front two
    /// stores: a CoreLib ConfigEntry&lt;T&gt; directly (Toggle/Slider/Stepper), or a
    /// string-token ConfigEntry&lt;string&gt; mapped to/from T (Choice&lt;T&gt;, whose T
    /// may be any type — the token = value.ToString()). Reading Value returns the live
    /// value; setting it persists (CoreLib auto-saves) and raises OnChanged.
    /// </summary>
    public sealed class SettingHandle<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        /// <summary>Fires after the value changes (menu edit, code set, or reload).</summary>
        public event Action<T> OnChanged;

        // Toggle/Slider/Stepper: T is a CoreLib-supported type; back straight onto ConfigEntry<T>.
        internal SettingHandle(ConfigEntry<T> entry)
        {
            _get = () => entry.Value;
            _set = v => entry.Value = v;
            entry.SettingChanged += (s, a) => OnChanged?.Invoke(_get());
        }

        // Choice<T>: store a string token; map token <-> T so the consumer still sees T.
        internal SettingHandle(ConfigEntry<string> tokenEntry, Func<string, T> fromToken, Func<T, string> toToken)
        {
            _get = () => fromToken(tokenEntry.Value);
            _set = v => tokenEntry.Value = toToken(v);
            tokenEntry.SettingChanged += (s, a) => OnChanged?.Invoke(_get());
        }

        public T Value
        {
            get => _get();
            set => _set(value); // CoreLib clamps to any AcceptableValue*, auto-saves, raises SettingChanged
        }
    }
}
