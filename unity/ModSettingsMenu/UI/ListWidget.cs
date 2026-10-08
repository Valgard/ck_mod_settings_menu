using System;
using System.Text;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// A discovered foreign comma-list, rendered as a COMPACT single-line row: label + a preview
    /// ("first, second, +N") + a drill affordance. Activation opens the drill-in detail screen.
    /// Read-only. The list-vs-plain classification now lives in ForeignConfigDiscovery
    /// (only genuine lists reach this widget), so there is no per-row toggle.
    /// </summary>
    public sealed class ListWidget : RadicalMenuOption, ISectionRow
    {
        private const int PreviewMaxChars = 22; // preview budget: fits one narrow value-column line

        private SettingDef _def;
        private ModSection _section;
        private ListWidgetBox _box;
        private SettingWidget.EntryWatch _watch;

        // What the row looked like before MSM first painted it locked. Per-widget fields: Populate
        // destroys and rebuilds every row, so a capture cannot outlive the row it describes.
        private SettingWidget.TextPaintMemo _labelMemo;
        private SettingWidget.TextPaintMemo _previewMemo;
        private bool _effectsCaptured;
        private PugTextEffectMenuOption[] _origMenuOptionEffects;
        private bool _lockPainted;
        private bool _paintRefusalWarned; // latched: ApplyLockAppearance is reached per selection change

        // Both texts' PugTextEffects and the enabled state each had before the lock was painted.
        // Captured and handed back rather than switched back on, because the prefab does not carry
        // them uniformly: the PugTextEffectMenuOption on the Label and on the Value is enabled, the
        // second effect beside each is not — so re-enabling the lot on release would start an
        // animation the prefab never asked for.
        private PugTextEffect[] _textEffects;
        private bool[] _textEffectsEnabled;

        public ModSection Section => _section;

        public void Bind(SettingDef def, ModSection section)
        {
            _def = def;
            _section = section;
            _box = GetComponent<ListWidgetBox>();
            if (_watch == null)
                _watch = new SettingWidget.EntryWatch(OnEntryChanged);
            _watch.Watch(def?.Entry);
            Render();
        }

        // See SettingWidget.OnEntryChanged: the same four decisions — Refresh() is the whole cycle
        // including the drill tint and the lock appearance, a row on an inactive screen is left for
        // the rebuild on return, a throwing render is logged against this row, and the height is
        // re-measured. Only the last one differs, and only in where the height comes from: the
        // preview, which can still wrap (see Preview), rather than the wider of the two text columns.
        private void OnEntryChanged()
        {
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
                    GetComponentInParent<ModSettingsScreen>()?.RemeasureRow(gameObject, ModSettingsScreen.RowHeightPx(PreviewHeight()));
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

        // Pairs with Bind's subscription; see SettingWidget.OnDestroy.
        private void OnDestroy()
        {
            if (_watch != null)
                _watch.Stop();
        }

        // RefreshSection (ModSettingsScreen, after a section-wide reset) and FollowPermissionChange
        // call this on every row in the section, selected or not. Render() ends with the selection-aware
        // drill tint, so a redraw of the currently selected row keeps its arrow in the selected tint.
        public void Refresh() => Render();

        public override OptionActiveState GetActiveStateInCurrentScene() => _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            Render();
        }

        // Called by ModSettingsScreen.RenderContent after activation. The row is single-line now, so
        // just (re)render the preview and return its height in units for SetRowHeight.
        public float RenderAndMeasure()
        {
            Render();
            return PreviewHeight();
        }

        private float PreviewHeight()
        {
            return _box != null && _box.preview != null && _box.preview.dimensions.height > 0f ? _box.preview.dimensions.height : 1f;
        }

        private string Value() => _def?.Entry?.BoxedValue?.ToString() ?? "";

        // A compact ONE-line preview: as many leading items as fit PreviewMaxChars, then a "+N" tail for
        // the rest ("InventoryChest, +15"). Budgeted by CHARACTER COUNT rather than by item count — a
        // fixed number of items would wrap the PugText to several lines on long names (the value column
        // is narrow) and blow up the row height.
        //
        // Character count is a stand-in for rendered width, and not an exact one: PugFont kerns per
        // glyph pair, so 22 wide glyphs occupy noticeably more room than 22 narrow ones and can still
        // wrap. Tolerable here because this row is read-only and a wrapped preview costs nothing but
        // looks — unlike the drill-in, where the same confusion between counting and measuring reaches
        // a foreign config file (see docs/ck/ui-framework.md § "A text row in a menu").
        private string Preview()
        {
            var tokens = ListTokenizer.Tokenize(Value());
            if (tokens.Count == 0)
                return Loc.T("ModSettingsMenu-UI/ListEmpty", "(empty)");
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var t in tokens)
            {
                string sep = shown == 0 ? "" : ", ";
                if (sb.Length + sep.Length + t.Length > PreviewMaxChars)
                    break;
                sb.Append(sep).Append(t);
                shown++;
            }
            if (shown == 0) // even the first token overflows the budget → truncate it
            {
                var first = tokens[0];
                sb.Append(first.Length > PreviewMaxChars ? first.Substring(0, PreviewMaxChars - 3) + "..." : first);
                shown = 1;
            }
            int rest = tokens.Count - shown;
            if (rest > 0)
                sb.Append(", +").Append(rest);
            return sb.ToString();
        }

        private void Render()
        {
            if (_def == null)
                return;
            if (_box == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListWidget has no ListWidgetBox — row renders blank.");
                return;
            }
            // Release before the render and paint after it, for the reasons SettingWidget.Refresh gives
            // in full: a lifted lock must be undone first so the render's ResetEffects colours the row,
            // and the render must run unsuppressed because it is what sizes the label effect's glyph
            // list. Of the two, only the first is load-bearing in this prefab — see there for why the
            // second is kept anyway.
            if (!_def.WithheldNow)
                ReleaseLockAppearance(IsSelected());
            _box.label.RenderPlain(_def.Label());
            _box.preview.RenderPlain(Preview());
            // Selection-aware, not a blanket grey: for a selected row that is not withheld,
            // ApplyLockAppearance takes its release branch and paints nothing, so a grey tint here
            // would stay. A withheld row is repainted over this by ApplyLockAppearance.
            TintDrillForSelection(IsSelected());
            ApplyLockAppearance();
        }

        /// <summary>Paints the row for its current lock state, or hands back what it painted. The list
        /// row stays ACTIVE (it must remain reachable and open its drill-in), so CK would write the
        /// ordinary grey at every render; spec § 4.4's second step is what makes it red at all. Reads
        /// WithheldNow and the selection itself; the overrides below pass the selection explicitly
        /// because IsSelected() still answers true for the row being left during OnDeselected — and
        /// so does the release, which tints the drill arrow from the same answer.</summary>
        internal void ApplyLockAppearance() => ApplyLockAppearance(IsSelected());

        private void ApplyLockAppearance(bool selected)
        {
            if (_def == null || _box == null)
                return;
            if (!_def.WithheldNow)
            {
                ReleaseLockAppearance(selected);
                return;
            }
            if (!_lockPainted)
            {
                _labelMemo = SettingWidget.TextPaintMemo.Of(_box.label);
                _previewMemo = SettingWidget.TextPaintMemo.Of(_box.preview);
                CaptureTextEffects();
            }
            if (!_labelMemo.Restorable || !_previewMemo.Restorable)
            {
                // Refuse to paint what cannot be handed back (see SettingWidget.TextPaintMemo) — but
                // say so, because the consequence here is worse than on a settings row, and for a
                // reason that has nothing to do with the paint. This row keeps the ACTIVE state its
                // drill-in needs, and neither GetActiveStateInCurrentScene nor CanBeActivated consults
                // the memo: so an unmarked withheld list still takes the selection, still plays the
                // select sound and still opens a drill-in that refuses every edit it offers — while
                // the section note, which reads WithheldNow rather than this paint, says settings are
                // being withheld. Latched per row, because this is reached on every selection change.
                if (!_paintRefusalWarned)
                {
                    _paintRefusalWarned = true;
                    Debug.LogWarning(
                        $"[ModSettingsMenu] '{_section?.ModId}/{_def.Key}' is withheld but nothing marks it as such — its label or preview PugText has no style, so the paint is refused while the row stays active and opens a drill-in that refuses every edit. Check the prefab wiring."
                    );
                }
                return;
            }
            // Capture at the point of mutation, never in Bind: base.Awake fills menuOptionEffects AFTER
            // Bind runs, so a capture there would save a null. Until it exists there is nothing to empty.
            if (!_effectsCaptured && menuOptionEffects != null)
            {
                _origMenuOptionEffects = menuOptionEffects;
                _effectsCaptured = true;
            }
            if (_effectsCaptured)
                menuOptionEffects = new PugTextEffectMenuOption[0];
            DisableTextEffects();
            var c = selected ? SettingWidget.LockedSelectedColor : PugTextEffectMenuOption.UNSELECTABLE_TEXT_COLOR;
            SettingWidget.PaintLocked(_box.label, c);
            SettingWidget.PaintLocked(_box.preview, c);
            TintDrill(c);
            _lockPainted = true;
        }

        // Takes the selection rather than asking, because both callers know it and IsSelected() does
        // not: during OnDeselected it still reports the row being left (see ApplyLockAppearance), so
        // asking here would hand a row losing the selection the selected blue on the way out.
        private void ReleaseLockAppearance(bool selected)
        {
            if (!_lockPainted)
                return;
            _lockPainted = false;
            if (_effectsCaptured)
                menuOptionEffects = _origMenuOptionEffects;
            _effectsCaptured = false;
            _labelMemo.RestoreTo(_box.label);
            _previewMemo.RestoreTo(_box.preview);
            RestoreTextEffects();
            TintDrillForSelection(selected);
        }

        // The effects on both texts, in one array with the enabled state each had on entry.
        private void CaptureTextEffects()
        {
            var labelEffects = _box.label != null ? _box.label.GetComponents<PugTextEffect>() : new PugTextEffect[0];
            var previewEffects = _box.preview != null ? _box.preview.GetComponents<PugTextEffect>() : new PugTextEffect[0];
            _textEffects = new PugTextEffect[labelEffects.Length + previewEffects.Length];
            labelEffects.CopyTo(_textEffects, 0);
            previewEffects.CopyTo(_textEffects, labelEffects.Length);
            _textEffectsEnabled = Array.ConvertAll(_textEffects, fx => fx.enabled);
        }

        // Mirrors SettingWidget.DisableValueEffects, and the asymmetry between the two is worth
        // stating so it does not read as an oversight in either direction.
        //
        // Emptying menuOptionEffects above stops CK from CALLING these effects
        // (OnSelected/OnDeselected/EndEffectImmediate); it does not stop them running. The per-frame
        // tick walks the PugText's OWN effect list and asks nothing but `enabled`
        // (PugText.ManagedLateUpdate, Pug.Other:367985, the filter at :368000), and
        // PugTextEffectMenuOption writes the text colour from there while its colorCooloff runs
        // (:366121, the writes at :366153 and :366167), settling on
        // `IsSelectionEnabled(visualOnly: true) ? unselectedTextColor : UNSELECTABLE_TEXT_COLOR`.
        //
        // For a withheld LIST row that predicate holds: on an enabled, active option IsSelectionEnabled
        // answers !ShouldBeGrayedOut (:359566-359568 → :359573-359576), and this row stays ACTIVE on
        // purpose (see GetActiveStateInCurrentScene). So the effect settles on the ordinary grey, over MSM's red, and
        // nothing paints over it again: the three call sites that repaint (Render, OnSelected,
        // OnDeselected) each need a render or a selection change, and the frame alone brings neither.
        //
        // The window is narrow rather than absent, which is why a walk will rarely produce it:
        // colorCooloff is a TimerSimple(1f/12f) ≈ 83 ms (:366033), started by the effect's own
        // OnDeselected (:366101), which CK reaches only while menuOptionEffects is non-empty — i.e.
        // while the row is NOT withheld. The lock therefore has to land within ~83 ms of the selection
        // leaving this row.
        //
        // SettingWidget leaves its LABEL effect running and needs nothing here: a withheld settings
        // row is GRAYED_OUT, so the same settle picks UNSELECTABLE_TEXT_COLOR — the colour MSM paints
        // — and while the row is selected the effect returns before writing anything
        // (:366123-366134). Do not "fix" that one to match this.
        private void DisableTextEffects()
        {
            if (_textEffects == null)
                return;
            foreach (var fx in _textEffects)
                if (fx != null)
                    fx.enabled = false;
        }

        private void RestoreTextEffects()
        {
            if (_textEffects == null)
                return;
            for (int i = 0; i < _textEffects.Length; i++)
                if (_textEffects[i] != null)
                    _textEffects[i].enabled = _textEffectsEnabled[i];
        }

        // A row whose drill-in would be refused must not present itself as activatable: CK gates the
        // menu-select SFX and the footer's select hint on this (MenuManager, and
        // GetHelpButtonsToShow), so without it the player hears an activation, sees nothing happen,
        // and the only explanation is in Player.log. The condition lives on SettingDef so this and
        // ListDetailScreen.Open cannot answer it differently.
        public override bool CanBeActivated() => base.CanBeActivated() && (_def == null || !_def.ListDetailWouldBeEmpty);

        public override void OnActivated()
        {
            base.OnActivated();
            if (_def != null)
                ListDetailScreen.Open(_def);
        }

        // Tint the drill affordance sprite to match the row's text on selection — CK's unselected grey
        // vs. the selected value-blue — driven from the same OnSelected/OnDeselected hooks the base uses
        // to recolour the label/preview PugTexts, so the arrow follows the row.
        public override void OnSelected()
        {
            base.OnSelected();
            TintDrillForSelection(selected: true);
            ApplyLockAppearance(selected: true); // after the base and the tint, so both are overwritten, not raced
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            TintDrillForSelection(selected: false);
            ApplyLockAppearance(selected: false);
        }

        // CK's two tones for an unlocked row, in one place: the four call sites that used to spell the
        // pair out are what hid a release tinting from IsSelected() while its caller knew better.
        private void TintDrillForSelection(bool selected) =>
            TintDrill(selected ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR : PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);

        private void TintDrill(Color c)
        {
            if (_box != null && _box.drillIcon != null)
                _box.drillIcon.color = c;
        }
    }
}
