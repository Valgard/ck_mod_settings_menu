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

        public ModSection Section => _section;

        public void Bind(SettingDef def, ModSection section)
        {
            _def = def;
            _section = section;
            _box = GetComponent<ListWidgetBox>();
            Render();
        }

        // RefreshSection (ModSettingsScreen, after a section-wide reset) calls this on every row
        // in the section, selected or not — Render() alone always ends by painting the drill arrow
        // unselected-grey, so a redraw of the currently selected row must also restore its
        // selected tint, or the arrow would stay grey until the next OnSelected/OnDeselected.
        public void Refresh()
        {
            Render();
            TintDrill(IsSelected() ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR : PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
        }

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
            _box.label.RenderPlain(_def.Label());
            _box.preview.RenderPlain(Preview());
            TintDrill(PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR); // start in the unselected grey
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
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            TintDrill(PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
        }

        private void TintDrill(Color c)
        {
            if (_box != null && _box.drillIcon != null)
                _box.drillIcon.color = c;
        }
    }
}
