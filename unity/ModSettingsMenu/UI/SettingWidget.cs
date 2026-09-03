using CoreLib.Data.Configuration;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One menu row for any setting kind (Toggle/Slider/Stepper/Choice). Inherits
    /// RadicalMenuOption so it joins menu navigation; labelText ("Label" child) +
    /// valueText ("Value" child) auto-assign in the base Awake. Left/right (or activate)
    /// adjusts the value via the type-agnostic ConfigEntryBase.BoxedValue; CoreLib clamps
    /// + auto-saves. Value display is per-kind. The label and a Choice's per-option text are
    /// localized through SettingDef.Label() / ValueLabel(), which own the term chain.
    /// </summary>
    public sealed class SettingWidget : RadicalMenuOption, ISectionRow
    {
        // ♦ active / ♢ inactive step glyphs (U+2666/2662), as \u escapes (pure-ASCII source —
        // a literal is encoding-unsafe in the Roslyn sandbox). thinMedium's atlas LACKS these
        // (→ '?'); they only render in boldLarge — the face CK's audio-volume value uses. Bind()
        // switches a Steps-slider's value font to boldLarge accordingly.
        private const char StepActive = '\u2666';
        private const char StepInactive = '\u2662';

        private SettingDef _def;
        private ModSection _section;

        public ModSection Section => _section;

        public void Bind(SettingDef def, ModSection section)
        {
            _def = def;
            _section = section;
            // The ♦/♢ step glyphs only render in boldLarge (thinMedium's atlas lacks them — that's
            // the face CK's audio-volume value uses). Switch this row's value font for the Steps
            // display only; every other value keeps the prefab's thinMedium.
            if (def.Kind == SettingKind.Slider && def.Display == SliderDisplay.Steps && valueText != null && valueText.style != null)
                valueText.style.fontFace = TextManager.FontFace.boldLarge;
            // ReadOnly rows (view-only / non-changeable-here server-admin setting / a shape with no
            // editable widget at all) never mutate via Adjust. Strip the interactive menu-option
            // effect from their VALUE so it no longer turns blue / pops in on selection like an
            // editable value — an editable widget (incl. a server setting a host CAN change) keeps
            // its effect. The label keeps its own effect, so the row still highlights while navigating.
            if (def.ReadOnly && valueText != null)
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
            if (_def == null || !_def.ReadOnly || menuOptionEffects == null)
                return;
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
        public override OptionActiveState GetActiveStateInCurrentScene() => _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

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

        public override bool OnSkimLeft()
        {
            Adjust(-1);
            return true;
        }

        public override bool OnSkimRight()
        {
            Adjust(+1);
            return true;
        }

        // Change the value one step in `dir` (Toggle flips regardless of sign). Toggle, Slider and
        // the bounded int Stepper write ConfigEntryBase.BoxedValue with type-exact casts; the unbounded
        // float Stepper and a NON-string Choice convert first, each for a reason local to its own case
        // below. A string Choice writes BoxedValue directly — and that is every Choice MSM's own API
        // declares, since SectionBuilder.Choice<T> always binds a string entry whatever T it is given.
        private void Adjust(int dir)
        {
            if (_def?.Entry == null)
                return;
            if (_def.ReadOnly)
                return; // read-only row: never changes, regardless of its native Kind
            var e = _def.Entry;
            var before = e.BoxedValue; // for the RequiresRestart change-detection below
            // Every write below is deliberately unwrapped — a failure has to be loud, which is the
            // whole reason the Choice case stopped going through SetSerializedValue. What it must NOT
            // be is invisible, and the direction of that is worth stating exactly, because it is the
            // opposite of what "failed" suggests: CoreLib assigns the in-memory value BEFORE it saves,
            // so a failing write leaves the entry holding the NEW value and the finally below renders
            // it. The player therefore sees the change succeed, and only the next launch disagrees.
            // The log line is the sole signal — this catch says so and attributes it to this mod.
            // A row that marked itself unsaved would be better than a log line nobody reads; the
            // reason there is none is that no such affordance exists yet, not that it was weighed.
            try
            {
                Apply(dir, e);
                // A restart-required setting that actually changed marks the menu dirty; leaving the
                // screen (ModSettingsScreen.Deactivate) then raises CK's restart prompt.
                if (_def.RequiresRestart && !object.Equals(before, e.BoxedValue))
                    ModSettingsScreen.RestartPending = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModSettingsMenu] changing '{_def.Key}' failed — the value may be set in memory but not saved: {ex}");
            }
            finally
            {
                Refresh();
            }
        }

        // The per-kind write itself. Split out of Adjust so the guard above reads as one thing.
        private void Apply(int dir, ConfigEntryBase e)
        {
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
                        if (!_def.Unbounded)
                            nv = Mathf.Clamp(nv, (int)_def.Min, (int)_def.Max);
                        e.BoxedValue = nv;
                    }
                    break;
                case SettingKind.Slider:
                    e.BoxedValue = Mathf.Clamp((float)e.BoxedValue + dir * _def.Step, _def.Min, _def.Max);
                    break;
                case SettingKind.Choice:
                {
                    var toks = _def.Tokens;
                    if (toks == null || toks.Length == 0)
                        break;
                    // Through the same renderer that produced the tokens, so the comparison happens in
                    // one string space rather than in two that used to coincide by accident: for a
                    // string the value itself, for an enum its member name, for a numeric type the
                    // converter's invariant form — which is what the write below parses back.
                    // NOT GetSerializedValue(), which escapes a string (TomlTypeConverter) and would
                    // compare, display and store the escaped form; ChoiceToken.Of exists partly to keep
                    // that exception in one place.
                    string cur = ChoiceToken.Of(e);
                    int idx = System.Array.IndexOf(toks, cur);
                    // An enum value that is not a member name is a [Flags] combination (rendered "A, B")
                    // or an undefined value — single-select cycling can't represent it, so leave it
                    // untouched rather than clobbering the .cfg to one flag. (flags editing = v2.)
                    // Any other type has no such reading and snaps to the first token below.
                    if (idx < 0 && e.SettingType.IsEnum)
                        break;
                    // Unknown/removed token -> snap to the first option; else step and wrap.
                    int next = idx < 0 ? 0 : ((idx + dir) % toks.Length + toks.Length) % toks.Length;
                    // A string is its own storage form; every other type converts here. Both tokens
                    // sources are known convertible: an enum's come from Enum.GetNames, which Toml
                    // parses back by construction, and a foreign value set's were each converted once
                    // already in ForeignConfigDiscovery.TryTokens. Deliberately NOT SetSerializedValue,
                    // which would repeat that conversion inside a catch(Exception) that also wraps the
                    // assignment — so a throwing foreign Clamp, or a failing save, came back as a
                    // warning about a *parse*, attributed to CoreLib, one line below a string branch
                    // where the same fault is loud. (Not a SettingChanged subscriber: ConfigFile wraps
                    // each of those itself.) Same write, same visibility, either way.
                    if (e.SettingType == typeof(string))
                        e.BoxedValue = toks[next];
                    else
                        e.BoxedValue = TomlTypeConverter.ConvertToValue(toks[next], e.SettingType);
                    break;
                }
            }
        }

        public void Refresh()
        {
            if (_def == null)
                return;
            SetText(labelText, _def.Label()); // localized; falls back to the raw key
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
                case SettingKind.Toggle:
                    return (bool)e.BoxedValue ? Loc.T("ModSettingsMenu-UI/On") : Loc.T("ModSettingsMenu-UI/Off");
                case SettingKind.Stepper:
                    return e.SettingType == typeof(float)
                        ? ((float)e.BoxedValue).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)
                        : ((int)e.BoxedValue).ToString();
                case SettingKind.Choice:
                {
                    string tok = ChoiceToken.Of(e); // same read as Adjust — see there
                    return _def.ValueLabel(tok); // localized per-option; falls back to the raw token
                }
                case SettingKind.Slider:
                {
                    float v = (float)e.BoxedValue;
                    float frac = (_def.Max - _def.Min) > 0f ? (v - _def.Min) / (_def.Max - _def.Min) : 0f;
                    switch (_def.Display)
                    {
                        case SliderDisplay.Number:
                            return v.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
                        case SliderDisplay.Percent:
                            return Mathf.RoundToInt(frac * 100f) + "%";
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
            if (pt == null)
                return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
        }
    }
}
