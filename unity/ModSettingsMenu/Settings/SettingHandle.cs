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

        // A handle with no store behind it, for a setting whose bind failed. It reports the
        // declared default and swallows writes, so a consumer that reads its own setting in a hot
        // path keeps working on the value it declared instead of dereferencing null. The setting is
        // simply absent from the menu — SectionBuilder logs why, and does not register a row.
        //
        // Deliberately not throwing on write: the failure is CoreLib's or the filesystem's, not the
        // consumer's, and a mod should not die in a setter because its config could not be created.
        //
        // NOT equivalent to a bound handle, in two ways a consumer could notice. It does not clamp
        // (a real one routes writes through CoreLib's AcceptableValueRange/List, so `Value = 999` on
        // a 0..10 slider reads back 999 here), and it raises no OnChanged, so a consumer that
        // recomputes from that event never recomputes. Both are acceptable on a path that has
        // already failed and is logged as such — the point is that the mod keeps running — but they
        // are worth knowing before treating this handle as a drop-in.
        internal SettingHandle(T detachedDefault)
        {
            T value = detachedDefault;
            _get = () => value;
            _set = v => value = v;
        }

        public T Value
        {
            get => _get();
            set => _set(value); // CoreLib clamps to any AcceptableValue*, auto-saves, raises SettingChanged
        }
    }
}
