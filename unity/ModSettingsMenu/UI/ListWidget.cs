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
        private EntryWatch _watch;

        // What the row looked like before MSM first painted it locked, and the paint over it. Both texts'
        // effects are silenced — see RowLockPaint's constructor for why this row needs that and a
        // settings row does not. Created on first use; the texts are read through _box at each call.
        private RowLockPaint _paint;
        private RowLockPaint Paint =>
            _paint
            ?? (_paint = new RowLockPaint(() => _box != null ? _box.label : null, () => _box != null ? _box.preview : null, silenceFirstTextEffects: true));

        public ModSection Section => _section;

        public void Bind(SettingDef def, ModSection section)
        {
            _def = def;
            _section = section;
            _box = GetComponent<ListWidgetBox>();
            if (_watch == null)
                // Measured from the preview alone, because it is the one text of this row that can wrap
                // (see Preview); a settings row takes the wider of its two columns instead.
                _watch = new EntryWatch(this, Refresh, () => ModSettingsScreen.RowHeightPx(PreviewHeight()));
            _watch.Watch(def, section);
            Render();
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
            // This row's capture policy: re-taken on every call while unpainted. An unpainted row's texts
            // are whatever the last render left, so a re-capture reads the same state a one-shot one kept.
            if (!Paint.Painted)
                Paint.CaptureTexts();
            // A refused paint costs more here than on a settings row, and for a reason that has nothing
            // to do with the paint. This row keeps the ACTIVE state its drill-in needs, and neither
            // GetActiveStateInCurrentScene nor CanBeActivated consults the capture: so an unmarked
            // withheld list still takes the selection, still plays the select sound and still opens a
            // drill-in that refuses every edit it offers — while the section note, which reads
            // WithheldNow rather than this paint, says settings are being withheld.
            if (
                Paint.TryPaint(
                    ref menuOptionEffects,
                    selected,
                    () =>
                        $"[ModSettingsMenu] '{_section?.ModId}/{_def.Key}' is withheld but nothing marks it as such — its label or preview PugText has no style, so the paint is refused while the row stays active and opens a drill-in that refuses every edit. Check the prefab wiring."
                )
            )
                TintDrill(RowLockPaint.LockedColor(selected));
        }

        // Takes the selection rather than asking, because both callers know it and IsSelected() does
        // not: during OnDeselected it still reports the row being left (see ApplyLockAppearance), so
        // asking here would hand a row losing the selection the selected blue on the way out.
        private void ReleaseLockAppearance(bool selected)
        {
            if (Paint.Release(ref menuOptionEffects))
                TintDrillForSelection(selected);
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
