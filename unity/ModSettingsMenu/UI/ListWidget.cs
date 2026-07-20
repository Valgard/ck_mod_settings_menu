using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Renders a discovered foreign string as either a comma-split read-only list (one line per item
    /// in the item container) or the plain truncated string, toggled by activating the row. The choice
    /// persists via ListOverrideStore; the initial state is override ?? heuristic. Read-only in this
    /// version (no value mutation); the item container is the home for the later edit UI.
    /// </summary>
    public sealed class ListWidget : RadicalMenuOption
    {
        private SettingDef _def;
        private ModSettingsScreen _screen;
        private ListWidgetBox _box;
        private bool _listView;

        public void Bind(SettingDef def, ModSettingsScreen screen)
        {
            _def = def;
            _screen = screen;
            _box = GetComponent<ListWidgetBox>();
            _listView = ListOverrideStore.Get(def.OverrideKey) ?? ForeignConfigDiscovery.HeuristicSaysList(Value());
            Render();
        }

        public override OptionActiveState GetActiveStateInCurrentScene()
            => _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            Render();
        }

        // Activation is the toggle: flip list/plain, persist, re-render, and re-measure the layout
        // (the item container's height changes, so the whole content layout must re-render).
        public override void OnActivated()
        {
            base.OnActivated();
            if (_def == null) return;
            _listView = !_listView;
            ListOverrideStore.Set(_def.OverrideKey, _listView);
            Render();
            if (_screen != null) _screen.RefreshLayout();
        }

        public override bool OnSkimLeft()  { OnActivated(); return true; }
        public override bool OnSkimRight() { OnActivated(); return true; }

        private string Value() => _def?.Entry?.BoxedValue?.ToString() ?? "";

        private void Render()
        {
            if (_def == null || _box == null) return;
            SetText(_box.label, Loc.T(_def.Term, _def.Key));
            SetText(_box.toggleValue, _listView ? Loc.T("ModSettingsMenu-UI/On") : Loc.T("ModSettingsMenu-UI/Off"));

            // Clear previous item lines (keep the template).
            if (_box.itemContainer != null)
                for (int i = _box.itemContainer.childCount - 1; i >= 0; i--)
                {
                    var child = _box.itemContainer.GetChild(i).gameObject;
                    if (_box.itemTemplate != null && child == _box.itemTemplate) continue;
                    child.transform.SetParent(null, false);
                    Object.Destroy(child);
                }

            if (_box.itemTemplate != null) _box.itemTemplate.SetActive(false);
            if (_box.itemContainer == null || _box.itemTemplate == null) return;

            if (_listView)
            {
                foreach (var raw in Value().Split(','))
                {
                    var item = raw.Trim();
                    if (item.Length > 0) AddLine(item);
                }
            }
            else
            {
                // Plain view: the full string on one line, truncated so it can't overflow the row.
                var s = Value();
                AddLine(s.Length > 40 ? s.Substring(0, 40) + "..." : s);
            }
        }

        // Clone the item-line template into the container, render `text`, size the row to the text.
        private void AddLine(string text)
        {
            var line = Object.Instantiate(_box.itemTemplate, _box.itemContainer);
            line.SetActive(true);
            var pt = line.GetComponent<PugText>();
            SetText(pt, text);
            var wrap = line.GetComponent<WrapperUIComponent>();
            if (wrap != null && pt != null)
                wrap.renderHeightPixels = Mathf.RoundToInt(16f * (pt.dimensions.height > 0f ? pt.dimensions.height : 1f)) + 4;
        }

        // Render a raw (non-localized) string into a PugText, like the sibling widgets.
        private static void SetText(PugText pt, string s)
        {
            if (pt == null) return;
            pt.localize = false;
            pt.Render(s, rewindEffectAnims: false, force: true);
        }
    }
}
