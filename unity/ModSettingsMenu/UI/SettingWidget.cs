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
            // Info rows are the currently-non-editable settings: foreign discovery routes every
            // view-only / non-changeable-here (server/admin as guest or at the title) / unrenderable
            // entry to Info, and Adjust never mutates one. Strip the interactive menu-option effect
            // from their VALUE so it no longer turns blue / pops in on selection like an editable
            // value — an editable widget (incl. a server setting a host CAN change) keeps its effect.
            // The label keeps its own effect, so the row still highlights while navigating.
            if (def.Kind == SettingKind.Info && valueText != null)
                MakeValueReadOnly();
            Refresh();
        }

        // Make the value render as a static read-only string. CK drives its PugTextEffects through
        // several independent paths; this covers the ones anchored on the value's own components:
        //   - PugText.ManagedLateUpdate ticks only ENABLED effects → disable both value effects so
        //     the colour transition and the juicy-appear pop-in never animate.
        //   - PugText.ResetEffects re-applies effects on every Render regardless of enabled → set
        //     dontResetEffectsOnRender so a render while the row is selected can't repaint the value.
        //     Safe because the effects are disabled (their LateUpdate — incl. JuicyAppear's glyph-timer
        //     read — never runs, so skipping ResetEffect can't leave it null-deref).
        //   - lock the value to CK's static deselected tone so it reads as a plain read-only value.
        // The remaining path — RadicalMenuOption.OnSelected/OnDeselected recolouring the value blue —
        // runs off menuOptionEffects, which base.Awake fills AFTER Bind, so it's handled at the point
        // of use in SuppressValueSelectionEffect (below).
        private void MakeValueReadOnly()
        {
            foreach (var fx in valueText.GetComponents<PugTextEffect>())
                fx.enabled = false;
            valueText.dontResetEffectsOnRender = true;
            if (valueText.style != null)
                valueText.style.color = PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR;
        }

        // RadicalMenuOption.OnSelected/OnDeselected recolour every menuOptionEffect DIRECTLY (they
        // ignore MonoBehaviour.enabled). base.Awake fills menuOptionEffects AFTER our Bind runs, so it
        // can't be filtered there — drop the value's effect (isValueText) right before base acts. The
        // label's effect stays, so the row still highlights for navigation. Idempotent + cheap.
        private void SuppressValueSelectionEffect()
        {
            if (_def == null || _def.Kind != SettingKind.Info || menuOptionEffects == null) return;
            menuOptionEffects = System.Array.FindAll(menuOptionEffects, fx => fx != null && !fx.isValueText);
        }

        public override void OnSelected()
        {
            SuppressValueSelectionEffect();
            base.OnSelected();
        }

        public override void OnDeselected(bool playEffect = true)
        {
            SuppressValueSelectionEffect();
            base.OnDeselected(playEffect);
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

        // Change the value one step in `dir` (Toggle flips regardless of sign). Numeric writes go
        // through ConfigEntryBase.BoxedValue with type-exact casts; foreign Choice round-trips via
        // the serialized value (BoxedValue of a foreign enum is not a string).
        private void Adjust(int dir)
        {
            if (_def?.Entry == null) return;
            if (_def.Kind == SettingKind.Info) return;   // read-only row: never changes
            var e = _def.Entry;
            var before = e.BoxedValue;   // for the RequiresRestart change-detection below
            switch (_def.Kind)
            {
                case SettingKind.Toggle:
                    e.BoxedValue = !(bool)e.BoxedValue;
                    break;
                case SettingKind.Stepper:
                    if (e.SettingType == typeof(float))
                    {
                        // Foreign unbounded float stepper: step by _def.Step, no bounds. Store the
                        // DISPLAYED value verbatim (SetSerializedValue) so the .cfg matches the row to
                        // the decimal: float arithmetic like 0.1f-0.05f would otherwise persist noise
                        // (0.09999993). Formatting to the same "0.0##" the row shows, then re-parsing,
                        // yields the canonical float; the next step re-reads that clean value.
                        float stepped = (float)e.BoxedValue + dir * _def.Step;
                        e.SetSerializedValue(stepped.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        int nv = (int)e.BoxedValue + dir;
                        if (!_def.Unbounded) nv = Mathf.Clamp(nv, (int)_def.Min, (int)_def.Max);
                        e.BoxedValue = nv;
                    }
                    break;
                case SettingKind.Slider:
                    e.BoxedValue = Mathf.Clamp((float)e.BoxedValue + dir * _def.Step, _def.Min, _def.Max);
                    break;
                case SettingKind.Choice:
                {
                    var toks = _def.Tokens;
                    if (toks == null || toks.Length == 0) break;
                    string cur = _def.Foreign ? e.GetSerializedValue() : (string)e.BoxedValue;
                    int idx = System.Array.IndexOf(toks, cur);
                    // A foreign value not among the member names is a [Flags] combination (serialized
                    // "A, B") or an undefined value — single-select cycling can't represent it, so leave
                    // it untouched rather than clobbering the .cfg to one flag. (flags editing = v2.)
                    if (idx < 0 && _def.Foreign) break;
                    // Unknown/removed token -> snap to the first option; else step and wrap.
                    int next = idx < 0 ? 0 : ((idx + dir) % toks.Length + toks.Length) % toks.Length;
                    if (_def.Foreign) e.SetSerializedValue(toks[next]);
                    else e.BoxedValue = toks[next];
                    break;
                }
            }
            // A restart-required setting that actually changed marks the menu dirty; leaving the
            // screen (ModSettingsScreen.Deactivate) then raises CK's restart prompt.
            if (_def.RequiresRestart && !object.Equals(before, e.BoxedValue))
                ModSettingsScreen.RestartPending = true;
            Refresh();
        }

        private void Refresh()
        {
            if (_def == null) return;
            SetText(labelText, Loc.T(_def.Term, _def.Key));   // localized; falls back to the raw key
            SetText(valueText, ValueString());
        }

        private string ValueString()
        {
            var e = _def.Entry;
            switch (_def.Kind)
            {
                case SettingKind.Info:
                {
                    // Read-only: show the raw value (BoxedValue.ToString, NOT the escaped serialized
                    // form), truncated so a long string (e.g. a comma-list) can't overflow the row.
                    var v = e.BoxedValue;
                    string s = v == null ? "" : v.ToString();
                    return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
                }
                case SettingKind.Toggle:  return (bool)e.BoxedValue ? Loc.T("ModSettingsMenu-UI/On") : Loc.T("ModSettingsMenu-UI/Off");
                case SettingKind.Stepper:
                    return e.SettingType == typeof(float)
                        ? ((float)e.BoxedValue).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)
                        : ((int)e.BoxedValue).ToString();
                case SettingKind.Choice:
                {
                    string tok = _def.Foreign ? e.GetSerializedValue() : (string)e.BoxedValue;
                    return Loc.T(_def.Term + "/" + tok, tok);   // localized per-option; foreign -> raw token
                }
                case SettingKind.Slider:
                {
                    float v = (float)e.BoxedValue;
                    float frac = (_def.Max - _def.Min) > 0f ? (v - _def.Min) / (_def.Max - _def.Min) : 0f;
                    switch (_def.Display)
                    {
                        case SliderDisplay.Number:  return v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
                        case SliderDisplay.Percent: return Mathf.RoundToInt(frac * 100f) + "%";
                        default: // Steps: diamond chain (boldLarge, set in Bind), segments = (Max-Min)/Step
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
