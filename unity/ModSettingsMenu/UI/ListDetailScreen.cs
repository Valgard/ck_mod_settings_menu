using System.Collections.Generic;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// The list drill-in detail screen: a pushed RadicalMenu showing one comma-list in full — a
    /// title plus one navigable row per entry, scrollable, each row a live text-input field
    /// (edit or clear an entry, committed on Enter/Escape/click-away — see ListDetailItem); adding
    /// goes through the trailing button (ListAddRow), not by typing into a row.
    /// A genuinely read-only SettingDef (SettingDef.ReadOnly) still shows every row navigable for
    /// viewing, just without the trailing add button and without ever entering edit mode. Controller/
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

        private SettingDef _pending; // seeded by Open() before PushMenu resolves this instance
        private SettingDef _activeDef; // the setting this open session is showing/editing — set once

        // this open session's own copy of _activeDef.ReadOnly — set in Populate from _pending (which
        // Activate() nulls right after)
        private bool _readOnly;

        // The rows this open session owns. While the drill-in is open THESE are the truth,
        // not the stored value: an empty row has to survive an edit to a different row, and
        // it can only do that by existing somewhere the stored value does not reach (the
        // value never carries an empty token — see the assembly in OnRowTextCommitted).
        // Populate seeds it, RebuildRows renders it, commit derives the value from it.
        // ListTokenizer is unchanged and still drops empties: it now describes how a stored
        // value becomes an initial row list, not what is on screen.
        private readonly List<string> _rows = new List<string>();

        // A rebuild's explicit selection target, or RowSelection.None to keep the previous slot (clamped).
        private RowSelection _pendingSelect = RowSelection.None;

        // The in-row slot the selection last occupied, so a vertical step keeps the column.
        private ListRowButton.Role? _lastFocusedSlot;

        // Bumped once per open. This is the ONLY thing in the design that marks a session boundary:
        // the screen is a singleton reused for every list, so `_owner` cannot tell two sessions
        // apart and a row's index is a coordinate with no coordinate system. A row takes this value
        // from the owner it is bound to (ListDetailItem.Bind), so a row from a previous session
        // carries a stale one and can be recognised instead of silently writing into whatever
        // setting happens to be open now.
        private int _rowGeneration;
        internal int RowGeneration => _rowGeneration;

        private bool _rebuildPending; // set by OnRowTextCommitted and AddEmptyRow, consumed by Update
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
            if (Manager.input.activeInputField is ListDetailItem activeItem && activeItem.Owner == this)
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
            // Everything below marks the boundary to the previous session, and all of it must move
            // together: the generation stamp rows are bound with, the row list itself, and BOTH
            // deferred-rebuild fields. _rows is cleared here rather than after the wiring guard so a
            // bailed-out open cannot leave the previous session's rows paired with the new setting.
            _rowGeneration++;
            _rows.Clear();
            _rebuildPending = false; // a stale deferred rebuild from a prior open can't apply here
            _pendingSelect = RowSelection.None; // ...and neither can its selection target
            _scroll = GetComponent<UIScrollWindow>();
            if (box == null || box.itemContainer == null || box.itemTemplate == null || box.addRow == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetailScreen prefab not wired (box/itemContainer/itemTemplate/addRow) — detail stays empty.");
                return;
            }
            if (box.itemTemplate.GetComponent<ListDetailItem>() == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetailScreen itemTemplate lacks its ListDetailItem component — detail stays empty.");
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

            _rows.AddRange(ListTokenizer.Tokenize(Value())); // cleared above, with the rest of the session state

            RebuildRows();

            // ListDetailScreen is a singleton reused for every list — selectedIndex survives across
            // opens of DIFFERENT lists (RadicalMenu's own field, never reset by this screen
            // otherwise), so without this an index left over from a longer previous list would be
            // applied to a shorter new one.
            //
            // **Correction (2026-08-23):** this used to claim RadicalMenu.Activate() indexes
            // menuOptions[selectedIndex] UNGUARDED via DeselectAnyCurrentOption(). It does not —
            // that method checks `selectedIndex != -1 && selectedIndex < menuOptions.Count` before
            // dereferencing, and the non-mouse branch clamps. The reset is still right, but as
            // correctness rather than crash avoidance: a stale index would silently select the wrong
            // row of a different setting's list. Do not read the old rationale as licence to drop
            // this line, and do not trust "unguarded" claims about CK without opening the method.
            //
            // -1 is RadicalMenu's own "nothing selected" sentinel (its declared default, and what
            // every one of its range checks treats as safe) — the same reset the post-edit rebuild
            // path in Update() does, there because SelectOptionIndex's `selectedIndex == index`
            // early-out would otherwise skip re-selecting a slot whose row is a different object.
            selectedIndex = -1;

            if (_scroll != null)
            {
                _scroll.scrollingContent = box.itemContainer;
                _scroll.ResetScroll();
            }
        }

        // Destroys every current row — real tokens, plus the trailing add button for an editable list
        // (a read-only list has none, see the !_readOnly guard below) — and rebuilds them fresh from
        // _rows, this open session's own row list (Populate seeds it from the stored value; commit
        // writes the edited row back into it). The SAME rebuild-from-_rows path serves the initial
        // open (Populate) and every post-edit refresh (OnRowTextCommitted, via the deferred Update
        // path) — there is no separate incremental add/remove/edit logic, just "re-render every row
        // the session currently holds."
        //
        // The full teardown-and-recreate must NOT be optimised into an in-place update of the
        // existing rows: destroying a row is what resets PugTextEffectMenuOption.isValueText, which
        // ListDetailItem.OnActivated flips to true (the vivid editing colour) and nothing else ever
        // reverts — a reused row would stay stuck in the "actively editing" tint forever.
        private void RebuildRows()
        {
            // Wiring check repeated from Populate rather than assumed: Update() reaches this method
            // on the deferred path without passing Populate again, so on an unwired prefab the loop
            // below would be the first thing to dereference a null and would report an NRE instead
            // of the cause.
            if (box == null || box.itemContainer == null || box.itemTemplate == null || box.addRow == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetailScreen rebuild skipped — prefab not wired (box/itemContainer/itemTemplate/addRow).");
                return;
            }

            // Clear the previous rows. Detach BEFORE Destroy (deferred to end-of-frame), else a rebuild
            // this same frame would count the stale rows and mis-size the layout.
            for (int i = box.itemContainer.childCount - 1; i >= 0; i--)
            {
                var child = box.itemContainer.GetChild(i).gameObject;
                // Remove only the rows THIS method created. Anything else in the container belongs
                // to the prefab and outlives every rebuild — today that is the add button, which is
                // a live object rather than a clone precisely because there is only ever one of it.
                // Phrasing the teardown as "my own rows" instead of "everything but that object"
                // keeps it a statement about ownership rather than a carve-out.
                if (child.GetComponent<ListDetailItem>() == null)
                    continue;
                // PugText pools its glyph SpriteRenderers (usePooledResources); destroying a row
                // without releasing them first leaks pooled glyphs every rebuild (this screen
                // rebuilds on every edit, unlike a static list built once per open) until the
                // pool runs dry and an unrelated screen's PugText renders blank next (reproduced:
                // ModSettingsScreen's own rows went text-less after editing a list). Release every
                // PugText in the row (pugText + hintText) before destroying, mirroring the identical
                // item-checklist fix and ModSettingsScreen's own section-teardown fix.
                foreach (var text in child.GetComponentsInChildren<PugText>(includeInactive: true))
                    text.Clear();
                // Silence the row BEFORE detaching it. Destroy is deferred to end-of-frame, and
                // detaching a child that is activeSelf out of an inactive hierarchy makes it a root
                // object — i.e. active again — so a doomed row can still receive Update() calls in
                // this frame. A row that was mid-edit when the screen closed would use one of them
                // to fire a commit against the list that is open NOW. A disabled component gets no
                // Update at all, which closes that off at the source rather than at the write path.
                foreach (var doomed in child.GetComponentsInChildren<RadicalMenuOption>(includeInactive: true))
                    doomed.enabled = false;
                child.transform.SetParent(null, worldPositionStays: false);
                Object.Destroy(child);
            }
            menuOptions.Clear();

            // One row per entry — including empty ones, which is the whole point: they are
            // invisible to the stored value but must stay on screen until the drill-in closes.
            for (int i = 0; i < _rows.Count; i++)
                AddItem(_rows[i], i);
            // ...plus the permanent trailing button for adding a new token — a read-only list has
            // nothing to add, so it is switched off entirely rather than left inert.
            //
            // menuOptions was just cleared, so the button has to re-register even though the object
            // itself survived; and it has to move back to the end, because the rows above were
            // Instantiate()d into the container and therefore landed AFTER it. The LinearLayout
            // stacks in hierarchy order, so sibling order is the row order.
            box.addRow.gameObject.SetActive(!_readOnly);
            if (!_readOnly)
            {
                box.addRow.transform.SetAsLastSibling();
                box.addRow.Bind(this);
                box.addRow.SetParentMenu(this);
                menuOptions.Add(box.addRow);
            }

            ChainRowsForUIElementNavigation();
        }

        // Link each row to its vertical neighbours. Only the UIElement navigation path reads these
        // (RadicalMenu.useUIElementsForNavigation -> SelectIndexInDirection -> GetAdjacentUIElement),
        // and on that path an empty list means no navigation at all rather than a fallback — so this
        // has to run on every rebuild, for rows that exist only from now on.
        //
        // This is CK's own arrangement for a dynamically built list, not an invention: what sits
        // INSIDE a row is wired in the prefab (sibling fileIDs survive Instantiate), what sits
        // BETWEEN rows is assigned here, because row N cannot know row N+1 before either exists.
        // ChooseCharacterMenu does exactly this for its save slots, SelectWorldMenu for its worlds.
        //
        // The chain is CYCLIC: the first row's top neighbour is the last one, the last row's bottom
        // neighbour is the first. That keeps the wrap-around this screen has always had — the index
        // path gets it free from `(i + 1) % Count`, and losing it on the UIElement path was a
        // regression, not a design change (reported from a play session).
        //
        // Wrapping through the chain rather than through an override of the navigation methods is
        // what makes it correct rather than merely present: CK's own GetClosestUIElementInList still
        // applies, so a wrap target that is scrolled out of view is accepted via
        // UIElementsSharesScrollWindow, and OnSelectedOptionChanged scrolls to it like any other
        // step. An override would have had to reproduce all of that by hand.
        //
        // It is also what vanilla does. CreateWorldMenu and WorldSettingsMenu both carry a chain
        // that is cyclic in BOTH directions, wired in the prefab — CK wraps wherever the screen is a
        // real vertical pick-list, and leaves it open on forms (Join Game) and short button rows
        // (Pause Menu). What it cannot do in a prefab is a list whose length is unknown until it
        // opens, which is why the code-side precedents (ChooseCharacterMenu, SelectWorldMenu) chain
        // linearly and stop: they are wiring rows that do not exist yet, not declining to wrap.
        // Doing the ring here is therefore the same convention, expressed the only way a dynamic
        // list can express it.
        private void ChainRowsForUIElementNavigation()
        {
            int last = menuOptions.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                var option = menuOptions[i];
                if (option == null)
                    continue;
                // Fresh lists rather than Clear()+Add: a row is a clone of the template, so it may
                // carry whatever the template's own lists hold, and reusing that instance would
                // quietly share it between rows.
                //
                // A single row gets empty lists rather than a cycle onto itself: the wrap would
                // resolve to the row already selected, which is a no-op with a selection SFX.
                if (last == 0)
                {
                    option.topUIElements = new List<UIelement>();
                    option.bottomUIElements = new List<UIelement>();
                    continue;
                }
                option.topUIElements = new List<UIelement> { menuOptions[i > 0 ? i - 1 : last] };
                option.bottomUIElements = new List<UIelement> { menuOptions[i < last ? i + 1 : 0] };
            }
        }

        // Clone the (inactive) item template into the container, seed its text, register it as a
        // navigable menu option. The template being inactive makes Instantiate(_, parent) produce an
        // inactive clone (no mid-clone OnEnable/NRE); SetActive(true) then activates it cleanly.
        private void AddItem(string token, int rowIndex)
        {
            var row = Object.Instantiate(box.itemTemplate, box.itemContainer);
            row.SetActive(true);
            var item = row.GetComponent<ListDetailItem>();
            if (item == null)
                return;
            item.Bind(this, rowIndex, _readOnly);
            item.RefreshButtonStates(_rows.Count);
            if (item.pugText != null)
                item.pugText.localize = false; // cloned PugText inherits localize=true — same trap as
            // SettingWidget.SetText and the hintText line below; must be
            // set before SetInputText's internal Render() call, not after
            item.SeedText(token); // not SetInputText: the row must remember what the value said
            if (item.hintText != null)
            {
                item.hintText.localize = false; // same cloned-PugText trap as pugText above — every
                // row's hintText clone otherwise inherits localize=true and renders its prefab-authored
                // literal placeholder ("Hint Text") as a failed loc-term lookup ("missing: Hint Text")
                item.hintText.SetText("");
            }
            item.SetParentMenu(this);
            menuOptions.Add(item);
        }

        // Called from the button's OnActivated, i.e. from inside a row's own callback — so the
        // rebuild is DEFERRED through _rebuildPending exactly like a commit is. Rebuilding here
        // would destroy the very row whose callback is still on the stack.
        internal void AddEmptyRow()
        {
            _rows.Add("");
            _rebuildPending = true;
            // The new row lands last in _rows, and the button follows it — so this index is the
            // new row, not the button. Selected but NOT activated, and that choice is LOAD-BEARING
            // for the on-screen keyboard: entering edit mode here would raise it on a controller
            // unasked. It used to be load-bearing a second way too — the base class's width trim
            // (Pug.Other:343398) could have masqueraded as a keystroke if a row were active on its
            // creation frame, since auto-activating would have run the trim inside an active edit
            // window and set _edited from a change the user never made — but that trim no longer runs
            // (ListDetailItem.maxWidth is 0; see its class-level note). SeedText/RenderContent's
            // baseline-then-rebaseline against the (now hypothetical) trim is redundancy today, not a
            // second reason this line can't simply be reversed.
            _pendingSelect = new RowSelection(_rows.Count - 1, null);
        }

        // Swap a row with its neighbour. Deferred through _rebuildPending exactly as AddEmptyRow is,
        // and for a stronger version of the same reason: this runs from inside a button's own
        // OnActivated, and the rebuild destroys the very row that button lives in.
        //
        // A cheaper shape exists — re-seed only the two affected rows and leave the focus physically
        // in place — and is deliberately not taken: every other write path on this screen defers
        // through _rebuildPending, and a second, shortcutting path would have to swap both rows'
        // RowIndex bindings by hand. That is the class of special case ADR-005 split the row types
        // to be rid of.
        internal void MoveRow(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= _rows.Count || target < 0 || target >= _rows.Count)
                return;
            (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
            WriteValueFromRows();
            // Unconditionally, ignoring the return value: swapping two identical tokens leaves the
            // stored value untouched, and the rows still have to redraw in their new order.
            _rebuildPending = true;
            // The selection follows the ROW, and stays on the same button, so a further press keeps
            // moving the same entry. Landing back on the text field would make moving an entry four
            // places cost eight inputs instead of four.
            _pendingSelect = new RowSelection(target, delta < 0 ? ListRowButton.Role.MoveUp : ListRowButton.Role.MoveDown);
        }

        // A row's button was pressed. The button knows its role and its row and nothing else; the
        // list lives here. Same division as ListAddRow's `_owner?.AddEmptyRow()`.
        internal void OnRowButtonActivated(ListDetailItem row, ListRowButton.Role role)
        {
            if (row == null || row.Generation != RowGeneration)
                return;
            switch (role)
            {
                case ListRowButton.Role.MoveUp:
                    MoveRow(row.RowIndex, -1);
                    break;
                case ListRowButton.Role.MoveDown:
                    MoveRow(row.RowIndex, +1);
                    break;
                case ListRowButton.Role.Delete:
                    // Intentionally inert until Task 6 adds RequestDelete. Pressing ✕ does nothing
                    // in this intermediate state; it is never shipped in it.
                    break;
            }
        }

        // Records which in-row control last took focus, so the NEXT row entered by a vertical step
        // can be seeded into the same column (see OnSelectedOptionChanged below). Called from
        // ListRowButton.OnSelected (a slot) and ListDetailItem.OnSelected (null — the text field),
        // never written directly by this class.
        internal void NoteFocusedSlot(ListRowButton.Role? slot) => _lastFocusedSlot = slot;

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
                // Each row type reports its own height through IListRow, measured from its frame
                // rather than from its text — see ListDetailItem.RowHeightPx for why. The interface
                // exists so a new row kind cannot be forgotten here: a row left unmeasured keeps the
                // prefab's renderHeightPixels of 0, which the LinearLayout collapses to nothing.
                var row = go.GetComponent<ListDetailItem>();
                // The row has rendered by now, so the base class would have already trimmed anything
                // too wide — moot since ListDetailItem.maxWidth is 0 (see its class-level note); this
                // call is now redundancy rather than a correction. Re-baseline the edit detector
                // against what is actually on screen anyway — see ListDetailItem.RebaselineEditDetector.
                row?.RebaselineEditDetector();
                int px = go.GetComponent<IListRow>()?.RowHeightPx ?? 0;
                var wrap = go.GetComponent<WrapperUIComponent>();
                if (px > 0 && wrap != null)
                    wrap.renderHeightPixels = px;
            }
            _layout.RenderUIComponent(force: true); // re-lay out with the measured heights
        }

        // Enter the list on the first arrow key when nothing is selected yet. Both overrides exist
        // only to restore what the index path gave for free and the UIElement path does not.
        //
        // RadicalMenu.SelectNextIndex computes `(selectedIndex + 1) % Count`, and with no selection
        // that is `(-1 + 1) % Count` = 0 — the first row, without anyone having to ask for it. The
        // UIElement path instead goes through SelectIndexInDirection, which handles the empty
        // selection only for a controller:
        //
        //     if (selectedMenuOption == null && !Manager.input.SystemPrefersKeyboardAndMouse())
        //         return SelectOptionIndex(DefaultOptionIndex);
        //
        // With a mouse attached, CK assumes the selection follows the pointer and returns false —
        // so opening this screen from the keyboard left the arrow keys dead until the mouse had
        // touched a row once. Reported from a play session; CK's own list menus share the gap.
        //
        // Deliberately NOT solved by selecting a row in Activate(): that would force a highlight on
        // a mouse user who never asked for one, which is exactly the behaviour CK avoids here. The
        // entry stays lazy — it happens on the first arrow key and no earlier.
        //
        // The entry point follows the direction of the key: down enters at the top, up enters at the
        // BOTTOM. That is what the wrap-around implies — pressing up from nothing is the same
        // gesture as pressing up from the first row, and both should land on the last one. (The
        // index path's own answer here is neither: `(-1 - 1 + Count) % Count` lands on the
        // second-to-last row, an arithmetic accident nobody would ask for.)
        public override bool SelectNextIndex() => EnterListIfNothingSelected(0) || base.SelectNextIndex();

        public override bool SelectPrevIndex() => EnterListIfNothingSelected(menuOptions.Count - 1) || base.SelectPrevIndex();

        private bool EnterListIfNothingSelected(int index)
        {
            if (menuOptions.Count == 0 || GetSelectedMenuOption() != null)
                return false;
            return SelectOptionIndex(index);
        }

        // Scroll the viewport so the selected row follows keyboard / controller navigation (the base
        // RadicalMenu never does this itself). Items are DIRECT children of the scroll content, so the
        // origin is the raw localPosition.y — the 1-level form CK's own scrollable menus use.
        protected override void OnSelectedOptionChanged()
        {
            base.OnSelectedOptionChanged();
            // Standing in the ✕ column and pressing down should land on the ✕ below, the expectation
            // any table sets — and "clear out several entries" is the case these buttons exist for.
            // The slot lives on the row, so moving it here is what makes it a COLUMN rather than a
            // per-row accident.
            if (_lastFocusedSlot.HasValue && selectedIndex >= 0 && selectedIndex < menuOptions.Count && menuOptions[selectedIndex] is ListDetailItem entering)
                entering.FocusedSlot = _lastFocusedSlot;
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
        // own Deactivate() as a close-time safety net. Reads the COMMITTING row's live text
        // (whitespace-trimmed via ListTokenizer.Sanitize, commas stripped so a typed comma can't
        // desync the stored split/join — unrelated to the base class's own width trim discussed
        // below, which no longer runs) back into _rows,
        // assembles the whole list from _rows, and re-persists it ONLY if it actually changed — skips
        // a no-op write (and the rebuild it would otherwise trigger) on a plain
        // activate-then-deactivate that never touched the text. The rebuild itself
        // is deferred to _rebuildPending/Update() rather than run synchronously here — this method is
        // most often reached from inside `row`'s own Update() (via ListDetailItem.Update() above),
        // and RebuildRows() destroys every current row, `row` included; deferring one frame keeps this
        // call from tearing down its own caller's GameObject mid-callback.
        public void OnRowTextCommitted(ListDetailItem row)
        {
            if (_activeDef?.Entry == null)
                return;
            // Only the committing row can have changed — it is the only one that could hold
            // activeInputField. Write it back at its own index, then derive the value from the
            // list.
            //
            // Two things together used to keep the base class's per-frame width trim out of a
            // third-party mod's config file — moot since ListDetailItem.maxWidth is 0 (see its
            // class-level note), kept as redundancy rather than as an active safeguard: reading the
            // other rows from _rows rather than off screen (they were never touched, so their stored
            // text stands), and asking this row for CommittedText rather than GetInputText — which
            // hands back the seeded token unless a keystroke actually changed it. Either alone would
            // have left half the hazard open.
            //
            // Dropping that menuOptions walk also retires the guard it needed: the walk saw the
            // inactive itemTemplate too (RadicalMenu's own option scan includes it — see
            // ListDetailItem.GetActiveStateInCurrentScene's comment), whose pugText carries the
            // prefab-authored "List Entry" placeholder forever, since only clones ever get
            // SetInputText; an activeSelf check kept that placeholder from being committed as a
            // phantom token. Nothing walks menuOptions any more, so the template is unreachable
            // from here by construction rather than by a check.
            //
            // The two checks below are LOUD on purpose. Neither has a legitimate case any more —
            // the add button is a different type and cannot arrive here at all — so reaching one
            // means a row outlived its session or was never bound, and the user's edit is about to
            // be discarded. Silence there would look exactly like a successful save.
            if (row.Generation != _rowGeneration)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] Ignoring a commit from a stale drill-in row (generation {row.Generation}, current {_rowGeneration}) — its edit is discarded."
                );
                return;
            }
            int index = row.RowIndex;
            if (index < 0 || index >= _rows.Count)
            {
                Debug.LogWarning(
                    $"[ModSettingsMenu] Ignoring a commit from an unbound drill-in row (index {index}, {_rows.Count} rows) — its edit is discarded."
                );
                return;
            }
            // Sanitize on the way in, not just on the way out. Join sanitizes too — it must, to stay
            // total for any caller — but a row is rebuilt FROM _rows, so leaving a typed comma in
            // there would show the user a character that silently disappears when the value is
            // written. The rule itself lives in ListTokenizer; this is only the second place it is
            // applied.
            _rows[index] = ListTokenizer.Sanitize(row.CommittedText);
            if (!WriteValueFromRows())
                return;
            _rebuildPending = true;
        }

        // The one place the row list becomes the stored value. Extracted from OnRowTextCommitted so
        // the reorder and delete paths cannot drift from the edit path — the four call sites
        // ListTokenizer itself had to unify are the precedent for why that matters here.
        //
        // Returns whether the stored value actually changed, so a caller can tell a real write from
        // a no-op. Callers still decide for themselves whether to rebuild: a reorder of two
        // identical tokens changes nothing here and must redraw anyway, because the ROWS moved.
        private bool WriteValueFromRows()
        {
            if (_activeDef?.Entry == null)
                return false;
            // Join through ListTokenizer, not by hand: dropping the empties is the same rule
            // Tokenize applies when reading, and the comparison below depends on both sides having
            // gone through it.
            string joined = ListTokenizer.Join(_rows);
            // Compare against the STORED value tokenized the same way rather than against the raw
            // string — Value() may carry authoring formatting (e.g. a space after each comma) that a
            // join never reproduces, so a raw comparison never matched and every mere open+close
            // wrote and rebuilt regardless.
            if (joined == ListTokenizer.Join(ListTokenizer.Tokenize(Value())))
                return false;
            _activeDef.Entry.BoxedValue = joined;
            // Mirrors SettingWidget.Adjust's identical line. The flag is static and consumed by
            // ModSettingsScreen.Deactivate, since this drill-in is only ever pushed on top of it.
            // It lives HERE rather than in OnRowTextCommitted so that every path which changes the
            // value raises it, not only the typing one — the ShortRestart fixture exists to catch
            // exactly that gap.
            if (_activeDef.RequiresRestart)
                ModSettingsScreen.RestartPending = true;
            return true;
        }

        private void Update()
        {
            if (!_rebuildPending)
                return;
            _rebuildPending = false;
            int previousIndex = selectedIndex;
            RowSelection explicitTarget = _pendingSelect;
            _pendingSelect = RowSelection.None;
            RebuildRows();
            // The initial open (Activate) renders AFTER RebuildRows for the same reason: a LinearLayout
            // only measures active children, so each row's height must be (re)computed here too, or the
            // freshly-rebuilt rows collapse to their default (near-zero) height and overlap.
            RenderContent();
            selectedIndex = -1; // stale index from before the rebuild — reset so SelectOptionIndex's
            // no-op guard and range check don't see a wrong/out-of-range value
            // A caller that knows where the selection must land says so via _pendingSelect — today
            // that is AddEmptyRow, aiming at the row the add button just appended, since the numeric
            // slot the button itself occupies would land on the button again. Everything else leaves
            // it at -1, meaning "keep the same numeric slot" (clamped), which is what every
            // edit/removal wants anyway.
            // Nothing to select is a legitimate outcome, and Mathf.Clamp cannot express it: with an
            // empty list the bounds invert (0 .. -1), and Clamp's `if (v < min) v = min; else if
            // (v > max) v = max;` then yields -1 for any non-negative target — or 0 for a negative
            // one. Both index a list that has none. Unreachable as things stand (an editable list always
            // has the add button, and a read-only one can never set _rebuildPending), which is
            // exactly why it needs saying: the next row kind or a read-only rebuild path would make
            // it reachable without anyone looking at this line.
            if (menuOptions.Count == 0)
                return;
            int target = explicitTarget.HasRow ? explicitTarget.Row : previousIndex;
            int clamped = Mathf.Clamp(target, 0, menuOptions.Count - 1);
            if (explicitTarget.Slot.HasValue && menuOptions[clamped] is ListDetailItem restored)
                restored.FocusedSlot = explicitTarget.Slot;
            SelectOptionIndex(clamped);
        }
    }
}
