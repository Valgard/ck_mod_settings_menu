using System;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>The lock cycle of one withheld row, shared by <see cref="SettingWidget"/> and
    /// <see cref="ListWidget"/>: remember what the row's two texts and their effects looked like, paint
    /// both texts in the locked tone with CK's own writes silenced, and hand back exactly what was taken
    /// once the lock lifts. Spec § 4.4: CK's writes are silenced in two steps (the selection path via
    /// menuOptionEffects, the render path via dontResetEffectsOnRender), and the captured effects are
    /// switched off so their own per-frame tick cannot write either — see the constructor for which.
    ///
    /// <see cref="CaptureTexts"/> and <see cref="DisableEffects"/> are usable outside a lock as well:
    /// SettingWidget's read-only value takes the same capture and switches the same effects off, so
    /// that a later lock and its release hand back the prefab's state rather than the read-only one.
    ///
    /// What stays with each row is what genuinely differs between them, and it stays there so it can
    /// be seen: WHEN the texts are captured (<see cref="CaptureTexts"/> is a building block, not a
    /// policy), which effects the paint silences (the constructor's
    /// <c>silenceFirstTextEffects</c>), the warning a refused paint logs, and what each row does after
    /// a paint or a release.
    ///
    /// Per-row state on purpose: Populate destroys and rebuilds every row, so nothing here can outlive
    /// the row it describes — PreWarm's load-time rows, where every scoped row is locked, are discarded
    /// with the rest.</summary>
    internal sealed class RowLockPaint
    {
        /// <summary>The selected tone of a locked row: CK's selection factor applied channel-wise to
        /// UNSELECTABLE_TEXT_COLOR, so it reads as a lighter red rather than as selection blue
        /// (spec § 4.4 gives the derivation).</summary>
        private static readonly Color LockedSelectedColor = new Color(0.642f, 0.263f, 0.277f, 1f);

        /// <summary>The tone a locked row's texts are painted in, for the selection state given.</summary>
        internal static Color LockedColor(bool selected) => selected ? LockedSelectedColor : PugTextEffectMenuOption.UNSELECTABLE_TEXT_COLOR;

        private readonly Func<PugText> _first;
        private readonly Func<PugText> _second;
        private readonly bool _silenceFirstTextEffects;

        private TextPaintMemo _firstMemo;
        private TextPaintMemo _secondMemo;

        // The silenced texts' PugTextEffects and the enabled state each had before the paint. Captured
        // and handed back rather than switched back on, because the prefabs do not carry them
        // uniformly: the PugTextEffectMenuOption on a list row's Label and Value is enabled, the second
        // effect beside each is not (and a settings row's JuicyAppear stays disabled) — so re-enabling
        // the lot on release would start an animation the prefab never asked for.
        private PugTextEffect[] _effects;
        private bool[] _effectsEnabled;

        private bool _menuEffectsCaptured;
        private PugTextEffectMenuOption[] _origMenuEffects;

        private bool _refusalWarned; // latched: a paint is attempted on every selection change

        /// <param name="first">The row's label text.</param>
        /// <param name="second">The row's other text — a settings row's value, a list row's preview.</param>
        /// <param name="silenceFirstTextEffects">Whether the paint switches off the label's effects as
        /// well as the second text's. The two rows answer this differently, and the asymmetry is worth
        /// stating so it does not read as an oversight in either direction.
        ///
        /// Emptying menuOptionEffects stops CK from CALLING these effects
        /// (OnSelected/OnDeselected/EndEffectImmediate); it does not stop them running. The per-frame
        /// tick walks the PugText's OWN effect list and asks nothing but <c>enabled</c>
        /// (PugText.ManagedLateUpdate, Pug.Other:367985, the filter at :368000), and
        /// PugTextEffectMenuOption writes the text colour from there while its colorCooloff runs
        /// (:366121, the writes at :366153 and :366167), settling on
        /// <c>IsSelectionEnabled(visualOnly: true) ? unselectedTextColor : UNSELECTABLE_TEXT_COLOR</c>.
        ///
        /// For a withheld LIST row that predicate holds: on an enabled, active option IsSelectionEnabled
        /// answers !ShouldBeGrayedOut (:359566-359568 → :359573-359576), and that row stays ACTIVE on
        /// purpose (see ListWidget.GetActiveStateInCurrentScene). So the effect settles on the ordinary
        /// grey, over MSM's red, and nothing paints over it again: ListWidget's three call sites that
        /// repaint (Render, OnSelected, OnDeselected) each need a render or a selection change, and the frame
        /// alone brings neither. The window is narrow rather than absent, which is why a walk will rarely
        /// produce it: colorCooloff is a TimerSimple(1f/12f) ≈ 83 ms (:366033), started by the effect's
        /// own OnDeselected (:366101), which CK reaches only while menuOptionEffects is non-empty — i.e.
        /// while the row is NOT withheld. The lock therefore has to land within ~83 ms of the selection
        /// leaving the row. ListWidget therefore passes <c>true</c>.
        ///
        /// SettingWidget leaves its LABEL effect running and needs nothing more: a withheld settings
        /// row is GRAYED_OUT, so the same settle picks UNSELECTABLE_TEXT_COLOR — the colour MSM paints
        /// — and while the row is selected the effect returns before writing anything
        /// (:366123-366134). It passes <c>false</c>; do not "fix" that one to match.</param>
        public RowLockPaint(Func<PugText> first, Func<PugText> second, bool silenceFirstTextEffects)
        {
            _first = first;
            _second = second;
            _silenceFirstTextEffects = silenceFirstTextEffects;
        }

        /// <summary>True while MSM holds a mutation that has to be handed back when the lock lifts.</summary>
        public bool Painted { get; private set; }

        /// <summary>True from <see cref="CaptureTexts"/> until a <see cref="Release"/> that actually
        /// released a paint.</summary>
        public bool TextsCaptured { get; private set; }

        /// <summary>Record what both texts and the silenced effects look like now. Unconditional: how
        /// often to call it is the row's decision, and the two rows decide differently (see each one's
        /// call site). Same guarantee either way — an unpainted row's texts are whatever the last render
        /// left, so a re-capture reads the same state a one-shot one kept. A no-op while painted: a
        /// capture then would record MSM's own paint as the row's original.</summary>
        public void CaptureTexts()
        {
            if (Painted)
                return;
            var first = _first();
            var second = _second();
            _firstMemo = TextPaintMemo.Of(first);
            _secondMemo = TextPaintMemo.Of(second);
            var firstEffects = _silenceFirstTextEffects && first != null ? first.GetComponents<PugTextEffect>() : new PugTextEffect[0];
            var secondEffects = second != null ? second.GetComponents<PugTextEffect>() : new PugTextEffect[0];
            _effects = new PugTextEffect[firstEffects.Length + secondEffects.Length];
            firstEffects.CopyTo(_effects, 0);
            secondEffects.CopyTo(_effects, firstEffects.Length);
            _effectsEnabled = Array.ConvertAll(_effects, fx => fx.enabled);
            TextsCaptured = true;
        }

        /// <summary>Record the row's menuOptionEffects before MSM's first write to them. Called at the
        /// point of the first mutation, never from Bind: base.Awake fills menuOptionEffects AFTER Bind
        /// runs, so a capture there would save a null and the release would wipe the row's effects.
        /// Until the array exists there is nothing to capture.</summary>
        public void CaptureMenuEffects(PugTextEffectMenuOption[] current)
        {
            if (_menuEffectsCaptured || current == null)
                return;
            _origMenuEffects = current;
            _menuEffectsCaptured = true;
        }

        /// <summary>Switch off every captured effect, so none of them ticks a colour over MSM's. The
        /// prior states stay in the capture, which is what <see cref="Release"/> hands back.</summary>
        public void DisableEffects()
        {
            if (_effects == null)
                return;
            foreach (var fx in _effects)
                if (fx != null)
                    fx.enabled = false;
        }

        /// <summary>Paint both texts in the locked tone for the selection state given, silencing CK's
        /// writes first. Refuses — and returns false — when the capture could not hand the row back.
        ///
        /// That refusal is the invariant this class exists to keep: a text may be painted only if its
        /// memo is restorable. The check therefore sits at the PAINT, not at the capture — a
        /// precondition guarded only where the memo is taken would let a row be painted that can never
        /// be handed back, since painting repeats and the capture need not. A row whose texts were never
        /// captured holds default memos, which are not restorable, so it is refused too.
        /// <paramref name="refusalWarning"/> is logged once per row, because this is reached on every
        /// selection change; each row words it for what a refusal costs there.</summary>
        public bool TryPaint(ref PugTextEffectMenuOption[] menuOptionEffects, bool selected, Func<string> refusalWarning)
        {
            if (!_firstMemo.Restorable || !_secondMemo.Restorable)
            {
                if (!_refusalWarned)
                {
                    _refusalWarned = true;
                    Debug.LogWarning(refusalWarning());
                }
                return false;
            }
            CaptureMenuEffects(menuOptionEffects);
            if (_menuEffectsCaptured)
                menuOptionEffects = new PugTextEffectMenuOption[0];
            DisableEffects();
            var c = LockedColor(selected);
            PaintLocked(_first(), c);
            PaintLocked(_second(), c);
            Painted = true;
            return true;
        }

        /// <summary>Give back what the paint overwrote — menuOptionEffects, both texts, the effects'
        /// enabled states — and forget the capture entirely, so the next paint cannot hand back an
        /// older cycle's state: without a fresh capture it is refused. Returns false, doing nothing, when nothing was
        /// painted; the row's own follow-up runs only on a real release.</summary>
        public bool Release(ref PugTextEffectMenuOption[] menuOptionEffects)
        {
            if (!Painted)
                return false;
            Painted = false;
            if (_menuEffectsCaptured)
                menuOptionEffects = _origMenuEffects;
            _menuEffectsCaptured = false;
            _firstMemo.RestoreTo(_first());
            _secondMemo.RestoreTo(_second());
            if (_effects != null)
                for (int i = 0; i < _effects.Length; i++)
                    if (_effects[i] != null)
                        _effects[i].enabled = _effectsEnabled[i];
            _firstMemo = default;
            _secondMemo = default;
            _effects = null;
            _effectsEnabled = null;
            TextsCaptured = false;
            return true;
        }

        // The one write a withheld row's text takes: stop CK's render path from repainting it
        // (dontResetEffectsOnRender), then set the base colour a render would use and the live glyphs,
        // which a render does not run for.
        private static void PaintLocked(PugText t, Color c)
        {
            if (t == null || t.style == null)
                return;
            t.dontResetEffectsOnRender = true;
            t.style.color = c;
            t.SetTempColor(c);
        }

        // What a text looked like before MSM painted it, so lifting the lock can hand back the row it
        // found rather than a guess at it. Private to this class, so the only paint there is —
        // TryPaint — is also the only place that checks Restorable. A missing text is restorable: there
        // is nothing to paint and nothing to give back. default(TextPaintMemo) is not, which is what
        // makes an uncaptured row refuse.
        private readonly struct TextPaintMemo
        {
            private readonly Color _color;
            private readonly bool _dontResetEffectsOnRender;
            private readonly bool _present;

            public bool Restorable { get; }

            private TextPaintMemo(Color color, bool dontResetEffectsOnRender, bool present, bool restorable)
            {
                _color = color;
                _dontResetEffectsOnRender = dontResetEffectsOnRender;
                _present = present;
                Restorable = restorable;
            }

            public static TextPaintMemo Of(PugText t) =>
                t == null
                    ? new TextPaintMemo(default, false, present: false, restorable: true)
                    : new TextPaintMemo(t.style != null ? t.style.color : default, t.dontResetEffectsOnRender, present: true, restorable: t.style != null);

            public void RestoreTo(PugText t)
            {
                if (t == null || !_present || !Restorable)
                    return;
                t.dontResetEffectsOnRender = _dontResetEffectsOnRender;
                t.style.color = _color;
                t.SetTempColor(_color);
            }
        }
    }
}
