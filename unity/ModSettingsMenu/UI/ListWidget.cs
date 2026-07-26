using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// A discovered foreign comma-list, rendered as a COMPACT single-line row: label + a preview
    /// ("first, second, +N") + a drill affordance. Activation opens the drill-in detail screen
    /// (Phase 2). Read-only. The list-vs-plain classification now lives in ForeignConfigDiscovery
    /// (only genuine lists reach this widget), so there is no per-row toggle.
    /// </summary>
    public sealed class ListWidget : RadicalMenuOption
    {
        private const int PreviewMaxChars = 22;   // preview budget: fits one narrow value-column line

        private SettingDef _def;
        private ModSettingsScreen _screen;
        private ListWidgetBox _box;

        public void Bind(SettingDef def, ModSettingsScreen screen)
        {
            _def = def;
            _screen = screen;
            _box = GetComponent<ListWidgetBox>();
            Render();
        }

        public override OptionActiveState GetActiveStateInCurrentScene()
            => _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

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
            return _box != null && _box.preview != null && _box.preview.dimensions.height > 0f
                ? _box.preview.dimensions.height : 1f;
        }

        private string Value() => _def?.Entry?.BoxedValue?.ToString() ?? "";

        // A compact ONE-line preview: as many leading items as fit PreviewMaxChars, then a "+N" tail for
        // the rest ("InventoryChest, +15"). Budget by WIDTH, not item count — long item names (the value
        // column is narrow) otherwise wrap the PugText to several lines and blow up the row height.
        private string Preview()
        {
            var tokens = new System.Collections.Generic.List<string>();
            foreach (var raw in Value().Split(','))
            {
                var t = raw.Trim();
                if (t.Length > 0) tokens.Add(t);
            }
            if (tokens.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var t in tokens)
            {
                string sep = shown == 0 ? "" : ", ";
                if (sb.Length + sep.Length + t.Length > PreviewMaxChars) break;
                sb.Append(sep).Append(t);
                shown++;
            }
            if (shown == 0)   // even the first token overflows the budget → truncate it
            {
                var first = tokens[0];
                sb.Append(first.Length > PreviewMaxChars ? first.Substring(0, PreviewMaxChars - 3) + "..." : first);
                shown = 1;
            }
            int rest = tokens.Count - shown;
            if (rest > 0) sb.Append(", +").Append(rest);
            return sb.ToString();
        }

        private void Render()
        {
            if (_def == null || _box == null) return;
            SetText(_box.label, Loc.T(_def.Term, _def.Key));
            if (_box.preview != null) SetText(_box.preview, Preview());
            TintDrill(PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);   // start in the unselected grey
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (_def != null) ListDetailScreen.Open(_def);
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
            if (_box != null && _box.drillIcon != null) _box.drillIcon.color = c;
        }

        private static void SetText(PugText pt, string s)
        {
            if (pt == null) return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
        }
    }
}
