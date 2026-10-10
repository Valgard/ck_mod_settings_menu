using System;
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
    /// and ConfigStore.Persist saves. Value display is per-kind. The label and a Choice's per-option text are
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

        /// <summary>What a withheld row's text looked like before MSM painted it, so lifting the lock
        /// can hand back the row it found rather than a guess at it.
        ///
        /// Invariant, and the one thing both users owe this type: a text may be painted only if its
        /// memo is <see cref="Restorable"/>. The check therefore sits at the PAINT, not at the capture
        /// — a precondition guarded only where the memo is taken would let a row be painted that can
        /// never be handed back, since painting repeats and the capture need not. Callers refuse to
        /// paint rather than retry the capture, so "painted" and "restorable" cannot come apart. A
        /// missing text is restorable: there is nothing to paint and nothing to give back.
        ///
        /// How often a memo is taken is each user's own business, and the two differ.
        /// <see cref="SettingWidget.CapturePaint"/> is one-shot on <c>_paintCaptured</c>, because it
        /// also captures the value effects' enabled states and must hand back the prefab's.
        /// <see cref="ListWidget"/> captures while <c>!_lockPainted</c> instead, so it re-takes the memo
        /// on every unpainted call. Same guarantee either way — an unpainted row's texts are whatever
        /// the last render left, so a re-capture reads the same state a one-shot one kept.</summary>
        internal struct TextPaintMemo
        {
            private Color _color;
            private bool _dontResetEffectsOnRender;
            private bool _present;

            public bool Restorable { get; private set; }

            public static TextPaintMemo Of(PugText t) =>
                t == null
                    ? new TextPaintMemo { Restorable = true }
                    : new TextPaintMemo
                    {
                        _color = t.style != null ? t.style.color : default,
                        _dontResetEffectsOnRender = t.dontResetEffectsOnRender,
                        _present = true,
                        Restorable = t.style != null,
                    };

            public void RestoreTo(PugText t)
            {
                if (t == null || !_present || !Restorable)
                    return;
                t.dontResetEffectsOnRender = _dontResetEffectsOnRender;
                t.style.color = _color;
                t.SetTempColor(_color);
            }
        }

        /// <summary>The selected tone of a locked row: CK's selection factor applied channel-wise to
        /// UNSELECTABLE_TEXT_COLOR, so it reads as a lighter red rather than as selection blue
        /// (spec § 4.4 gives the derivation).</summary>
        internal static readonly Color LockedSelectedColor = new Color(0.642f, 0.263f, 0.277f, 1f);

        /// <summary>The one write a withheld row's text takes, shared with ListWidget: stop CK's render
        /// path from repainting it (dontResetEffectsOnRender), then set the base colour a render would
        /// use and the live glyphs, which a render does not run for.</summary>
        internal static void PaintLocked(PugText t, Color c)
        {
            if (t == null || t.style == null)
                return;
            t.dontResetEffectsOnRender = true;
            t.style.color = c;
            t.SetTempColor(c);
        }

        /// <summary>One row's subscription to its entry's file, so an outside write reaches the row.
        /// <c>ConfigFile.SettingChanged</c> is the only event reachable over the <c>ConfigEntryBase</c>
        /// MSM holds (the per-entry one lives on <c>ConfigEntry&lt;T&gt;</c>), and it fires for every entry
        /// in the file, so the handler filters on the one entry watched.
        ///
        /// Lifetime: the file outlives every row (rows are rebuilt on each open, the file is cached by
        /// ConfigStore), so a handler left behind keeps a destroyed row alive and fires on it. The
        /// subscribe and the unsubscribe therefore sit in one place and cannot drift apart: <see cref="Watch"/>
        /// first <see cref="Stop"/>s whatever was watched, which makes a second Bind of the same row replace
        /// its subscription rather than add one, and <see cref="Stop"/> removes the one delegate instance
        /// from the file it was added to — not from wherever the entry points now.</summary>
        internal sealed class EntryWatch
        {
            private readonly EventHandler<SettingChangedEventArgs> _handler;
            private ConfigFile _file;
            private ConfigEntryBase _entry;

            public EntryWatch(Action onChanged)
            {
                _handler = (sender, args) =>
                {
                    if (args.ChangedSetting == _entry)
                        onChanged();
                };
            }

            public void Watch(ConfigEntryBase entry)
            {
                Stop();
                if (entry == null || entry.ConfigFile == null)
                    return;
                _entry = entry;
                _file = entry.ConfigFile;
                _file.SettingChanged += _handler;
            }

            public void Stop()
            {
                if (_file != null)
                    _file.SettingChanged -= _handler;
                _file = null;
                _entry = null;
            }
        }

        private SettingDef _def;
        private ModSection _section;
        private EntryWatch _watch;

        // What this row looked like before MSM first touched it. Per-widget fields on purpose: Populate
        // destroys and rebuilds every row, so nothing here can outlive the row it describes — PreWarm's
        // load-time rows, where every scoped row is locked, are discarded with the rest.
        private bool _paintCaptured;
        private TextPaintMemo _labelMemo;
        private TextPaintMemo _valueMemo;
        private PugTextEffect[] _valueEffects;
        private bool[] _valueEffectsEnabled;
        private bool _effectsCaptured;
        private PugTextEffectMenuOption[] _origMenuOptionEffects;
        private bool _lockPainted; // we hold a mutation that has to be handed back when the lock lifts
        private bool _paintRefusalWarned; // latched: ApplyLockAppearance is reached per selection change

        public ModSection Section => _section;

        public void Bind(SettingDef def, ModSection section)
        {
            // A Label is not this widget's to render, and the failure is silent without this:
            // ValueString has no Label case, so it returns "" without ever touching the null Entry
            // — nothing throws, and the result is a SELECTABLE, empty-valued row registered in
            // menuOptions, which is precisely what the heading design exists to prevent.
            // ModSettingsScreen.Populate routes labels to LabelRow before reaching here; that one
            // branch is the whole guarantee, so this says so loudly if it ever stops being true.
            // Returning early leaves _def null, and GetActiveStateInCurrentScene then reports
            // INACTIVE — so the row drops out of navigation instead of becoming the very thing
            // being guarded against.
            if (def != null && def.Kind == SettingKind.Label)
            {
                Debug.LogError(
                    $"[ModSettingsMenu] internal: the label '{def.Key}' reached SettingWidget — a heading renders through LabelRow, not here. Row skipped."
                );
                return;
            }
            _def = def;
            _section = section;
            // The ♦/♢ step glyphs only render in boldLarge (thinMedium's atlas lacks them — that's
            // the face CK's audio-volume value uses). Switch this row's value font for the Steps
            // display only; every other value keeps the prefab's thinMedium.
            if (def.Kind == SettingKind.Slider && def.Display == SliderDisplay.Steps && valueText != null && valueText.style != null)
                valueText.style.fontFace = TextManager.FontFace.boldLarge;
            // A non-editable row (locked, or a Kind == Info shape with no editable widget at all —
            // ask IsEditable, not Locked alone) never mutates via Adjust. Strip the interactive
            // menu-option effect from their VALUE so it no longer turns blue / pops in on selection
            // like an editable value — an editable widget (incl. a server setting a host CAN
            // change) keeps its effect. The label keeps its own effect, so the row still highlights
            // while navigating.
            if (!def.IsEditable && valueText != null)
                MakeValueReadOnly();
            if (_watch == null)
                _watch = new EntryWatch(OnEntryChanged);
            _watch.Watch(def.Entry);
            Refresh();
        }

        // This row's entry was written — and most often by this row itself, which is the part that was
        // worth getting right here. Adjust writes BoxedValue and CoreLib raises the file event
        // synchronously (ConfigEntryBase.cs:42 → :134 → ConfigFile.cs:504, invoked at :517), so this
        // runs INSIDE Apply, before Adjust's own finally { Refresh(); }. A section reset arrives the
        // same way (SectionReset.cs:56 writes BoxedValue per entry, and RefreshSection then redraws
        // the same rows a second time). The other writers are a second row over the same entry
        // (testDupKeyAccess) and a mod's own gameplay code. All of them land here, which is why this
        // body has to be idempotent rather than transition-shaped. The exception is a write that
        // changes nothing: CoreLib's setter returns before the event when the new value equals the old
        // (ConfigEntryBase.cs:38), so a step that clamps back to where it already was is silent.
        //
        // Goes through Refresh, which already runs the lock appearance in the order its contract
        // needs, so no tint or lock pass here.
        //
        // A row whose screen is not the active one is skipped, not rendered: the list drill-in keeps this
        // screen's rows alive but inactive. Two reasons. Rendering them would be wasted work, since the
        // screen's Activate rebuilds every row from the live values on return. And the re-measure below
        // could not work: LinearLayout counts inactive children as zero height, so a layout pass over an
        // inactive hierarchy would size the rows against zero-height siblings.
        //
        // The row's height is measured once, in Populate, so a value that now wraps differently (a
        // Kind == Info row showing a foreign string) is re-measured here; ModSettingsScreen.RemeasureRow
        // relays out only when the height moved.
        //
        // Guarded like FollowPermissionChange's stages, and worth naming what can actually throw,
        // because the case this clause used to name was looked for and not reproduced: a Kind whose
        // unboxing cast disagrees with its entry's runtime type would need a producer that does not
        // pin the two together, and both pin them per Kind (ForeignConfigDiscovery.BuildDef's type
        // tests, SectionBuilder's typed overloads). What is reachable is the foreign material this
        // path handles — Label()/ValueLabel() → Loc.TFirstOf → API.Localization, called with term
        // strings composed from a foreign config's file path and keys; PugText.Render on arbitrary
        // foreign text; and RemeasureRow's layout pass below, which runs inside this same try. The
        // casts stay guarded all the same: this costs one row, and ConfigFile would log any of these
        // without saying whose row it was. Unlike a permission transition nothing is consumed here, so
        // the next write to the entry simply tries again.
        private void OnEntryChanged()
        {
            // A destroyed row whose OnDestroy never ran (Unity skips it for an object that was never
            // active) is the one way a handler outlives its row. This ends it at the first write to this
            // row's entry; on an entry nothing writes again it never fires, and the handler stays.
            if (this == null)
            {
                _watch.Stop();
                return;
            }
            try
            {
                if (gameObject.activeInHierarchy)
                {
                    Refresh();
                    GetComponentInParent<ModSettingsScreen>()?.RemeasureRow(gameObject, ModSettingsScreen.SettingRowHeightPx(this));
                }
            }
            catch (Exception e)
            {
                // The owner, not the key alone: on the discovery path a key is a foreign mod's bare
                // field name, and the fixture set alone has several that repeat across files.
                Debug.LogError($"[ModSettingsMenu] refreshing '{_section?.ModId}/{_def?.Key}' after an outside change failed: {e}");
            }
        }

        public void StopWatching() => _watch?.Stop();

        // The matching end of Bind's subscription. Populate destroys every row on every open, including
        // the Activate that resumes the screen after a drill-in, so destruction is the lifecycle that
        // pairs with it. Deactivate(pop: false) needs nothing: the rows survive it and stay subscribed.
        private void OnDestroy()
        {
            if (_watch != null)
                _watch.Stop();
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
            CapturePaint();
            DisableValueEffects();
            valueText.dontResetEffectsOnRender = true;
            if (valueText.style != null)
                valueText.style.color = PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR;
        }

        // Both value effects off, so the colour transition and the pop-in never animate. The prior state
        // is in _valueEffectsEnabled (CapturePaint runs first at both call sites), which is what
        // ReleaseLockAppearance hands back.
        private void DisableValueEffects()
        {
            foreach (var fx in valueText.GetComponents<PugTextEffect>())
                fx.enabled = false;
        }

        // RadicalMenuOption.OnSelected/OnDeselected recolour every menuOptionEffect DIRECTLY (they
        // ignore MonoBehaviour.enabled). base.Awake fills menuOptionEffects AFTER our Bind runs, so it
        // can't be filtered there — drop the value's effect (isValueText) right before base acts. The
        // label's effect stays, so the row still highlights for navigation. Idempotent + cheap.
        private void SuppressValueSelectionEffect()
        {
            if (_def == null || _def.IsEditable || menuOptionEffects == null)
                return;
            CaptureEffects(); // before the filter, so lifting a lock never hands back a thinned array
            menuOptionEffects = System.Array.FindAll(menuOptionEffects, fx => fx != null && !fx.isValueText);
        }

        // Record what the row looked like before MSM's first write to it. Called at the point of the
        // first mutation, never from Bind: base.Awake fills menuOptionEffects AFTER Bind runs, so a
        // capture there would save a null and the restore would wipe the row's effects.
        private void CaptureEffects()
        {
            if (_effectsCaptured || menuOptionEffects == null)
                return;
            _origMenuOptionEffects = menuOptionEffects;
            _effectsCaptured = true;
        }

        private void CapturePaint()
        {
            if (_paintCaptured || valueText == null)
                return;
            _labelMemo = TextPaintMemo.Of(labelText);
            _valueMemo = TextPaintMemo.Of(valueText);
            _valueEffects = valueText.GetComponents<PugTextEffect>();
            _valueEffectsEnabled = System.Array.ConvertAll(_valueEffects, fx => fx.enabled);
            _paintCaptured = true;
        }

        /// <summary>Paints the row for its current lock state, or hands back what it painted. Idempotent;
        /// reads WithheldNow and the selection itself, so a caller cannot disagree with the row about
        /// its own state. Spec § 4.4: CK's own writes are silenced in two steps (the selection path via
        /// menuOptionEffects, the render path via dontResetEffectsOnRender), and then only MSM writes.
        ///
        /// The two overrides below pass the selection state explicitly instead, because during
        /// OnDeselected CK has not yet moved its selection (SelectOptionIndex deselects the old row
        /// BEFORE assigning selectedIndex), so IsSelected() still answers true for the row being left.</summary>
        internal void ApplyLockAppearance() => ApplyLockAppearance(IsSelected());

        private void ApplyLockAppearance(bool selected)
        {
            if (_def == null)
                return;
            if (!_def.WithheldNow)
            {
                ReleaseLockAppearance();
                return;
            }
            CapturePaint();
            if (!_labelMemo.Restorable || !_valueMemo.Restorable)
            {
                // Refuse to paint what cannot be handed back (see TextPaintMemo) — and say so, since
                // this is the one branch in this widget where a withheld row comes out unmarked.
                // Restorable is false exactly when a PugText is present with no style, so the
                // condition reports a prefab that is not wired as expected, which is what the
                // template guards in ModSettingsScreen warn about.
                //
                // How such a row then LOOKS is deliberately not predicted here, because CK's own
                // colouring reaches the very same field: PugText.SetTempColor dereferences style
                // (Pug.Other:368127), and that is the path this text's own PugTextEffectMenuOption
                // takes on every render (ResetEffect → EndEffectImmediate, :366078 → :366114). An
                // unstyled text is therefore a fault for the game as much as for MSM. What is certain
                // is the part worth logging: nothing MSM owns marks this row as withheld. Latched per
                // row, because this is reached on every selection change.
                if (!_paintRefusalWarned)
                {
                    _paintRefusalWarned = true;
                    Debug.LogWarning(
                        $"[ModSettingsMenu] '{_section?.ModId}/{_def.Key}' is withheld but nothing marks it as such — its label or value PugText has no style, so the paint is refused rather than left unrestorable. Check the prefab wiring."
                    );
                }
                return;
            }
            CaptureEffects();
            if (_effectsCaptured)
                menuOptionEffects = new PugTextEffectMenuOption[0];
            // A row built editable still has its value effects running, because only a row bound
            // read-only went through MakeValueReadOnly. The live poll is what relocks such a row, and
            // an enabled effect would tick over the colour painted below. Captured first, so the
            // release gives back exactly the states the prefab had (JuicyAppear stays disabled).
            if (valueText != null)
                DisableValueEffects();
            var c = selected ? LockedSelectedColor : PugTextEffectMenuOption.UNSELECTABLE_TEXT_COLOR;
            PaintLocked(labelText, c);
            PaintLocked(valueText, c);
            _lockPainted = true;
        }

        // The lock lifted under a row that was painted: give back what MSM overwrote, then re-impose the
        // read-only state if the row is still not editable. Runs BEFORE a render in Refresh, so that the
        // render's ResetEffects (no longer suppressed) is what colours the row.
        private void ReleaseLockAppearance()
        {
            if (!_lockPainted)
                return;
            _lockPainted = false;
            if (_effectsCaptured)
                menuOptionEffects = _origMenuOptionEffects;
            _effectsCaptured = false;
            _labelMemo.RestoreTo(labelText);
            _valueMemo.RestoreTo(valueText);
            if (_valueEffects != null)
                for (int i = 0; i < _valueEffects.Length; i++)
                    if (_valueEffects[i] != null)
                        _valueEffects[i].enabled = _valueEffectsEnabled[i];
            _paintCaptured = false;
            if (!_def.IsEditable && valueText != null)
                MakeValueReadOnly();
        }

        public override void OnSelected()
        {
            SuppressValueSelectionEffect();
            base.OnSelected();
            ApplyLockAppearance(selected: true); // after the base, so its direct writes are overwritten, not raced
        }

        public override void OnDeselected(bool playEffect = true)
        {
            SuppressValueSelectionEffect();
            base.OnDeselected(playEffect);
            ApplyLockAppearance(selected: false);
        }

        // Only bound rows activate; the inactive template (never bound → _def null) stays hidden.
        // A row that exists but cannot be changed right now reports GRAYED_OUT, CK's own convention: the
        // navigation skips it and it stays visible. WithheldNow is false for ViewOnly and Info rows.
        public override OptionActiveState GetActiveStateInCurrentScene() =>
            _def == null ? OptionActiveState.INACTIVE
            : _def.WithheldNow ? OptionActiveState.GRAYED_OUT
            : OptionActiveState.ACTIVE;

        // Not redundant beside GetActiveStateInCurrentScene, and the reason is worth stating because it
        // was read the other way once: GRAYED_OUT keeps navigation off this row, but RadicalMenu.Activate
        // still hands the selection to the first option that is ACTIVE *or* GRAYED_OUT (Pug.Other:359134
        // → SelectOptionIndex, :359260, which has no state gate). So a withheld row CAN hold the
        // selection while being the screen's first option — necessary but not sufficient, and both
        // exceptions sit in that same method: a mouse-last open deselects everything instead (:359145),
        // and from the second open on, rememberSelectedIndex (set in this prefab) prefers the remembered
        // index while it is in range and its option is ACTIVE (:359149-359158; nothing clears it between
        // opens, CleanupAndReset at :359183-359189 never touches selectedIndex). The reliable case is a
        // keyboard-driven first open, where selectedIndex is still its initial -1 (:358972) and the scan's
        // first hit wins. When the row does hold the selection, this override is what CK asks before
        // activating it. It drives two observables, both through RadicalMenu.CanActivateCurrentOption:
        // MenuManager suppresses the menu-select SFX (:278869) and the footer drops its Select prompt,
        // because GetHelpButtonsToShow returns helpButtonsNoSelect — [NAVIGATE, BACK] — instead of
        // defaultHelpButtons (:359480, :358966, :278117). The value is safe either way: Adjust() refuses
        // on !IsEditable, which WithheldNow implies. 0TestFirstWithheldFixtures is what makes that
        // selected state reachable for a walk.
        public override bool CanBeActivated() => base.CanBeActivated() && (_def == null || !_def.WithheldNow);

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
            if (!_def.IsEditable)
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
                // Saved here rather than trusted to CoreLib: its auto-save is a flag another mod can
                // switch off (ConfigStore.Persist). Inside the try, so a failing save is the same
                // logged failure as a failing write.
                ConfigStore.Persist(e);
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
            // Release first and paint last. The two halves are load-bearing to different degrees, and
            // saying so is the point of this comment.
            //
            // RELEASE BEFORE THE RENDER is load-bearing today: it restores dontResetEffectsOnRender, so
            // the render's ResetEffects is the thing that colours a row whose lock just lifted. Paint
            // after it and MSM's own white would stand.
            //
            // PAINT AFTER THE RENDER keeps glyphJumps in step with glyphs, and in THIS prefab that is
            // insurance rather than a live need. Painting sets dontResetEffectsOnRender, which
            // suppresses the ResetEffect that sizes glyphJumps (Pug.Other:366067) — so paint-first
            // leaves the array short for as long as the row stays painted. Nothing reachable here
            // reads it: the only UNGUARDED read is PugTextEffectLateUpdate's isDanceWhenSelected branch
            // (:366131), and all six PugTextEffectMenuOptions across this mod's two prefabs carry
            // isDanceWhenSelected: 0; the guarded read logs and returns on a count mismatch (:366177),
            // and the three routes to it from outside the effect all run over menuOptionEffects
            // (:358793, :358866, :359731), which the paint has emptied. Its one internal route is
            // ResetEffect's own EndEffectImmediate, where the counts agree because ResetEffect has
            // just sized the array. It also self-heals, since the render
            // after a release re-sizes. So the ordering costs nothing and forecloses the per-frame
            // IndexOutOfRange that flipping that one Editor flag would otherwise make reachable —
            // which is also the real reason task 3's concern 1 never materialised.
            if (!_def.WithheldNow)
                ReleaseLockAppearance();
            SetText(labelText, _def.Label()); // localized; falls back to the raw key
            SetText(valueText, ValueString());
            ApplyLockAppearance();
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
