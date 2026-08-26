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
        // read from that list and never off the screen. That closes one half of the width-trim
        // hazard described at CommittedText below — the untouched neighbours. CommittedText closes
        // the other half, this row itself.
        private int _rowIndex = -1;
        public int RowIndex => _rowIndex;

        // The token this row was seeded with, and whether a keystroke has changed the text since.
        //
        // These exist because the base class SHORTENS the row's text behind our back:
        // RadicalMenuOptionTextInput.Update trims anything wider than maxWidth, on every active row,
        // whether or not it is being edited. Committing GetInputText() would therefore write that
        // shortening into the owning mod's config file — a value silently truncated by nothing more
        // than looking at it. A text comparison cannot catch this, because a trimmed value and a
        // value the user backspaced look identical.
        //
        // The TIMING can tell them apart: while this row holds activeInputField, a text change is
        // the user's; outside that window the only thing that changes the text is the trim.
        //
        // _textLastFrame is what makes that EDGE-triggered rather than level-triggered, and it is
        // not redundant. Comparing the live text against _seededText instead would misfire on the
        // very case this exists for: a token wider than the row is trimmed down over several frames
        // BEFORE anyone touches it, so on the first active frame the live text already differs from
        // the seed — the row would be marked edited without a single keystroke, and the trim would
        // land in the config file after all.
        //
        // Seed only through SeedText, never through the inherited SetInputText: the latter leaves
        // _seededText behind and reopens exactly that hole. (Shadowing SetInputText to redirect it
        // would be worse — the base class calls it while typing, which would clear _edited on every
        // keystroke.)
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

        // Re-baseline the edit detector against what is actually on screen, after the base class has
        // had a chance to trim it. Called once per row from RenderContent, i.e. after the layout
        // pass that follows seeding.
        //
        // Without this, the first frame after seeding compares the (already trimmed) live text
        // against the untrimmed seed and would set _edited from the trim alone. That is harmless
        // only because a row cannot own activeInputField on its creation frame — a fact that
        // AddEmptyRow's "selected but NOT activated" choice currently guarantees. Making the
        // baseline explicit means that choice stays a UX decision instead of quietly becoming
        // load-bearing for data integrity.
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
        public void Bind(ListDetailScreen owner, int rowIndex, bool readOnly)
        {
            _owner = owner;
            _rowIndex = rowIndex;
            _generation = owner != null ? owner.RowGeneration : -1;
            this.readOnly = readOnly;
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
            // A frame promises "you can type here". A read-only list's rows stay navigable for
            // reading but can never become activeInputField (OnActivated returns before
            // base.OnActivated below), so they get no frame — the same line this class draws in
            // "no edit mode", and the screen draws in "no add button".
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
            // An unwired fieldMask leaves _viewport unbound, so CaretIndexFromLocalX (which needs
            // _text) keeps returning 0 — every keystroke then inserts at the front instead of the
            // caret, writing text backwards into a foreign mod's config. An unwired/renamed
            // ViewportMask leaves screenMask null, so TextFieldViewport.FitMaskToViewport returns
            // early every frame and the row's own mask never re-fits — it keeps whatever clip the
            // prefab authored, clipping outside the list as it scrolls.
            var screenMask = _owner != null ? _owner.transform.Find("ViewportMask")?.GetComponent<SpriteMask>() : null;
            if (fieldMask == null)
            {
                Debug.LogWarning(
                    "[ModSettingsMenu] ListDetailItem.fieldMask is unwired on this row's prefab — the horizontal "
                        + "viewport never binds, so typing inserts at index 0 instead of the caret."
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
        // explicitly. No revert on end-of-edit is needed for any edit that CHANGES the value: it
        // triggers ListDetailScreen.RebuildRows(), which destroys this (flipped) instance and
        // creates a fresh one starting back at the prefab's own isValueText = false.
        //
        // A no-op commit is the expected exception, and it is deliberate rather than overlooked:
        // activating a row and leaving it untouched returns early in OnRowTextCommitted, no rebuild
        // follows, and the row keeps the vivid editing tint until the next real rebuild. Harmless —
        // it marks the row you were last on. The same is true of that method's other early returns
        // (no entry, stale generation, unbound index), but those are logged fault paths rather than
        // ordinary use. Either way, do not read the sentence above as "always".
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
                int target = _viewport.CaretIndexFromLocalX(worldX - pugText.transform.position.x);
                MoveCharMarker(target - _viewport.CaretIndex);
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

            // Distinguish the user's edits from the base class's width trim, which is the whole
            // point of CommittedText above. A change WHILE this row holds the input field can only
            // come from a keystroke; the trim also runs outside that window, and the moment it
            // matters (a value too wide, shortened before anyone touched the row) is precisely
            // outside it. Note the trim cannot masquerade as typing either: once it has cut the
            // text down to maxWidth it stops, so it produces no further changes during an edit.
            string now = GetInputText();
            // `|| _wasActiveField` covers the on-screen keyboard, and without it the drill-in is
            // read-only on a controller while looking editable. CK's OSK result handler
            // (UIManager.TrySetInputText) does SetInputText(result) and Deactivate(success) in ONE
            // synchronous callback, so there is no frame in which the text has changed AND this row
            // still owns activeInputField: while the keyboard is open the text does not move, and by
            // the frame it does, ownership is already gone. Checking the PREVIOUS frame's ownership
            // catches exactly that landing frame.
            //
            // It does not readmit the width trim: an untouched row has both flags false, so the trim
            // still cannot mark a row edited. And it cannot be fixed on this class instead — CK
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
                _owner?.OnRowTextCommitted(this);
            _wasActiveField = isActiveField;
        }
    }
}
