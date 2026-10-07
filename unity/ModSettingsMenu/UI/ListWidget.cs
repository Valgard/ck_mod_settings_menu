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

        // See SettingWidget.OnEntryChanged: the same three decisions — Refresh() is the whole cycle
        // including the drill tint and the lock appearance, a row on an inactive screen is left for
        // the rebuild on return, a throwing render is logged against this row, and the height is
        // re-measured because the preview can still wrap (see Preview).
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
                Debug.LogError($"[ModSettingsMenu] refreshing '{_def?.Key}' after an outside change failed: {e}");
            }
        }

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
            // Release before the render and paint after it, for the same reasons as SettingWidget.Refresh:
            // a lifted lock must be undone first so the render's ResetEffects colours the row, and the
            // first render must run unsuppressed because it sizes the label effect's glyph list.
            if (!_def.WithheldNow)
                ReleaseLockAppearance();
            _box.label.RenderPlain(_def.Label());
            _box.preview.RenderPlain(Preview());
            // Selection-aware, not a blanket grey: for a selected row that is not withheld,
            // ApplyLockAppearance takes its release branch and paints nothing, so a grey tint here
            // would stay. A withheld row is repainted over this by ApplyLockAppearance.
            TintDrill(IsSelected() ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR : PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
            ApplyLockAppearance();
        }

        /// <summary>Paints the row for its current lock state, or hands back what it painted. The list
        /// row stays ACTIVE (it must remain reachable and open its drill-in), so CK would write the
        /// ordinary grey at every render; spec § 4.4's second step is what makes it red at all. Reads
        /// WithheldNow and the selection itself; the overrides below pass the selection explicitly
        /// because IsSelected() still answers true for the row being left during OnDeselected.</summary>
        internal void ApplyLockAppearance() => ApplyLockAppearance(IsSelected());

        private void ApplyLockAppearance(bool selected)
        {
            if (_def == null || _box == null)
                return;
            if (!_def.WithheldNow)
            {
                ReleaseLockAppearance();
                return;
            }
            if (!_lockPainted)
            {
                _labelMemo = SettingWidget.TextPaintMemo.Of(_box.label);
                _previewMemo = SettingWidget.TextPaintMemo.Of(_box.preview);
            }
            if (!_labelMemo.Restorable || !_previewMemo.Restorable)
                return; // refuse to paint what cannot be handed back (see SettingWidget.TextPaintMemo)
            // Capture at the point of mutation, never in Bind: base.Awake fills menuOptionEffects AFTER
            // Bind runs, so a capture there would save a null. Until it exists there is nothing to empty.
            if (!_effectsCaptured && menuOptionEffects != null)
            {
                _origMenuOptionEffects = menuOptionEffects;
                _effectsCaptured = true;
            }
            if (_effectsCaptured)
                menuOptionEffects = new PugTextEffectMenuOption[0];
            var c = selected ? SettingWidget.LockedSelectedColor : PugTextEffectMenuOption.UNSELECTABLE_TEXT_COLOR;
            SettingWidget.PaintLocked(_box.label, c);
            SettingWidget.PaintLocked(_box.preview, c);
            TintDrill(c);
            _lockPainted = true;
        }

        private void ReleaseLockAppearance()
        {
            if (!_lockPainted)
                return;
            _lockPainted = false;
            if (_effectsCaptured)
                menuOptionEffects = _origMenuOptionEffects;
            _effectsCaptured = false;
            _labelMemo.RestoreTo(_box.label);
            _previewMemo.RestoreTo(_box.preview);
            TintDrill(IsSelected() ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR : PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
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
            TintDrill(PugTextEffectMenuOption.SELECTED_VALUE_COLOR);
            ApplyLockAppearance(selected: true); // after the base and the tint, so both are overwritten, not raced
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            TintDrill(PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
            ApplyLockAppearance(selected: false);
        }

        private void TintDrill(Color c)
        {
            if (_box != null && _box.drillIcon != null)
                _box.drillIcon.color = c;
        }
    }
}
