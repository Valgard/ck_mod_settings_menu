using CoreLib.Data.Configuration;
using ModSettingsMenu.Settings;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Boolean toggle menu option bound to a SettingDef's ConfigEntry&lt;bool&gt;.
    /// Inherits RadicalMenuOption so it joins menu navigation; labelText ("Label"
    /// child) + valueText ("Value" child) auto-assign in the base Awake. Left/right
    /// or activate flips the value; CoreLib auto-saves. Label is the raw key for
    /// now (Phase 3 swaps to Loc.T).
    /// </summary>
    public sealed class ToggleWidget : RadicalMenuOption
    {
        private ConfigEntry<bool> _entry;

        public void Bind(SettingDef def)
        {
            _entry = (ConfigEntry<bool>)def.Entry;
            if (labelText != null) labelText.Render(def.Key);
            UpdateText();
        }

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            UpdateText();
        }

        public override void OnActivated()
        {
            base.OnActivated();
            Flip();
        }

        public override bool OnSkimLeft()  { Flip(); return true; }
        public override bool OnSkimRight() { Flip(); return true; }

        private void Flip()
        {
            if (_entry == null) return;
            _entry.Value = !_entry.Value; // CoreLib auto-saves + raises SettingChanged
            UpdateText();
        }

        private void UpdateText()
        {
            if (_entry != null && valueText != null)
                valueText.Render(_entry.Value ? "on" : "off");
        }
    }
}
