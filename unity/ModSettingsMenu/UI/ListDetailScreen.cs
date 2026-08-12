using System.Collections.Generic;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The list drill-in detail screen: a pushed RadicalMenu showing one comma-list in full — a
    /// title plus one navigable row per token, scrollable, each row a live text-input field
    /// (add/edit/remove a token, committed on Enter/Escape/click-away — see ListDetailItem).
    /// A genuinely read-only SettingDef (SettingDef.ReadOnly) still shows every row navigable for
    /// viewing, just without the trailing add-row and without ever entering edit mode. Controller/
    /// keyboard navigation walks the rows and scroll-follow reaches the bottom (the overflow fix
    /// that motivated this screen over a single truncated Info preview).
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

        // this open session's own copy of _activeDef.ReadOnly — set in Populate from _pending (which
        // Activate() nulls right after)
        private bool _readOnly;

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
        //
        // Clear Manager.input.activeInputField FIRST, THEN persist — not the other way around.
        // OnRowTextCommitted writes into a (possibly third-party mod's) live ConfigEntry, whose
        // whole CoreLib save chain (OnSettingChanged -> ConfigFile.Save -> API.ConfigFilesystem.Write)
        // has no exception handling anywhere along it; a fault there (a foreign mod's own
        // SettingChanged handler throwing, or a Wine filesystem fault — six IL patches exist in this
        // project specifically because those happen) would previously have skipped BOTH the
        // Deactivate(commit: false) call below AND base.Deactivate(pop) right after, since an
        // uncaught exception unwinds the rest of THIS method too. Manager.input.activeInputField is a
        // plain interface reference (InputManager.TextInputInterface), not a UnityEngine.Object — it
        // has none of Unity's "destroyed-but-not-null" comparison semantics, so a row left referenced
        // there stays referenced forever, not just until GC. Both of MenuPatch's Harmony prefixes key
        // off exactly this field via an `is ListDetailItem` check, so a stuck reference would disable
        // ALL menu selection game-wide (mouse and keyboard) until restart — clearing it first means
        // that guard is already lifted even if the persist step below throws.
        //
        // Reordering does not lose the commit-before-teardown intent: OnRowTextCommitted reads the
        // row's own live text (GetInputText()), which Deactivate(commit: false) does not touch — only
        // Manager.input.activeInputField and the caret blinker's visibility change. And it cannot
        // double-commit: the only thing that reacts to the activeInputField-cleared transition is
        // ListDetailItem.Update()'s own check, which needs the row's GameObject to still be receiving
        // Update() calls — base.Deactivate(pop), which deactivates the whole hierarchy, runs
        // immediately after this method returns, so that check never fires again for this row.
        public override void Deactivate(bool pop)
        {
            if (Manager.input.activeInputField is ListDetailItem activeItem && activeItem.owner == this)
            {
                activeItem.Deactivate(commit: false);
                OnRowTextCommitted(activeItem);
            }
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

            // ListDetailScreen is a singleton reused for every list — selectedIndex survives across
            // opens of DIFFERENT lists (RadicalMenu's own field, never reset by this screen otherwise).
            // A stale index from a longer previous list is out of range for a shorter one, and
            // RadicalMenu.Activate() indexes menuOptions[selectedIndex] UNGUARDED via
            // DeselectAnyCurrentOption() whenever SystemIsUsingMouse() — throwing before base.Activate()
            // ever reaches RenderContent() below, so every row keeps its prefab-default
            // renderHeightPixels (0) and collapses onto the same position. -1 is RadicalMenu's own
            // "nothing selected" sentinel (its declared default, and what every one of its own range
            // checks treats as safe) — the same reset the post-edit rebuild path in Update() already
            // does for the identical reason.
            selectedIndex = -1;

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
                item.hintString = Loc.T("ModSettingsMenu-UI/ListAddHint", "+ Add");
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

        // Called from two places, neither of them OnDeselected (that fires on mere mouse hover, which
        // is exactly why the trigger moved away from it — see ListDetailItem.OnDeselected's own
        // comment): ListDetailItem.Update()'s own activeInputField-transition check, and this screen's
        // own Deactivate() as a close-time safety net. Reads every row's live text (trimmed, commas
        // stripped so a typed comma can't desync the stored split/join), and re-persists the whole
        // list ONLY if it actually changed — skips a no-op write (and the rebuild it would otherwise
        // trigger) on a plain activate-then-deactivate that never touched the text. The rebuild itself
        // is deferred to _rebuildPending/Update() rather than run synchronously here — this method is
        // most often reached from inside `row`'s own Update() (via ListDetailItem.Update() above),
        // and RebuildRows() destroys every current row, `row` included; deferring one frame keeps this
        // call from tearing down its own caller's GameObject mid-callback.
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
            // Compare against the STORED value tokenized the same way (Split/Trim/drop-empty, exactly
            // RebuildRows's own rule) rather than the raw string — Value() may carry authoring
            // formatting (e.g. "Alpha, Beta, Gamma" with a space after each comma) that joined never
            // reproduces, so a raw comparison never matched and every mere open+close (no real edit at
            // all) wrote and rebuilt regardless, defeating the whole point of this no-op guard.
            var existingTokens = new List<string>();
            foreach (var raw in Value().Split(','))
            {
                var token = raw.Trim();
                if (token.Length > 0)
                    existingTokens.Add(token);
            }
            if (joined == string.Join(",", existingTokens))
                return;
            _activeDef.Entry.BoxedValue = joined;
            // Mirrors SettingWidget.Adjust's identical line: a restart-required setting that actually
            // changed marks the menu dirty; leaving ModSettingsScreen (not this drill-in — the flag is
            // static and consumed by ModSettingsScreen.Deactivate, since the drill-in is only ever
            // pushed on top of it) then raises CK's restart prompt.
            if (_activeDef.RequiresRestart)
                ModSettingsScreen.RestartPending = true;
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
