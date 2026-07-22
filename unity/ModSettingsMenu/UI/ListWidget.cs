using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Renders a discovered foreign string as either a comma-split read-only list (one line per item
    /// in the item container) or the plain truncated string, toggled by the far-right icon (a
    /// ListToggleButton — the row itself does not toggle). The choice persists via ListOverrideStore;
    /// the initial state is override ?? heuristic. Read-only in this version (no value mutation); the
    /// item container is the home for the later edit UI.
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

        // The row itself no longer toggles — only the far-right toggle icon (a ListToggleButton on
        // the icon GO) flips the view (user decision: icon-only; keyboard/controller support for the
        // icon comes later). So OnActivated / OnSkim are intentionally NOT overridden — the row stays
        // a plain, navigable menu option.

        // Flip list <-> plain, persist the override, re-render, and re-measure the layout (the item
        // container's height changes, so the whole content layout must re-render). Called by the
        // toggle icon's ListToggleButton.OnLeftClicked.
        public void ToggleView()
        {
            if (_def == null) return;
            _listView = !_listView;
            ListOverrideStore.Set(_def.OverrideKey, _listView);
            Render();
            if (_screen != null) _screen.RefreshLayout();
        }

        // Called by ModSettingsScreen.RenderContent (after activation): render the item container's
        // inner layout so the lines stack, align the row's label + toggle icon to the first item's
        // line, and RETURN the container's rendered height in UNITS. The screen turns that into the
        // row height via SetRowHeight(RowHeightPx(..)) — the same path the normal rows use — so this
        // method sets no row height and no top padding itself. The row's WrapperUIComponent keeps its
        // prefab pivot (TopLeft, like Header/Hint, the other rows whose content grows downward).
        public float RenderAndMeasure()
        {
            if (_box == null || _box.itemContainer == null) return 0f;
            var layout = _box.itemContainer.GetComponent<LinearLayoutUIComponent>();
            layout?.RenderUIComponent(force: true);
            // Center the content within the row's RowPaddingPx (RowHeightPx adds it), the way a normal
            // MiddleLeft row's centering splits it 50/50. This TopLeft row would otherwise leave all the
            // padding at the bottom, so line 1 sits RowPaddingPx/2 higher than a normal row's label
            // (measured: normal label = rowH/2 below the box top, the list's only textH/2). Only matters
            // once the value is taller than the label; when they match, this equals a normal row exactly.
            SetLocalY(_box.itemContainer, -ModSettingsScreen.RowPaddingPx / 2f / 16f);
            AlignHeaderToFirstItem();
            return layout != null ? layout.GetUIComponentRenderHeight() : 0f;
        }

        // The item container centers each line in its slot (a half-line offset), so the first item
        // sits on line 1's centre — not at the container's top edge. The label + toggle icon are
        // static siblings, so move them onto that same line, read from the item's actual position.
        // This is the list analogue of a normal MiddleLeft row whose label sits on its single centred
        // line: it aligns the label with the list AND lifts it off the box's top border, with no
        // hand-tuned top padding.
        private void AlignHeaderToFirstItem()
        {
            Transform first = null;
            for (int i = 0; i < _box.itemContainer.childCount; i++)
            {
                var c = _box.itemContainer.GetChild(i);
                if (c.gameObject.activeSelf) { first = c; break; }
            }
            if (first == null) return;
            float lineY = _box.itemContainer.localPosition.y + first.localPosition.y;
            if (_box.label != null) SetLocalY(_box.label.transform, lineY);
            if (_box.toggleIcon != null) SetLocalY(_box.toggleIcon.transform, lineY);
        }

        private static void SetLocalY(Transform t, float y)
        {
            var p = t.localPosition; p.y = y; t.localPosition = p;
        }

        private string Value() => _def?.Entry?.BoxedValue?.ToString() ?? "";

        private void Render()
        {
            if (_def == null || _box == null) return;
            SetText(_box.label, Loc.T(_def.Term, _def.Key));
            // Swap the 2-state toggle sprite (far-right); the value column holds the list/plain text.
            if (_box.toggleIcon != null)
                _box.toggleIcon.sprite = _listView ? _box.listIcon : _box.plainIcon;

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

            // The item lines are (re)cloned on every Render, so the base's Awake-time menuOptionEffects
            // snapshot never sees them. Rebuild it now so OnSelected/OnDeselected can drive the current
            // lines (editable) while always keeping the label's own navigation highlight.
            RebuildMenuOptionEffects();
        }

        // Clone the item-line template into the container, render `text`, size the row to the text.
        private void AddLine(string text)
        {
            var line = Object.Instantiate(_box.itemTemplate, _box.itemContainer);
            line.SetActive(true);
            var pt = line.GetComponent<PugText>();
            // Non-editable (v1): render the line as CK's static read-only grey — the per-line analogue of
            // SettingWidget.MakeValueReadOnly. This kills the animated transition + on-render repaint ONLY;
            // the blue-on-SELECTION path is blocked separately by keeping the line OUT of menuOptionEffects
            // in RebuildMenuOptionEffects (RadicalMenuOption.OnSelected recolours that array's entries
            // directly, ignoring component.enabled). MUST run before SetText so the render's ResetEffects
            // is already suppressed (dontResetEffectsOnRender).
            if (_def != null && !_def.Editable && pt != null) LockLineReadOnly(line, pt);
            SetText(pt, text);
            var wrap = line.GetComponent<WrapperUIComponent>();
            // Slot height = EXACTLY the rendered text height (no padding). PugText forces single-line
            // text to center on its transform; with the slot equal to the text, that centre sits so
            // the text is flush to the slot's top — i.e. centered == top-aligned. The lines then stack
            // tight and top-aligned, and the header (aligned to line 1) lands on the same top edge.
            if (wrap != null && pt != null)
                wrap.renderHeightPixels = Mathf.RoundToInt(16f * (pt.dimensions.height > 0f ? pt.dimensions.height : 1f));
        }

        // Render this item line as a static read-only value: CK's deselected grey, no juicy-appear, no
        // on-render repaint. Mirrors SettingWidget.MakeValueReadOnly but per cloned line — disables EVERY
        // PugTextEffect (MenuOption + JuicyAppear both copied onto the ItemTemplate), stops Render from
        // re-applying them, and locks the per-instance style colour (PugText.style is copied per clone,
        // so this never bleeds into the label or other rows). NOTE: the blue on SELECTION is NOT blocked
        // here — RadicalMenuOption.OnSelected recolours menuOptionEffects entries directly regardless of
        // component.enabled; that path is blocked by RebuildMenuOptionEffects keeping this line out.
        private static void LockLineReadOnly(GameObject line, PugText pt)
        {
            foreach (var fx in line.GetComponents<PugTextEffect>())
                fx.enabled = false;
            pt.dontResetEffectsOnRender = true;
            if (pt.style != null)
                pt.style.color = PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR;
        }

        // Refresh the base RadicalMenuOption.menuOptionEffects to match the CURRENT children. Always
        // include the label's effect (isValueText=false → SELECTED_TEXT_COLOR, the normal label
        // highlight). Include the item lines' effects (isValueText=true → SELECTED_VALUE_COLOR blue)
        // ONLY when editable; when not, the lines are locked grey in AddLine and stay out of the array
        // so OnSelected/OnDeselected can't repaint them.
        private void RebuildMenuOptionEffects()
        {
            var effects = new System.Collections.Generic.List<PugTextEffectMenuOption>();
            // Search the label's own subtree (matches the base Awake's GetComponentsInChildren breadth), so
            // a prefab that parents the effect on a label child still resolves. Warn instead of dropping it
            // silently — a missing effect here kills the row's navigation highlight, and CLAUDE.md notes the
            // Editor can strip prefab components on reserialization; this surfaces that regression.
            var labelFx = _box != null && _box.label != null
                ? _box.label.GetComponentInChildren<PugTextEffectMenuOption>(true) : null;
            if (labelFx != null) effects.Add(labelFx);
            else if (_box != null && _box.label != null)
                Debug.LogWarning($"[ModSettingsMenu] '{_box.label.name}' has no PugTextEffectMenuOption (expected in the prefab) — list row won't highlight.");
            if (_def != null && _def.Editable && _box != null && _box.itemContainer != null)
                for (int i = 0; i < _box.itemContainer.childCount; i++)
                {
                    var c = _box.itemContainer.GetChild(i);
                    if (!c.gameObject.activeSelf) continue;   // skip the inactive template
                    var fx = c.GetComponent<PugTextEffectMenuOption>();
                    if (fx != null) effects.Add(fx);
                }
            menuOptionEffects = effects.ToArray();
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
