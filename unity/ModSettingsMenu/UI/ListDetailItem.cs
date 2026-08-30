using System.Collections.Generic;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// One navigable, EDITABLE row of the list drill-in detail screen. Built on CK's own
    /// RadicalMenuOptionTextInput — the same base class the character-name field
    /// (CharacterCustomizationOption_NameInput) uses — so it gets on-screen-keyboard support
    /// (controller sessions), focus/blink handling, and the read-vs-edit visual split for free:
    /// looks like a static value while merely navigated onto (OnSelected, inherited), and only
    /// captures real input once confirmed (OnActivated -> Manager.input.SetActiveInputField(this),
    /// inherited unchanged). Committing happens when this row STOPS being
    /// Manager.input.activeInputField (Update() below) — not on OnDeselected, which CK's own
    /// UIMouse also fires on mere hover; while a row holds activeInputField, clicking a DIFFERENT
    /// row is ignored (OnLeftClicked below) so only Enter/Escape (Deactivate) or the drill-in
    /// screen closing can end an edit. See ListDetailScreen.OnRowTextCommitted for the actual
    /// persist-and-rebuild logic; this class only reports the event and what it may contribute —
    /// CommittedText, which is deliberately NOT the current text: an untouched row hands back the
    /// token it was seeded with, so a display-side truncation cannot reach a foreign config file.
    /// </summary>
    public sealed class ListDetailItem : RadicalMenuOptionTextInput, IListRow
    {
        // The resting frame — a child of the row, mirroring CK's own sessionIP field where `border`
        // sits beside `selectedBorder` under the text input. Serialized because it is a prefab
        // reference, unlike the runtime identity below.
        //
        // There is deliberately NO second field for the focus frame: the base class already carries
        // `selectedMarker`, and it points at the very GameObject that frame lives on. A second
        // serialized reference to the same object could be pointed elsewhere by accident, after
        // which the base class would keep toggling the marker while this class enabled a stranger's
        // renderer — invisible in the Inspector, visible only in game.
        [SerializeField]
        private SpriteRenderer fieldBorder;

        // The row's own horizontal clip, authored in the prefab beside the frame. Serialized for
        // the same reason fieldBorder is: it is a prefab reference, not a runtime identity.
        [SerializeField]
        private SpriteMask fieldMask;

        // Owning screen and row identity — private + Bind(), not raw public fields, matching
        // SettingWidget.Bind/ListWidget.Bind's established idiom elsewhere in this framework.
        private ListDetailScreen _owner;
        public ListDetailScreen Owner => _owner;

        // The row's three in-row buttons, assigned in the prefab. An array rather than three fields
        // because every use is a loop over them; the role lives on the button itself.
        [SerializeField]
        private ListRowButton[] rowButtons;

        // Exposed so the screen can register each button as its own menuOptions entry and wire its
        // vertical neighbours — a button is a real, independently selectable menu option now (see
        // ListRowButton), not something this row forwards navigation or activation to on its behalf.
        internal ListRowButton[] RowButtons => rowButtons;

        // The buttons this row actually offers at the level it was bound at, in prefab column order.
        // ONE place asks ShowsRole and one place null-checks the serialized array, for the two
        // callers that must agree: registration as a menu option (ListDetailScreen.AddItem) and the
        // navigation chain (RowElements). If those two ever disagree, a button is either invisible
        // but selectable — the selection lands on it and cannot leave — or wired but absent.
        //
        // This row owns both inputs (its buttons and its level), which is why the question belongs
        // here rather than being asked twice at the call sites in opposite polarity.
        //
        // NOT usable by RefreshButtonStates: that one must walk EVERY button, because Refresh is
        // what switches a hidden one off in the first place.
        internal List<ListRowButton> OfferedButtons()
        {
            var offered = new List<ListRowButton>();
            if (rowButtons == null)
                return offered;
            foreach (var button in rowButtons)
            {
                if (button != null && ListRowButton.ShowsRole(_editing, button.ButtonRole))
                    offered.Add(button);
            }
            return offered;
        }

        // Refresh every button against this row's current position. Called on every rebuild, because
        // a row is a fresh clone each time and its buttons start unbound — an unbound button renders
        // normally and does nothing when pressed. What each button does with rowIndex/rowCount/
        // access (its own disabled state, and whether its role is offered at all) is its own decision
        // now — see ListRowButton.Refresh; this loop only owns the null check over a serialized array
        // a prefab slot can leave empty.
        internal void RefreshButtonStates(int rowCount)
        {
            if (rowButtons == null)
                return;
            foreach (var button in rowButtons)
            {
                if (button == null)
                    continue;
                button.Refresh(this, _rowIndex, rowCount, _editing);
            }
        }

        // The height this row occupies in the LinearLayout, in layout pixels (16 per world unit).
        //
        // Taken from the FRAME, not from the text. A row is as tall as the frame drawn around it —
        // that is what the player sees, and it is the same source the width and the click collider
        // already use. Measuring the text instead (16 × text height + padding) is what the screen
        // did when a row was nothing but text; it left the frame overhanging its own slot, which
        // went unnoticed between rows and clipped the first and last row against the viewport mask.
        //
        // Falls back to the text measurement when there is no frame to ask, which keeps a row
        // without frame references laid out rather than collapsed.
        public int RowHeightPx => fieldBorder != null ? ModSettingsScreen.FrameHeightPx(fieldBorder) : ModSettingsScreen.RowHeightPx(pugText);

        // Which drill-in session this row belongs to. A row does NOT receive this; it takes it from
        // the owner it is bound to, so a wrong generation is unrepresentable rather than merely
        // checked. Without it a row is a coordinate with no coordinate system: RowIndex says WHERE
        // in a list, never WHICH list, and this screen is a singleton reused for every setting.
        private int _generation = -1;
        public int Generation => _generation;

        // Index into ListDetailScreen's row list. Always valid for a bound row — the add button is
        // a different type entirely (ListAddRow), so -1 no longer means "this is the button" but
        // "this row was never bound", which is a fault worth a log line rather than a silent skip.
        // The screen writes this row's committed text back at this index; every OTHER row's text is
        // read from that list and never off the screen. That closed one half of the (now historical
        // — see below) width-trim hazard described at CommittedText below — the untouched
        // neighbours. CommittedText closes the other half, this row itself.
        private int _rowIndex = -1;
        public int RowIndex => _rowIndex;

        // The access level this row was bound at, already reduced by any permission lock (the screen
        // hands over SettingDef.EffectiveEditing, never the raw declaration). Kept alongside the
        // inherited readOnly rather than derived back out of it: readOnly answers "may this text be
        // typed into", which is true for both OrderOnly and ReadOnly, and the buttons need the two
        // told apart. Defaults to the most restrictive value, so a row that somehow renders before
        // Bind offers nothing rather than everything.
        private ListEditing _editing = ListEditing.ReadOnly;

        // The token this row was seeded with, and whether a keystroke has changed the text since.
        //
        // These existed because the base class used to SHORTEN the row's text behind our back:
        // RadicalMenuOptionTextInput.Update's `while (maxWidth > 0f && …)` trim (Pug.Other:343398)
        // fired on every active row, whether or not it was being edited, whenever maxWidth was
        // positive. It no longer applies: ListDetailItem.maxWidth is 0 since the field mask took
        // over defining the row's visible window (see UpdateClickCollider's comment below), and the
        // trim's own guard clause switches off cleanly at that value. The fields and the guard logic
        // below remain — as redundancy against a mechanism that no longer runs, not as an active
        // safeguard — because the reasoning still explains why they are shaped the way they are, and
        // dropping them would only save a few fields.
        //
        // Committing GetInputText() would have written that shortening into the owning mod's config
        // file — a value silently truncated by nothing more than looking at it. A text comparison
        // could not have caught this, because a trimmed value and a value the user backspaced look
        // identical.
        //
        // The TIMING told them apart: while this row holds activeInputField, a text change is the
        // user's; outside that window the only thing that could change the text was the trim.
        //
        // _textLastFrame is what made that EDGE-triggered rather than level-triggered, and it was
        // not redundant. Comparing the live text against _seededText instead would have misfired on
        // the very case this existed for: a token wider than the row was trimmed down over several
        // frames BEFORE anyone touched it, so on the first active frame the live text already
        // differed from the seed — the row would have been marked edited without a single keystroke,
        // and the trim would have landed in the config file after all.
        //
        // Seed only through SeedText, never through the inherited SetInputText: the latter leaves
        // _seededText behind and would reopen exactly that hole. (Shadowing SetInputText to redirect
        // it would be worse — the base class calls it while typing, which would clear _edited on
        // every keystroke.)
        private string _seededText = "";
        private string _textLastFrame = "";
        private bool _edited;

        /// <summary>Seeds the row's text and marks it unedited. Use instead of SetInputText, so the
        /// row remembers what the stored value actually said.</summary>
        public void SeedText(string token)
        {
            _seededText = token ?? "";
            _textLastFrame = _seededText;
            _edited = false;
            SetInputText(_seededText);
        }

        // Re-baseline the edit detector against what is actually on screen, after the base class
        // would have had a chance to trim it — moot since maxWidth: 0 (see the note above), so this
        // call is now redundancy rather than the load-bearing step it originally was. Called once per
        // row from RenderContent, i.e. after the layout pass that follows seeding.
        //
        // Without this, the first frame after seeding would have compared the (already trimmed) live
        // text against the untrimmed seed and set _edited from the trim alone. That was harmless only
        // because a row cannot own activeInputField on its creation frame — a fact that AddEmptyRow's
        // "selected but NOT activated" choice guarantees. Making the baseline explicit meant that
        // choice stayed a UX decision instead of quietly becoming load-bearing for data integrity —
        // moot today, kept for the same reason the fields above are kept.
        internal void RebaselineEditDetector() => _textLastFrame = GetInputText();

        /// <summary>What this row may contribute to the stored value: its own text once the user has
        /// typed, otherwise the token it was seeded with — never a shortening the user did not ask
        /// for.</summary>
        public string CommittedText => _edited ? GetInputText() : _seededText;

        // Sets this row's identity and behaviour in one call, right after Instantiate
        // (ListDetailScreen.AddItem) — the same commit-point every other field poke used to happen
        // at individually.
        //
        // readOnly is set here too but deliberately NOT declared as a field on this class:
        // RadicalMenuOptionTextInput (our base class) already has its own public bool readOnly — a
        // same-named field this class used to shadow (CS0108, present in every build log this session
        // and missed until a code review caught it). Shadowing meant any access through a
        // RadicalMenuOptionTextInput-typed reference (including CK's own internals, which read this
        // field on the base type) saw a permanently-false copy, independent of what this method set.
        // Writing the inherited field directly means CK's own read path and ours are guaranteed to
        // agree — true for a genuine read-only list (SettingDef.ReadOnly): view/scroll/navigate like
        // any other row, but OnActivated below never enters edit mode.
        //
        // ListAccess.CanType is the named question the inherited readOnly flag answers — an
        // OrderOnly row is as inert to the keyboard as a locked one, and differs only in the buttons
        // beside it, which is ListRowButton's decision to make. Asking it by name rather than
        // testing the level inline matters because three other decisions in this codebase are also
        // spelled `!= FreeText` today and are NOT this question.
        //
        // The level is kept alongside, because RefreshButtonStates has to pass it on and cannot
        // recover it from a boolean that has already collapsed two levels into one.
        public void Bind(ListDetailScreen owner, int rowIndex, ListEditing editing)
        {
            _owner = owner;
            _rowIndex = rowIndex;
            _generation = owner != null ? owner.RowGeneration : -1;
            _editing = editing;
            this.readOnly = !ListAccess.CanType(editing);
            // A fresh row has never been the active input field, whatever the GameObject did in a
            // previous life. Without this reset a row that still held activeInputField when the
            // screen closed keeps the latch set, and can fire one more commit while being torn down
            // — against the NEXT session's list. The generation check in OnRowTextCommitted is the
            // second lock on the same door.
            //
            // The text trio is reset here for the same reason and not because anything needs it
            // today: AddItem always calls SeedText straight after Bind, so these values never
            // actually survive. But this method's whole premise is "assume nothing about what this
            // object was before", and leaving three of its fields outside that promise is how the
            // premise quietly stops being true — which is exactly the shape of the stale-row bug
            // this same reset exists to prevent.
            _wasActiveField = false;
            _seededText = "";
            _textLastFrame = "";
            _edited = false;
            // A frame promises "you can type here", so only FreeText rows get one: at the other two
            // levels a row stays navigable for reading but can never become activeInputField
            // (OnActivated returns before base.OnActivated below) — the same line this class draws in
            // "no edit mode", and the screen draws in "no add button".
            //
            // The focus marker's renderer goes with it, which looks like it would leave a navigable
            // OrderOnly row with no visible selection. It does not: the base class recolours the
            // row's text on selection, so the selected entry is still evident. Verified in game
            // 2026-08-30 against the OrderOnly fixture, because reading the marker path alone
            // suggests the opposite.
            //
            // .enabled, never SetActive: the base class owns selectedMarker's active state and
            // toggles it on every select/deselect, so competing for that flag is a race it wins on
            // the next selection change. Switching the renderer instead leaves that mechanism
            // untouched and simply gives it nothing to draw.
            //
            // NOT a general rule, and ListAddRow deliberately does the opposite: it derives from
            // RadicalMenuOption, which has no selectedMarker, so it declares and drives its own with
            // SetActive. The rule is "do not fight the owner of the flag" — here the base class owns
            // it, there this mod does.
            if (fieldBorder != null)
                fieldBorder.enabled = !readOnly;
            var focus = selectedMarker != null ? selectedMarker.GetComponent<SpriteRenderer>() : null;
            if (focus != null)
                focus.enabled = !readOnly;

            // The row's own horizontal clip, fitted against the list's viewport mask every frame
            // (Update -> _viewport.Tick) so it scrolls out cleanly instead of clipping past the list
            // edge. screenMask is looked up by name because ListDetailScreen owns it, not this row.
            //
            // Both references are logged loudly when missing rather than left to degrade silently.
            // An unwired fieldMask leaves _viewport unbound, and an unbound viewport has no text to
            // measure a caret index against — so it reports no index at all and every caret-derived
            // feature falls back (typing appends at the row's end, word jumps and click-to-place
            // do nothing; see TextFieldViewport.IndexSpaceIsSound). The row still edits and still
            // stores what was typed, which is why the fault has to be logged to be noticed. An
            // unwired/renamed ViewportMask leaves screenMask null, so
            // TextFieldViewport.FitMaskToViewport returns early every frame and the row's own mask
            // never re-fits — it keeps whatever clip the prefab authored, clipping outside the list
            // as it scrolls.
            var screenMask = _owner != null ? _owner.transform.Find("ViewportMask")?.GetComponent<SpriteMask>() : null;
            if (fieldMask == null)
            {
                Debug.LogWarning(
                    "[ModSettingsMenu] ListDetailItem.fieldMask is unwired on this row's prefab — the horizontal "
                        + "viewport never binds, so the row cannot scroll and typing appends at the end instead of at the caret."
                );
                return;
            }
            if (screenMask == null)
            {
                Debug.LogWarning(
                    "[ModSettingsMenu] ListDetailScreen has no 'ViewportMask' SpriteMask (renamed or removed?) — "
                        + "the row's field mask can't fit itself to the list viewport and will keep clipping outside it."
                );
            }
            _viewport.Bind(pugText, fieldMask, screenMask, characterMarkBlinker);
        }

        // ACTIVE only for a live (cloned, SetActive(true)) row — the inactive prefab template must
        // report INACTIVE, else RadicalMenu's includeInactive option scan navigates to it too (the
        // template is the list's last prefab sibling). Unchanged from the read-only version.
        public override OptionActiveState GetActiveStateInCurrentScene() => gameObject.activeSelf ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;

        // NOT called from here anymore — see Update() below. RadicalMenu.SelectOptionIndex calls
        // OnDeselected() on every navigation-away, INCLUDING mere mouse hover onto a different row
        // (CK's own UIMouse re-derives menu selection from a hover raycast every frame; hovering a
        // different RadicalMenuOption drives SelectOptionIndex exactly like arrow-key navigation
        // does). Committing here would end an active edit the instant the mouse passes over any
        // other row, even without a click.
        public override void OnDeselected(bool playEffect = true)
        {
            // Also suppress the visual deselect (base.OnDeselected hides selectedMarker) while
            // THIS row is still the active input field — that only happens on the hover-driven
            // call above, since a real "stop editing" (Enter/Escape/click-elsewhere) already
            // cleared activeInputField before RadicalMenu ever gets here. Keeps the row LOOKING
            // like the one being edited instead of flickering to "unselected" on every hover.
            if (Manager.input.activeInputField == (object)this)
                return;
            base.OnDeselected(playEffect);
        }

        // Mirrors OnDeselected above: suppress the hover-selected marker on THIS row while a
        // DIFFERENT row in this menu is the active input field, so hovering around while typing
        // elsewhere doesn't visually highlight whatever the mouse happens to pass over.
        public override void OnSelected()
        {
            if (Manager.input.activeInputField != null && Manager.input.activeInputField != (object)this)
                return;
            base.OnSelected();
        }

        // Distinguishes "actively editing" (SELECTED_VALUE_COLOR, vivid) from merely "selected/
        // navigated" (SELECTED_TEXT_COLOR, pale — the prefab's own isValueText = false default),
        // since our patches above now pin selectedIndex on this row throughout an edit, so a user
        // could no longer tell the two states apart just by looking.
        //
        // Flipping isValueText alone does not immediately recolor an already-selected row: the
        // color is only (re)applied at specific transitions (OnSelected/OnDeselected/the
        // unselected<->selected cooldown blend in PugTextEffectLateUpdate) — while IsSelected()
        // stays true every frame, which it now always does mid-edit, that method just runs the
        // glyph "dance" animation and returns without touching color at all. But EVERY keystroke
        // DOES pass back through one such transition: AppendString/RemoveCharBehindMarker/etc. all
        // call pugText.Render(), which (unless dontResetEffectsOnRender) calls
        // PugTextEffectMenuOption.ResetEffect -> OnSelected() again, re-reading isValueText fresh
        // each time. A first attempt that only called pugText.SetTempColor(...) once here got
        // silently overwritten back to the prefab default on the very next keystroke for exactly
        // this reason. So the field flip IS the right mechanism here (not a redundant one) — it's
        // just insufficient by itself for the very first frame, which OnSelected() below covers
        // explicitly.
        //
        // Reverted at end-of-edit too, unconditionally, in Update() below — the transition it
        // detects (activeInputField leaving this row) is the mirror image of this one. An earlier
        // version of this comment judged the lingering tint "harmless" for a no-op commit or one of
        // OnRowTextCommitted's other early returns (no entry, stale generation, unbound index),
        // reasoning that it merely "marks the row you were last on" until the next rebuild. That
        // judgement held when this drill-in was rows only and a field was entered on purpose; per-
        // row buttons make the player move between field and buttons constantly, so a tint that
        // outlives its own edit no longer marks anything the player can act on — it just sits on a
        // row they already left. Treat the earlier reasoning as superseded by that change, not as
        // having been wrong for the drill-in it was written for. For an edit that CHANGES the
        // value, the revert in Update() is redundant rather than load-bearing: RebuildRows()
        // destroys this (flipped) instance anyway and creates a fresh one starting back at the
        // prefab's own isValueText = false — but redundant is the right place for it to be, given
        // the transition is unconditional and does not know in advance which case it is.
        // A row that cannot enter edit mode must not offer itself as activatable either: CK gates the
        // menu-select SFX and the footer's select prompt on this, so without it the player gets an
        // activation receipt for a press that returns immediately below. The rows stay navigable —
        // that is GetActiveStateInCurrentScene, a different question — so a read-only list can still
        // be read and scrolled; only the promise of "pressing this does something" is withdrawn.
        //
        // ListRowButton makes the same distinction for its disabled arrows, for the same reason.
        public override bool CanBeActivated() => base.CanBeActivated() && ListAccess.CanType(_editing);

        public override void OnActivated()
        {
            // A read-only list's rows are still navigable (GetActiveStateInCurrentScene stays
            // ACTIVE) so the player can view/scroll every token, but activating one must not enter
            // edit mode — base.OnActivated() is what calls Manager.input.SetActiveInputField(this);
            // skipping it here means activeInputField can never become a read-only row, so every
            // other guard in this class and MenuPatch's two Harmony prefixes (all keyed off
            // activeInputField) stay correctly inert without needing their own readOnly check.
            if (readOnly)
                return;
            base.OnActivated();
            // PugTextEffectMenuOption now lives on the "Text" child alongside the PugText it tints
            // (the ICL/SettingTemplate "Display" pattern) — GetComponentInChildren is safe here (unlike
            // for PugText itself) because this component type is unique in the row's hierarchy, so
            // there's no sibling-order ambiguity to worry about.
            var effect = GetComponentInChildren<PugTextEffectMenuOption>();
            if (effect != null)
            {
                effect.isValueText = true;
                effect.OnSelected();
            }
        }

        // Defense in depth: the actual click-away-during-edit fix lives in MenuPatch's Harmony
        // prefix on UIMouse.TrySelectNewElement (see there for why — the real mechanism is CK's
        // own code deactivating the old field BEFORE this ever runs, not this method itself). This
        // guard is what would run in an ordinary (non-hijacked) click while a different row is
        // still active, if that mechanism is ever bypassed some other way.
        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            if (Manager.input.activeInputField != null && Manager.input.activeInputField != (object)this)
                return;
            base.OnLeftClicked(mod1, mod2);

            // Guards a caret move against firing when there is no caret to move: a read-only row
            // never enters edit mode (OnActivated returns early for readOnly, above), so
            // activeInputField never becomes this row and base.OnLeftClicked was just a plain select.
            // On an ordinary row this check is ALREADY true on the very ACTIVATING click, not only on
            // later ones — OnActivated runs synchronously inside base.OnLeftClicked and calls
            // SetActiveInputField(this) before this line ever runs (Pug.Other:343502-343511) — so the
            // caret lands where you clicked from the first click onward, which is the intended UX.
            if (Manager.input.activeInputField == (object)this)
            {
                // Manager.ui.mouse.pointer.transform.position IS a world position — this is CK's own
                // way of reading the mouse for exactly this kind of comparison, not a workaround: CK's
                // own UIScrollWindow.IsMouseWithinScrollArea (Pug.Other:357471-357476) compares it
                // directly against a UI element's transform.position, and this screen implements the
                // very IScrollable interface that method serves. The pointer is set from
                // PugCamera.TransformMousePosition (PugRP.decompiled.cs:1854-1874), which removes
                // integer scaling and letterboxing and rounds pixel-perfect; Camera.ScreenToWorldPoint
                // does none of that. Under PugRP's scaled render target the two values diverge — worse
                // away from screen centre, and changing with window size — which used to place the
                // caret on the wrong character.
                float worldX = Manager.ui.mouse.pointer.transform.position.x;
                // Both halves of the move come out of the same recovered index space — where the
                // click landed, and where the caret already is — so one untrustworthy answer
                // disqualifies the whole move (TextFieldViewport.IndexSpaceIsSound explains when that
                // happens). Leaving the caret alone loses nothing: the click has already selected and
                // activated the row, which is the part that matters, and the only thing forgone is a
                // convenience. Moving on a bad index would instead put the caret on a character
                // nobody pointed at, and the player's next keystroke would land there.
                if (_viewport.TryCaretIndexFromLocalX(worldX - pugText.transform.position.x, out int target) && _viewport.TryCaretIndex(out int caret))
                    MoveCharMarker(target - caret);
            }
        }

        // A fixed row width instead of the base RadicalMenuOption behavior (text-rendered width
        // only): a text-width collider shrinks with every deleted character. If the mouse sits
        // stationary near the end of the text, backspacing can shrink the collider out from under
        // it, and CK's hover system reacts as if the mouse left the row entirely (see the Update()
        // note below). So the hit area must be an upper bound that does not move while typing.
        //
        // DERIVED from the frame, never copied: the frame is what the player sees and aims at, and
        // reading its size means an Editor resize needs no code change. This used to be a literal
        // matching the prefab's maxWidth, and that copy silently went stale the moment the frame and
        // maxWidth moved to a different width — the exact failure mode a derivation cannot have.
        //
        // The frame sprite's pivot is centred, so its localPosition IS the collider centre; the
        // fallback below derives from the field mask instead when a row has no frame reference. A
        // read-only row does NOT take either fallback path: its renderer is merely disabled, so size
        // and transform still read correctly.
        protected override void UpdateClickCollider()
        {
            base.UpdateClickCollider();
            if (clickCollider == null)
                return;
            // Width, x-centre AND height come from the frame — the height matters most and bites
            // hardest: the base sizes the collider from the rendered text, and PugText.Render
            // reports Rect.zero for an EMPTY string, so a blank row would get a zero-height box and
            // UIMouse's raycast could never hit it. That is precisely the row this screen now
            // creates on purpose (the add button appends one, and clearing an entry leaves one), so
            // without this the mouse cannot reach the very rows the feature exists for. Keyboard and
            // controller reach them regardless, which is why an in-game check that does not
            // deliberately click a blank row misses it.
            if (fieldBorder != null)
            {
                ModSettingsScreen.FitColliderToFrame(clickCollider, fieldBorder);
                return;
            }
            // No frame wired: fall back to the field mask's width, not maxWidth — maxWidth is 0 now
            // that the field mask defines the row's visible/typeable window (see the class-level note
            // above), so sizing from it here would collapse this fallback to a zero-width collider,
            // silently unhittable, the opposite of what this comment used to promise. The mask's own
            // transform gives both size and centre directly, mirroring FitColliderToFrame's
            // convention (its pivot is centred too, same as the frame's). Height stays whatever the
            // base measured.
            //
            // If fieldMask is unwired as well, Bind() has already logged that fault loudly (see
            // there) — this method does not repeat the warning, it just leaves the collider at
            // whatever the base class measured from rendered text rather than guessing further.
            if (fieldMask != null)
            {
                var size = clickCollider.size;
                var center = clickCollider.center;
                size.x = fieldMask.transform.localScale.x;
                center.x = fieldMask.transform.localPosition.x;
                clickCollider.size = size;
                clickCollider.center = center;
            }
        }

        // RadicalMenuOptionTextInput.Update() overrides RadicalMenuOption.Update() WITHOUT calling
        // base.Update() — so UpdateClickCollider() (which lives in RadicalMenuOption.Update() and
        // resizes the click collider to the row's actual rendered text bounds) never runs, leaving
        // every row stuck at Unity's default freshly-added BoxCollider size (1x1x1, centered at
        // origin) regardless of content. Restore it explicitly. (Root-caused via Debug.Log
        // instrumentation showing clickCollider.size/center never changing across 20+ rows of
        // varying text length, while pugText.dimensions correctly reflected each row's real width.)
        //
        // Manager.input.activeInputField and RadicalMenu.selectedIndex are two INDEPENDENT pieces
        // of state. Only Deactivate() (fired from Enter/Escape while typing) or a DIFFERENT row's
        // OnActivated() (fired from an explicit click, which calls Manager.input.
        // SetActiveInputField on itself) ever actually clear activeInputField away from this row;
        // mere mouse hover elsewhere only moves selectedIndex (via OnDeselected above, deliberately
        // left inert) and never touches activeInputField. So "activeInputField just stopped being
        // this row" is the one reliable "the user is genuinely done editing" signal — commit here,
        // once, on that transition, instead of on every hover-driven OnDeselected.
        private bool _wasActiveField;

        // Keeps this row's field mask inside the list viewport as it scrolls. See TextFieldViewport
        // for why this is a standalone helper rather than logic inlined here.
        private readonly TextFieldViewport _viewport = new TextFieldViewport();

        // Exposes the viewport's caret-index recovery to MenuPatch's AppendString prefix, which
        // needs to insert at the caret rather than always at the text end. internal, not public: the
        // only consumer outside this class lives in this same mod assembly.
        internal TextFieldViewport Viewport => _viewport;

        protected override void Update()
        {
            base.Update();
            UpdateClickCollider();

            // Computed before Tick, not after: the viewport needs to know whether THIS row is the
            // one being edited to decide between following the caret and resting at the text start
            // (see TextFieldViewport.ApplyOffset) — an untouched row's own currentCharIndex sits at
            // its text's end (SetInputText's doing), so without this an unfocused row would scroll
            // to its end instead of showing from the beginning.
            bool isActiveField = Manager.input.activeInputField == (object)this;
            _viewport.Tick(isActiveField);

            // Distinguish the user's edits from the base class's width trim — no longer applies since
            // maxWidth: 0 (see the class-level note near _seededText above), so this distinction is
            // now redundancy rather than the load-bearing check it originally was. Kept for the
            // reasoning: a change WHILE this row holds the input field can only come from a keystroke;
            // the trim, when it ran, also ran outside that window, and the moment it mattered (a value
            // too wide, shortened before anyone touched the row) was precisely outside it. The trim
            // could not masquerade as typing either: once it had cut the text down to maxWidth it
            // stopped, so it produced no further changes during an edit.
            string now = GetInputText();
            // `|| _wasActiveField` covers the on-screen keyboard, and without it the drill-in is
            // read-only on a controller while looking editable. CK's OSK result handler
            // (UIManager.TrySetInputText) does SetInputText(result) and Deactivate(success) in ONE
            // synchronous callback, so there is no frame in which the text has changed AND this row
            // still owns activeInputField: while the keyboard is open the text does not move, and by
            // the frame it does, ownership is already gone. Checking the PREVIOUS frame's ownership
            // catches exactly that landing frame.
            //
            // It would not have readmitted the width trim either, back when maxWidth was nonzero: an
            // untouched row has both flags false, so even a live trim could not have marked a row
            // edited. It could not have been intercepted from this class instead, either — CK
            // calls SetInputText and Deactivate through InputManager.TextInputInterface, and
            // RadicalMenuOptionTextInput implements both non-virtually, so a shadowing member here
            // would never be dispatched.
            //
            // Cancelling the keyboard is unaffected and was already correct: no SetInputText runs,
            // so the seeded value is what gets committed. That asymmetry is why a quick controller
            // test can look healthy while every CONFIRMED edit is being discarded.
            if ((isActiveField || _wasActiveField) && now != _textLastFrame)
                _edited = true;
            _textLastFrame = now;

            if (_wasActiveField && !isActiveField)
            {
                // Revert OnActivated's flip HERE, unconditionally, rather than inside
                // OnRowTextCommitted — that method has several early returns (no entry, stale
                // generation, unbound index, and a genuine no-op commit), and every one of them is
                // a way an edit can end, so the tint has to clear in all of them, not only the ones
                // that persist a change. Mirrors the set exactly, for the identical reason: flipping
                // isValueText alone does not repaint an already-selected row, since the colour is
                // only (re)applied at specific transitions (see OnActivated's own comment) — this
                // transition (activeInputField just left this row, which is still selected the
                // whole time per the comment above) is one of them, but only because OnSelected()
                // is called explicitly; the flag flip by itself would sit there unapplied until the
                // next such transition happened to come along.
                var effect = GetComponentInChildren<PugTextEffectMenuOption>();
                if (effect != null)
                {
                    effect.isValueText = false;
                    effect.OnSelected();
                }
                _owner?.OnRowTextCommitted(this);
            }
            _wasActiveField = isActiveField;
        }
    }
}
