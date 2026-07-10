using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One menu row for any setting kind (Toggle/Slider/Stepper/Choice). Inherits
    /// RadicalMenuOption so it joins menu navigation; labelText ("Label" child) +
    /// valueText ("Value" child) auto-assign in the base Awake. Left/right (or activate)
    /// adjusts the value via the type-agnostic ConfigEntryBase.BoxedValue; CoreLib clamps
    /// + auto-saves. Value display is per-kind. Label is the raw key for now (Phase 5
    /// swaps to Loc.T(_def.Term); Choice value likewise Loc.T(term/token) ?? token).
    /// </summary>
    public sealed class SettingWidget : RadicalMenuOption
    {
        // ♦ active / ♢ inactive step glyphs (U+2666/2662), as \u escapes (pure-ASCII source —
        // a literal is encoding-unsafe in the Roslyn sandbox). thinMedium's atlas LACKS these
        // (→ '?'); they only render in boldLarge — the face CK's audio-volume value uses. Bind()
        // switches a Steps-slider's value font to boldLarge accordingly.
        private const char StepActive = '\u2666';
        private const char StepInactive = '\u2662';

        private SettingDef _def;

        public void Bind(SettingDef def)
        {
            _def = def;
            // The ♦/♢ step glyphs only render in boldLarge (thinMedium's atlas lacks them — that's
            // the face CK's audio-volume value uses). Switch this row's value font for the Steps
            // display only; every other value keeps the prefab's thinMedium.
            if (def.Kind == SettingKind.Slider && def.Display == SliderDisplay.Steps
                && valueText != null && valueText.style != null)
                valueText.style.fontFace = TextManager.FontFace.boldLarge;
            Refresh();
        }

        // Only bound rows activate; the inactive template (never bound → _def null) stays hidden.
        public override OptionActiveState GetActiveStateInCurrentScene()
            => _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            Refresh(); // re-render in the active menu state
        }

        public override void OnActivated()
        {
            base.OnActivated();
            Adjust(+1); // click/Space steps forward, like CK's stepper
        }

        public override bool OnSkimLeft()  { Adjust(-1); return true; }
        public override bool OnSkimRight() { Adjust(+1); return true; }

        // Change the value one step in `dir` (Toggle flips regardless of sign). All writes go
        // through ConfigEntryBase.BoxedValue → CoreLib clamps to the AcceptableValue* + auto-saves.
        private void Adjust(int dir)
        {
            if (_def?.Entry == null) return;
            var e = _def.Entry;
            switch (_def.Kind)
            {
                case SettingKind.Toggle:
                    e.BoxedValue = !(bool)e.BoxedValue;
                    break;
                case SettingKind.Stepper:
                    e.BoxedValue = Mathf.Clamp((int)e.BoxedValue + dir, (int)_def.Min, (int)_def.Max);
                    break;
                case SettingKind.Slider:
                    e.BoxedValue = Mathf.Clamp((float)e.BoxedValue + dir * _def.Step, _def.Min, _def.Max);
                    break;
                case SettingKind.Choice:
                {
                    var toks = _def.Tokens;
                    if (toks == null || toks.Length == 0) break;
                    int cur = System.Array.IndexOf(toks, (string)e.BoxedValue);
                    if (cur < 0) cur = 0;
                    int next = ((cur + dir) % toks.Length + toks.Length) % toks.Length; // wrap
                    e.BoxedValue = toks[next];
                    break;
                }
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_def == null) return;
            SetText(labelText, _def.Key);      // Phase 5: Loc.T(_def.Term) ?? _def.Key
            SetText(valueText, ValueString());
        }

        private string ValueString()
        {
            var e = _def.Entry;
            switch (_def.Kind)
            {
                case SettingKind.Toggle:  return (bool)e.BoxedValue ? "on" : "off";
                case SettingKind.Stepper: return ((int)e.BoxedValue).ToString();
                case SettingKind.Choice:  return (string)e.BoxedValue;  // Phase 5: Loc.T(term/token) ?? token
                case SettingKind.Slider:
                {
                    float v = (float)e.BoxedValue;
                    float frac = (_def.Max - _def.Min) > 0f ? (v - _def.Min) / (_def.Max - _def.Min) : 0f;
                    switch (_def.Display)
                    {
                        // Always >=1 decimal, dot separator (4 -> "4.0", 4.5 -> "4.5").
                        case SliderDisplay.Number:  return v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
                        case SliderDisplay.Percent: return Mathf.RoundToInt(frac * 100f) + "%";
                        default: // Steps: ♦/♢ chain (boldLarge, set in Bind), segments = (Max-Min)/Step
                        {
                            int seg = Mathf.Max(1, Mathf.RoundToInt((_def.Max - _def.Min) / _def.Step));
                            int n = Mathf.Clamp(Mathf.RoundToInt(frac * seg), 0, seg);
                            return new string(StepActive, n) + new string(StepInactive, seg - n);
                        }
                    }
                }
            }
            return "";
        }

        // Cloned vanilla PugText inherits localize=true (resolves the string as a loc term);
        // render raw instead. Colour + maskInteraction come from the prefab style.
        private static void SetText(PugText pt, string s)
        {
            if (pt == null) return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
        }
    }
}
