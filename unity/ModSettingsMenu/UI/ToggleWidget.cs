using CoreLib.Data.Configuration;
using ModSettingsMenu.Settings;
using UnityEngine;

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
        private SettingDef _def;
        private ConfigEntry<bool> _entry;

        public void Bind(SettingDef def)
        {
            _def = def;
            _entry = (ConfigEntry<bool>)def.Entry;
            // The vanilla options text style tags its glyphs VisibleInsideMask on every render
            // (for the scroll viewport we removed). PugText.style is per-instance (new PugTextStyle()),
            // so set it to None once here — all renders (initial, selection, toggle) then show text.
            DisableMasking(labelText);
            DisableMasking(valueText);
            Refresh();
        }

        // Mod settings must be reachable everywhere; the vanilla default returns INACTIVE in the
        // title screen for options cloned from an in-game-only entry, which Activate() would hide.
        public override OptionActiveState GetActiveStateInCurrentScene() => OptionActiveState.ACTIVE;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            Refresh(); // re-render in the active menu state
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
            Refresh();
        }

        private void Refresh()
        {
            if (_def != null) SetText(labelText, _def.Key);
            if (_entry != null) SetText(valueText, _entry.Value ? "on" : "off");
        }

        private static void DisableMasking(PugText pt)
        {
            if (pt != null && pt.style != null) pt.style.maskInteraction = SpriteMaskInteraction.None;
        }

        // Cloned vanilla PugText inherits localize=true (→ "missing: <term>" in red);
        // render raw + re-apply the style colour.
        private static void SetText(PugText pt, string s)
        {
            if (pt == null) return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
            pt.SetTempColor(pt.color, keepColorOnStart: true);
        }
    }
}
