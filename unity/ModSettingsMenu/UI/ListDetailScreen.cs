using System.Collections.Generic;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The list drill-in detail screen: a pushed RadicalMenu showing one comma-list in full — a
    /// title plus one navigable read-only row per item, scrollable. Controller/keyboard navigation
    /// walks the rows and scroll-follow reaches the bottom (the overflow fix). Read-only in v1; the
    /// rows are the future home of per-token editing.
    ///
    /// Unlike ModSettingsScreen (rows nested row->box->section->contentRoot), the item rows here are
    /// DIRECT children of box.itemContainer (the scroll content), so the scroll-follow is the simple
    /// 1-level form CK's own scrollable menus use (raw localPosition.y).
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class ListDetailScreen : RadicalMenu, IScrollable
    {
        public ListDetailBox box;

        private SettingDef _pending; // seeded by Open() before PushMenu resolves this instance — UNCHANGED
        private SettingDef _activeDef; // the setting this open session is showing/editing — set once
        private bool _readOnly; // this open session's own copy of _activeDef.ReadOnly — set in Populate

        // in Populate() from _pending (which Activate() nulls right after)
        private ListDetailItem _addRow; // the permanent trailing blank row, tracked by RebuildRows
        private bool _rebuildPending; // set by OnRowTextCommitted, consumed by Update — see design note
        private bool _lastCommitWasAddRow; // which row triggered the pending rebuild (focus-follow target)
        private UIScrollWindow _scroll;
        private LinearLayoutUIComponent _layout;

        public static void Open(SettingDef def)
        {
            // Broken bundle → no detail instance → TypeToMenu returns null → PushMenu(null) NREs. Guard it.
            if (MenuPatch.ListDetailInstance == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetail instance missing; cannot open the list drill-in.");
                return;
            }
            MenuPatch.ListDetailInstance._pending = def;
            Manager.menu.PushMenu(ModSettingsMenuMod.ListDetailMenuType);
        }

        // Three-step open like ModSettingsScreen: build structure + fill menuOptions (Populate),
        // activate the hierarchy (base.Activate), THEN render the layout (RenderContent) — a
        // LinearLayout skips inactive children, so heights would be 0 before activation.
        public override void Activate()
        {
            Populate();
            base.Activate();
            RenderContent();
            _pending = null; // consumed by Populate (title + Value()) — clear so a stale def can't leak
        }

        // Safety net for OnRowTextCommitted's new activeInputField-transition trigger (see
        // ListDetailItem.Update): that trigger fires when the active row loses activeInputField —
        // which happens on Enter/Escape or on clicking a DIFFERENT text-input row, but NOT on
        // clicking something else entirely (a non-text widget, the scrollbar, outside the screen).
        // In those cases the row would stay "active" with nothing left to clear it, silently
        // dropping the pending edit. Force the commit here, once, whenever this screen closes —
        // regardless of how the player left it.
        public override void Deactivate(bool pop)
        {
            if (Manager.input.activeInputField is ListDetailItem activeItem && activeItem.owner == this)
                OnRowTextCommitted(activeItem);
            base.Deactivate(pop);
        }

        private string Value() => _activeDef?.Entry?.BoxedValue?.ToString() ?? "";

        private void Populate()
        {
            _activeDef = _pending;
            _readOnly = _activeDef != null && _activeDef.ReadOnly;
            _rebuildPending = false; // a stale deferred rebuild from a prior open can't apply here
            _scroll = GetComponent<UIScrollWindow>();
            if (box == null || box.itemContainer == null || box.itemTemplate == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetailScreen prefab not wired (box/itemContainer/itemTemplate) — detail stays empty.");
                return;
            }
            box.itemTemplate.SetActive(false);
            _layout = box.itemContainer.GetComponent<LinearLayoutUIComponent>();
            if (_layout == null)
                Debug.LogWarning("[ModSettingsMenu] ListDetailScreen itemContainer has no LinearLayoutUIComponent — items won't lay out.");

            // Title = the setting's own label (the list's name).
            if (box.title != null && _activeDef != null)
            {
                string label = Loc.T(_activeDef.Term, _activeDef.Key);
                box.title.RenderPlain(label);
                // Keep the drop-shadow twin in sync (a sibling of the title), else it shows stale text.
                var shadow = box.title.transform.parent != null ? box.title.transform.parent.Find("Title bigtext shadow") : null;
                if (shadow != null)
                    shadow.GetComponent<PugText>().RenderPlain(label);
            }

            RebuildRows();

            if (_scroll != null)
            {
                _scroll.scrollingContent = box.itemContainer;
                _scroll.ResetScroll();
            }
        }

        // Destroys every current row (real tokens + the trailing add-row) and rebuilds them fresh from
        // _activeDef's live value. The SAME rebuild-from-canonical-value path serves the initial open
        // (Populate) and every post-edit refresh (OnRowTextCommitted, via the deferred Update path) —
        // there is no separate incremental add/remove/edit logic, just "re-derive everything from the
        // value that was just persisted."
        private void RebuildRows()
        {
            // Clear the previous rows. Detach BEFORE Destroy (deferred to end-of-frame), else a rebuild
            // this same frame would count the stale rows and mis-size the layout.
            for (int i = box.itemContainer.childCount - 1; i >= 0; i--)
            {
                var child = box.itemContainer.GetChild(i).gameObject;
                // PugText pools its glyph SpriteRenderers (usePooledResources); destroying a row
                // without releasing them first leaks pooled glyphs every rebuild (this screen
                // rebuilds on every edit, unlike a static list built once per open) until the
                // pool runs dry and an unrelated screen's PugText renders blank next (reproduced:
                // ModSettingsScreen's own rows went text-less after editing a list). Release every
                // PugText in the row (pugText + hintText) before destroying, mirroring the identical
                // item-checklist fix and ModSettingsScreen's own section-teardown fix.
                foreach (var text in child.GetComponentsInChildren<PugText>(includeInactive: true))
                    text.Clear();
                child.transform.SetParent(null, worldPositionStays: false);
                Object.Destroy(child);
            }
            menuOptions.Clear();
            _addRow = null;

            // One row per non-empty token...
            foreach (var raw in Value().Split(','))
            {
                var token = raw.Trim();
                if (token.Length > 0)
                    AddItem(token, isAddRow: false);
            }
            // ...plus one permanent trailing blank row for adding a new token — a read-only list has
            // nothing to add, so it gets no add-row at all, not just an inert one.
            if (!_readOnly)
                AddItem("", isAddRow: true);
        }

        // Clone the (inactive) item template into the container, seed its text, register it as a
        // navigable menu option. The template being inactive makes Instantiate(_, parent) produce an
        // inactive clone (no mid-clone OnEnable/NRE); SetActive(true) then activates it cleanly.
        private void AddItem(string token, bool isAddRow)
        {
            var row = Object.Instantiate(box.itemTemplate, box.itemContainer);
            row.SetActive(true);
            var item = row.GetComponent<ListDetailItem>();
            if (item == null)
                return;
            item.owner = this;
            item.isAddRow = isAddRow;
            item.readOnly = _readOnly;
            if (item.pugText != null)
                item.pugText.localize = false; // cloned PugText inherits localize=true — same trap as
            // SettingWidget.SetText and the hintText line below; must be
            // set before SetInputText's internal Render() call, not after
            item.SetInputText(token);
            if (item.hintText != null)
            {
                item.hintText.localize = false; // same cloned-PugText trap as pugText above — every
                // row's hintText clone otherwise inherits localize=true and renders its prefab-authored
                // literal placeholder ("Hint Text") as a failed loc-term lookup ("missing: Hint Text")
                item.hintText.SetText("");
            }
            if (isAddRow)
            {
                item.hintString = Loc.T("ModSettingsMenu-UI/ListAddHint", "+ Add");
                _addRow = item;
            }
            item.SetParentMenu(this);
            menuOptions.Add(item);
        }

        // Render the layout AFTER activation (children are active now, so the LinearLayout counts them
        // and computes real heights). Size each row to its rendered text (like ModSettingsScreen), then
        // lay out. contentRoot position is owned by UIScrollWindow, so no manual anchoring here.
        internal void RenderContent()
        {
            if (_layout == null || box == null || box.itemContainer == null)
                return;
            _layout.RenderUIComponent(force: true); // rows render → PugText.dimensions available
            for (int i = 0; i < box.itemContainer.childCount; i++)
            {
                var go = box.itemContainer.GetChild(i).gameObject;
                if (!go.activeSelf)
                    continue;
                // ItemTemplate's own label PugText now lives on a child GO (the ICL/SettingTemplate
                // "Display" pattern), so it is read via ListDetailItem.pugText — the same serialized
                // reference ModSettingsScreen reads via SettingWidget.labelText — not GetComponent<PugText>()
                // on the row root, which would return null now that WrapperUIComponent is the row's only
                // UIComponentMonoBehaviour.
                var pt = go.GetComponent<ListDetailItem>()?.pugText;
                var wrap = go.GetComponent<WrapperUIComponent>();
                if (pt != null && wrap != null)
                    wrap.renderHeightPixels = ModSettingsScreen.RowHeightPx(pt);
            }
            _layout.RenderUIComponent(force: true); // re-lay out with the measured heights
        }

        // Scroll the viewport so the selected row follows keyboard / controller navigation (the base
        // RadicalMenu never does this itself). Items are DIRECT children of the scroll content, so the
        // origin is the raw localPosition.y — the 1-level form CK's own scrollable menus use.
        protected override void OnSelectedOptionChanged()
        {
            base.OnSelectedOptionChanged();
            if (_scroll == null || box == null || box.itemContainer == null)
                return;
            if (selectedIndex < 0 || selectedIndex >= menuOptions.Count)
                return;
            var option = menuOptions[selectedIndex];
            if (option == null)
                return;
            // Mouse hover must not scroll the page — CK gates its own ScrollIntoView the same way.
            if (Manager.input.SystemIsUsingMouse())
                return;

            float origin = option.transform.localPosition.y;
            var wrap = option.GetComponent<WrapperUIComponent>();
            float height = wrap != null ? wrap.GetUIComponentRenderHeight() : 1f;
            bool topPivot = wrap != null && wrap.GetUIComponentPivotPosition() == WrapperUIComponent.PivotPosition.TopLeft;
            float topEdge = topPivot ? origin : origin + height / 2f;

            if (height <= _scroll.windowHeight)
            {
                float center = topEdge - height / 2f;
                _scroll.MoveScrollToIncludePosition(center, height / 2f);
            }
            else
            {
                const float TopMarginUnits = 0.25f;
                float delta = -TopMarginUnits - (box.itemContainer.localPosition.y + topEdge);
                _scroll.MoveScroll(delta);
            }
        }

        // IScrollable — window height from the item container's layout (feeds scroll clipping).
        public void UpdateContainingElements(float scroll) { }

        public bool IsBottomElementSelected() => false;

        public bool IsTopElementSelected() => false;

        // Matches ModSettingsScreen's own GetCurrentWindowHeight — the top layout's self-reported
        // render height feeds UIScrollWindow's scroll clipping.
        public float GetCurrentWindowHeight() => _layout != null ? _layout.GetUIComponentRenderHeight() : 0f;

        // Called from a row's OnDeselected (ListDetailItem). Reads every row's live text (trimmed,
        // commas stripped so a typed comma can't desync the stored split/join), and re-persists the
        // whole list ONLY if it actually changed — skips a no-op write (and the rebuild it would
        // otherwise trigger on every plain navigate-through, not just real edits). The rebuild itself
        // is deferred to Update — see the design note above this task for why.
        public void OnRowTextCommitted(ListDetailItem row)
        {
            if (_activeDef?.Entry == null)
                return;
            var tokens = new List<string>();
            foreach (var opt in menuOptions)
            {
                // RadicalMenu's own includeInactive option scan also registers the itemTemplate
                // itself (see ListDetailItem.GetActiveStateInCurrentScene's comment) — its pugText
                // still carries the prefab-authored "List Entry" placeholder forever, since only
                // clones ever get SetInputText. Skip it exactly like navigation already does, via
                // the same active check, or its placeholder text gets committed as a phantom token.
                if (!(opt is ListDetailItem item) || !item.gameObject.activeSelf)
                    continue;
                var text = item.GetInputText().Trim().Replace(",", "");
                if (text.Length > 0)
                    tokens.Add(text);
            }
            string joined = string.Join(",", tokens);
            if (joined == Value())
                return;
            _activeDef.Entry.BoxedValue = joined;
            _rebuildPending = true;
            _lastCommitWasAddRow = row.isAddRow;
        }

        private void Update()
        {
            if (!_rebuildPending)
                return;
            _rebuildPending = false;
            int previousIndex = selectedIndex;
            bool wasAddRow = _lastCommitWasAddRow;
            RebuildRows();
            // The initial open (Activate) renders AFTER RebuildRows for the same reason: a LinearLayout
            // only measures active children, so each row's height must be (re)computed here too, or the
            // freshly-rebuilt rows collapse to their default (near-zero) height and overlap.
            RenderContent();
            selectedIndex = -1; // stale index from before the rebuild — reset so SelectOptionIndex's
            // no-op guard and range check don't see a wrong/out-of-range value
            // After adding a token (the add-row had content), navigate onto the fresh blank add-row
            // that follows it — one Enter fully exits edit mode either way; adding another token still
            // needs an explicit activate on this now-selected row. Any other edit/removal just keeps
            // the same numeric slot (clamped).
            int target = wasAddRow ? menuOptions.Count - 1 : Mathf.Clamp(previousIndex, 0, menuOptions.Count - 1);
            SelectOptionIndex(target);
        }
    }
}
