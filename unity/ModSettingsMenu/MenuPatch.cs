using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModSettingsMenu
{
    /// <summary>
    /// Mounts the "Mod settings" screen into the vanilla Options menu (GMCM
    /// MenuPatch technique + HealthBars menu clone): a MenuManager.Init prefix
    /// clones the "Go to UI settings" entry and repoints it at our menu id, the
    /// postfix instantiates our screen prefab(s) (the settings screen + the list
    /// drill-in), and a RadicalMenu.TypeToMenu prefix resolves our menu ids to
    /// those instances. Harmony patch classes are auto-discovered (no PatchAll()).
    /// </summary>
    [HarmonyPatch]
    public static class MenuPatch
    {
        internal static ModSettingsMenu.UI.ModSettingsScreen MenuInstance { get; private set; }
        internal static ModSettingsMenu.UI.ListDetailScreen ListDetailInstance { get; private set; }

        // Set the Options-menu entry label to our localised title. Use SetText (which only sets
        // textString), NOT Render: the vanilla prefab entries are unrendered templates (0 glyphs)
        // that the LIVE menu renders on activate. Rendering here builds glyphs into the shared
        // optionsMenuPrefab that InstantiateMenu then clones as ORPHANED (untracked) SpriteRenderers
        // — a frozen, never-cleared duplicate label (the red twin). SetText leaves the prefab entry a
        // clean template; the live instance renders our term fresh, relocalizes (localize=true is
        // inherited), and its PugTextEffectMenuOption drives the colour — exactly like every sibling.
        private static void SetEntryLabel(PugText text)
        {
            if (text == null)
                return;
            text.SetText("ModSettingsMenu-UI/Title");
        }

        // Add a "Mod settings" entry to the Options menu by cloning the vanilla
        // "Go to UI settings" push-menu entry and repointing it at our menu id.
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Init)), HarmonyPrefix]
        public static void MenuManager_PreInit(MenuManager __instance)
        {
            var optionsPrefab = __instance.optionsMenuPrefab;
            var pushOptions = optionsPrefab.GetComponentsInChildren<RadicalOptionsMenuOption_PushMenu>();
            var uiEntry = Array.Find(pushOptions, x => x.menuToPush == RadicalMenu.MenuType.UI_OPTIONS);
            if (uiEntry == null)
            {
                Debug.Log("[ModSettingsMenu] UI_OPTIONS entry not found; cannot add menu entry.");
                return;
            }

            // Clone parentless, THEN SetParent. Instantiate(gameObject, parent)
            // activates the clone mid-clone (Internal_CloneSingleWithParent) and
            // fires OnEnable/ResetEffect before the PugText's text component is
            // cloned → NRE. A parentless clone finishes fully first; SetParent
            // then activates it cleanly.
            var entry = Object.Instantiate(uiEntry.transform);
            entry.SetParent(uiEntry.transform.parent);
            entry.SetSiblingIndex(uiEntry.transform.GetSiblingIndex() + 1);
            entry.name = "GoToModSettings";
            // Menu-entry label uses the vanilla unselected colour (grey, alpha 0.725);
            // the option's own PugTextEffectMenuOption drives the hover/selected colour.
            SetEntryLabel(entry.gameObject.GetComponentInChildren<PugText>());
            entry.GetComponent<RadicalOptionsMenuOption_PushMenu>().menuToPush = ModSettingsMenuMod.SettingsMenuType;
        }

        // Instantiate our own menu prefab + populate it from the registry.
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Init)), HarmonyPostfix]
        public static void MenuManager_PostInit()
        {
            var prefab = ModSettingsMenuMod.MenuPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[ModSettingsMenu] MenuPrefab not loaded; Mod settings entry will have no menu.");
                MenuInstance = null;
                return;
            }
            var menu = Object.Instantiate(prefab, Manager.camera.uiCamera.transform).GetComponent<ModSettingsMenu.UI.ModSettingsScreen>();
            menu.gameObject.SetActive(false);
            MenuInstance = menu;

            var detailPrefab = ModSettingsMenuMod.ListDetailPrefab;
            if (detailPrefab == null)
            {
                Debug.LogWarning("[ModSettingsMenu] ListDetailPrefab not loaded; list rows cannot drill in.");
                return;
            }
            var detail = Object.Instantiate(detailPrefab, Manager.camera.uiCamera.transform).GetComponent<ModSettingsMenu.UI.ListDetailScreen>();
            detail.gameObject.SetActive(false);
            ListDetailInstance = detail;
        }

        // Resolve our menu id to the cloned menu.
        [HarmonyPatch(typeof(RadicalMenu), nameof(RadicalMenu.TypeToMenu)), HarmonyPrefix]
        public static bool RadicalMenu_TypeToMenu(RadicalMenu.MenuType type, ref RadicalMenu __result)
        {
            if (type == ModSettingsMenuMod.SettingsMenuType)
            {
                __result = MenuInstance;
                return false;
            }
            if (type == ModSettingsMenuMod.ListDetailMenuType)
            {
                __result = ListDetailInstance;
                return false;
            }
            return true;
        }

        // Populate now runs in ModSettingsScreen.Activate() (before ActivateTopMenu),
        // so no PushMenu postfix is needed here.

        // Suppress a reselection that would land on another option while a list-detail row is being
        // actively edited. NOT the mouse path, despite appearances: on hover the same element
        // travels TrySelectNewElement -> Select() -> OnUIElementSelected -> SelectOption unchanged,
        // and the prefix below tests the identical predicate on it first, so by the time this one
        // runs there is nothing left for it to block. (It still runs — Harmony calls it either way.)
        // That "unchanged" is a property of a MENU OPTION, not of the mouse path:
        // OnUIElementSelected forwards to SelectOption only when the incoming element reports
        // isMenuOption (Pug.Other:273427); a plain ButtonUIElement takes its own OnSelected instead,
        // which forwards to SelectOption on a DIFFERENT element whenever optionToSelectOnHover is
        // set (:335006-:335009). Both halves hold on these screens — every row is a
        // RadicalMenuOption (isMenuOption => true, :343070), and the one ButtonUIElement in each
        // prefab, the scrollbar handle, leaves optionToSelectOnHover at {fileID: 0} — so re-check
        // them after a prefab edit rather than assuming the identity.
        // What it does catch is a path that never touches TrySelectNewElement at all —
        // UIScrollWindow.UpdateScroll calls Select() directly on the adjacent element when the
        // selected one scrolls out of view (Pug.Other:357556), behind an early return that makes
        // the whole block controller-only (:357529). (:357562 calls it again on what :357557 just
        // reassigned, so Select()'s own guard makes that one a no-op.)
        // Scroll a drill-in with a gamepad mid-edit and this is the only guard there. Note what it
        // does NOT do: Select() still runs through OnUIElementSelected, which assigns
        // currentSelectedUIElement unconditionally, so the selection does move off the edited row.
        // Only selectedIndex is held. The edit survives because nothing on this path calls
        // Deactivate at all: the call that would end it here lives in TrySelectNewElement, which
        // this path never enters — and which gates it on interactDownThisFrame besides (:356112).
        //
        // Letting it through would move RadicalMenu.selectedIndex
        // to whatever was selected instead, which (a) plays the menu-select SFX and (b) recolours
        // both rows. The recolour runs through the selection callbacks, NOT through a per-frame
        // check: RadicalMenuOption.OnSelected walks menuOptionEffects into
        // PugTextEffectMenuOption.OnSelected (Pug.Other:343240-343244 -> :349591), which stops the
        // two cooloff timers and sets the selected colour on the text and on the effect's
        // spriteRenderers (:349593-349599), while the row losing selection goes through
        // OnDeselected/EndEffectImmediate (:343262-343269). PugTextEffectLateUpdate is not that
        // path — for a selected option it does the optional dance and returns (:349638).
        // Skipping SelectOptionIndex leaves the index where it was, so the edited row keeps
        // looking selected and nothing reacts to a change that did not happen.
        // Mouse hover and a click on another row are both the prefix below's business — see its
        // note; the two patches divide by PATH, not by input device.
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.SelectOption)), HarmonyPrefix]
        public static bool MenuManager_SelectOption(UIelement option)
        {
            if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem active && (object)option != active)
                return false;
            return true;
        }

        // The actual "click on a different row while editing switches focus there" bug lives here,
        // NOT in RadicalMenuOption's OnLeftClicked/OnActivated chain (ListDetailItem's own guards on
        // those are insufficient — see below). UIMouse.TrySelectNewElement is reached on every
        // opening of CK's hover gate, which covers BOTH plain hover and the click itself —
        // movement and the interact press are two of its six conditions, and the other four
        // (nothing selected, the selection no longer visible, no visible UIelement under the ray,
        // a controller-preferring system) reach it just as well while a row is being edited. It
        // contains its own hardcoded
        // deactivation:
        //
        //   if ((UIelement)Manager.input.activeInputField != selectedUIElement
        //       && Manager.input.textInputIsActive && interactDownThisFrame)
        //   {
        //       Manager.input.activeInputField.Deactivate(commit: false);
        //   }
        //
        // This runs BEFORE the click is ever delivered to the clicked row's OnLeftClicked — by the
        // time OnLeftClicked's own activeInputField-is-null-or-mismatched guard runs, CK has already
        // cleared activeInputField to null itself, so that guard always sees "nothing is active" and
        // lets the click through (root-caused via Debug.Log instrumentation on OnLeftClicked/
        // OnActivated: activeInputField was reliably already null on every click, before our code
        // ever ran). Blocking downstream (OnLeftClicked) is structurally too late; this must be
        // blocked at the source. Skip the WHOLE method (not just the Deactivate line) so
        // RadicalMenu.selectedIndex and Manager.ui.currentSelectedUIElement are also left untouched —
        // letting DeselectAnySelectedUIElement or Select() run partially would set selectedIndex to
        // -1, which desyncs from activeInputField and would make PugTextEffectMenuOption.
        // IsSelected() (keyed off selectedIndex, not activeInputField) wrongly grey out the row still
        // being edited.
        //
        // Deliberately NOT scoped to "selectedUIElement is also a ListDetailItem": moving the mouse
        // to EMPTY space (selectedUIElement == null) or onto a non-option element (e.g. the
        // scrollbar) hits this same method with selectedUIElement != currentSelectedUIElement, which
        // unconditionally runs Manager.ui.DeselectAnySelectedUIElement() -> RadicalMenu.
        // DeselectAnyCurrentOption(), setting selectedIndex = -1 regardless of what ListDetailItem.
        // OnDeselected does — reproduced: the edited row's text stayed blue only while the mouse
        // stayed over ITS row, and turned grey (PugTextEffectMenuOption's UNSELECTED_TEXT_COLOR) the
        // instant the mouse moved anywhere else, precisely because selectedIndex fell to -1 there and
        // was never restored except by re-hovering the same row. Blocking on "target != the row being
        // edited" (any target, including null) keeps selectedIndex pinned on the edited row regardless
        // of where the mouse wanders while typing.
        [HarmonyPatch(typeof(UIMouse), "TrySelectNewElement"), HarmonyPrefix]
        public static bool UIMouse_TrySelectNewElement(UIelement selectedUIElement)
        {
            if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem active && (object)selectedUIElement != active)
                return false;
            return true;
        }

        // Commit a drill-in row BEFORE CK blanks it on a world event, and disarm the blanking.
        //
        // UIManager.TryHideAllInventoryAndCraftingUI ends with, guarded by textInputIsActive:
        //     Manager.input.activeInputField.SetInputText("");
        //     Manager.input.activeInputField.Deactivate(commit: false);
        // Its callers are world events, not menu actions — opening a chest, a cattle pen, a vending
        // machine, a crafting station, a sign, the map, and PlayerController.FadeOutAndLockPlayer.
        // In multiplayer the simulation keeps running while a player sits in the options menu, so
        // another player or a mob can trigger it mid-edit.
        //
        // Why this has to be a patch rather than a rule in ListDetailItem: that sequence is
        // BYTE-FOR-BYTE the shape of the on-screen keyboard's own result handler
        // (UIManager.TrySetInputText: SetInputText(result) then Deactivate(success), one callback,
        // no frame between). The row's edit detector must treat that shape as a genuine edit or
        // every controller entry is silently discarded — and must NOT treat this one as an edit or
        // the entry is silently deleted. No timing rule can separate them; only the source can, and
        // only from here.
        //
        // Committing first also clears activeInputField, so CK's own `if (textInputIsActive)`
        // (textInputIsActive => activeInputField != null) finds nothing and the blanking never runs.
        // The user's edit is preserved rather than merely not-destroyed.
        //
        // 1.3 renamed this to TryHideAllInventoryAndCraftingUI and gave it a bool return, along
        // with an early exit that did not exist before: it hides nothing and returns false when a
        // QuickTrash or Locking mouse mode was toggled and forceClose is false. A Harmony prefix
        // runs regardless, so in that one case the row commits while CK keeps the menu open.
        [HarmonyPatch(typeof(UIManager), nameof(UIManager.TryHideAllInventoryAndCraftingUI)), HarmonyPrefix]
        public static void UIManager_TryHideAllInventoryAndCraftingUI()
        {
            if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem row && row.Owner != null)
            {
                row.Deactivate(commit: false); // clears activeInputField; does not touch the text
                row.Owner.OnRowTextCommitted(row);
            }
        }

        // RadicalMenuOptionTextInput enforces maxWidth in two places, and only one is gated on it.
        // Update's per-frame trim (Pug.Other:343398) is `while (maxWidth > 0f && …)` and switches off
        // cleanly at 0. AppendString's reject (Pug.Other:343446) is a bare
        // `if (pugText.dimensions.width > maxWidth)` — at maxWidth 0 that is true for every non-empty
        // string. Our drill-in rows run at maxWidth 0 on purpose (the field is meant to hold more text
        // than it shows, not less), which without this prefix means every keystroke gets appended,
        // found "too wide", and rolled back — the field refuses all input.
        //
        // Only ListDetailItem rows are redirected; every other text field in the game (character name,
        // chat, …) keeps vanilla's width-capped behaviour untouched.
        [HarmonyPatch(typeof(RadicalMenuOptionTextInput), nameof(RadicalMenuOptionTextInput.AppendString)), HarmonyPrefix]
        public static bool RadicalMenuOptionTextInput_AppendString(RadicalMenuOptionTextInput __instance, string s)
        {
            if (__instance is not ModSettingsMenu.UI.ListDetailItem row)
                return true;

            // Same filtering the base class does — trim, newline strip, whitelist (Pug.Other:343406-
            // 343429) — minus the width rejection, which is the whole reason this prefix exists (see
            // the note above). Replicated rather than assumed away: our rows ship an empty
            // characterWhiteList today (prefab-verified), but that is an unenforced assumption a
            // future row template could break silently.
            //
            // The row prefab ships trim: 0, and that is deliberate — do NOT turn it back on in the
            // Editor. A lone typed space arrives as the WHOLE of s, so `s.Trim()` empties it and the
            // keystroke vanishes; with trim on, a list entry cannot be given a space at all, and the
            // word navigation has nothing to navigate except in tokens a foreign config already
            // contained. (s is not always one character — Input.inputString batches a frame's worth
            // and the paste branch hands over the clipboard entire, which is what the 255-char cap
            // below is for. Trim would eat the outer spaces of a paste too.) What trim was wanted
            // for — no leading or trailing space on a stored token — happens at commit instead, in
            // ListTokenizer.Sanitize, which is also where the separator is stripped. One rule,
            // applied once, at the moment the token is written rather than on every keystroke. The
            // branch stays because a future row template may legitimately want it and because this
            // prefix mirrors the base class.
            if (__instance.trim)
                s = s.Trim();
            for (int i = s.Length - 1; i >= 0; i--)
            {
                if (__instance.dontAllowNewLines && (s[i] == '\n' || s[i] == '\r'))
                {
                    s = s.Remove(i, 1);
                    continue;
                }
                int j;
                for (
                    j = 0;
                    j < __instance.characterWhiteList.Length
                        && (__instance.ignoreCapitalizationInWhiteList || s[i] != __instance.characterWhiteList[j])
                        && (
                            !__instance.ignoreCapitalizationInWhiteList || !(s[i].ToString().ToLower() == __instance.characterWhiteList[j].ToString().ToLower())
                        );
                    j++
                ) { }
                if (__instance.characterWhiteList.Length > 0 && j == __instance.characterWhiteList.Length)
                    s = s.Remove(i, 1);
            }

            // Vanilla clears this unconditionally at the end of AppendString (Pug.Other:343452),
            // including when filtering left nothing to insert — clear it here too, before the early
            // return below, so a filtered-to-nothing keystroke doesn't leave it stuck set.
            __instance.WasAutoActivated = false;
            if (string.IsNullOrEmpty(s))
                return false;

            var text = row.pugText;
            string current = text.GetText();

            // Cap the TOTAL length against MaxCharactersForOnScreenKeyboard (255, serialized on the
            // row's prefab) — the number is not invented here, it is the same field the on-screen-
            // keyboard path already enforces (Manager.platform.platformImpl.GetControllerTextInput
            // is handed this exact property, Pug.Other:269617). Vanilla's own width rejection is what
            // this whole prefix removed (see the note above AppendString), and removing it without
            // replacing it left the keyboard path uncapped while the OSK path stayed capped at 255 —
            // an accidental Ctrl+V then writes an unbounded paste whole into a foreign mod's
            // config.cfg. Truncate the APPENDED string to what still fits, rather than rejecting the
            // whole keystroke: a silent full-width rejection is the exact failure this prefix exists
            // to eliminate (vanilla's own `if (dimensions.width > maxWidth)` rollback, Pug.Other:
            // 343446), and it would be one for a paste too.
            int room = Mathf.Max(0, __instance.MaxCharactersForOnScreenKeyboard - current.Length);
            if (s.Length > room)
                s = s.Substring(0, room);
            if (string.IsNullOrEmpty(s))
                return false;

            // Insert at the caret — which is what vanilla AppendString already does
            // (Pug.Other:343442). This prefix replaced the whole method in order to drop its width
            // rejection, so it has to carry the insertion point over too; an earlier draft appended
            // at the end and was wrong for exactly that reason. It reads the very counter vanilla
            // inserts at, through API.Reflection (TextFieldViewport.TryCaretIndex), so nothing is
            // approximated here any more: this used to reconstruct the index from the caret's
            // on-screen POSITION, which was exact only while PugText's glyph count and the string's
            // character count agreed, and every glyphless character broke that.
            //
            // When the counter cannot be read at all — an unbound viewport, or a Core Keeper update
            // that renames or reshapes the field — append at the END. That is not a made-up
            // fallback: it is the shape
            // vanilla's own AppendString takes whenever the caret sits at the end
            // (Pug.Other:343436-343438), and it is the only insertion point that can never REORDER
            // what is already there. Nothing is dropped on either path; only where the typed text
            // lands differs, and that the player can see.
            //
            // The 255-cap above applies to both paths on purpose — it is measured against the total
            // length, which is unaffected by WHERE the text goes in.
            //
            // The clamp bounds a TRUSTED value, which is the distinction that used to argue against
            // having one here: while `at` came from a reconstruction, clamping was the very move
            // that turned "no answer" into "the front of the string". vanilla guards the same
            // overrun on its own way in (Pug.Other:343431-343434 — LogError, then correct), so the
            // state is one to survive rather than to rule out: currentCharIndex tracks the text only
            // as long as everything that writes that text also moves the marker. Unclamped, it is an
            // ArgumentOutOfRangeException out of a Harmony prefix, for as long as that state holds.
            // Only the upper bound defends anything — MoveCharMarker clamps at 0 (Pug.Other:343458)
            // and Update's unclamped decrement is gated on maxWidth > 0, which these rows set to 0 —
            // and the 0 is there because Mathf.Clamp takes two bounds, not because it is reachable.
            //
            // It also warns, because taking over vanilla's correction meant taking over its report:
            // this prefix returns false, so vanilla's LogError above never runs again for these rows,
            // and a silent clamp would leave a recurring caret/text desync looking exactly like a
            // one-off mis-click while the value goes into a foreign mod's config file.
            //
            // MoveCharMarker is relative and clamped (Pug.Other:343455-343458), so the two paths need
            // different arguments. At the caret: the caret was at `at` and the text grew by s.Length
            // there, so +s.Length lands on the right side of what was typed. At the end: the caret's
            // index is precisely what could not be read, so a relative step cannot be aimed — a
            // full-length forward move lets vanilla's own clamp put the caret on the text end, which
            // is where the insertion happened. Same trick the Home/End handler below uses.
            bool atCaret = row.Viewport.TryCaretIndex(out int caret);
            if (atCaret && caret > current.Length)
                WarnCaretPastTextOnce(caret, current.Length);
            int at = atCaret ? Mathf.Clamp(caret, 0, current.Length) : current.Length;
            text.SetText(current.Insert(at, s));
            text.Render(rewindEffectAnims: false);
            row.MoveCharMarker(atCaret ? s.Length : text.GetTextLength());
            return false;
        }

        // Latched for the session, like TextFieldViewport's two: the state that trips this holds
        // across keystrokes, so an unlatched line would bury itself. No manual walk of the drill-in
        // can provoke it — every path that writes a row's text also moves the marker — which is
        // exactly why it has to say so when it happens rather than being left to inference.
        private static bool _warnedCaretPastText;

        private static void WarnCaretPastTextOnce(int caret, int textLength)
        {
            if (_warnedCaretPastText)
                return;
            _warnedCaretPastText = true;
            Debug.LogWarning(
                "[ModSettingsMenu] A row's caret counter reads "
                    + caret
                    + " for "
                    + textLength
                    + " characters of text — something changed the text without moving the marker. The keystroke was inserted at "
                    + "the end of the value instead of at the caret; nothing was lost. Logged once per session."
            );
        }

        // Vanilla's own verdict for this frame, per arrow key — set by the IsKeyDown postfix below
        // and consumed by the HandleTypingInput postfix further down.
        //
        // Two fields rather than one nullable KeyCode. Not because both arrows can be reported in
        // one frame — vanilla's chain short-circuits, so a true for Left means Right is never asked
        // about — but because nothing here depends on that staying true: a foreign patch that
        // reaches IsKeyDown by another route could set both, and two plain bools make that
        // representable instead of forcing a choice at the point where it is written. The consumer
        // resolves the pair the way the chain would, left first.
        private static bool _leftArrowFired;
        private static bool _rightArrowFired;

        // Whether Backspace or Delete answered true this frame. Vanilla's chain is an else-if
        // cascade and never asks about the arrows after those two (Pug.Other:269628, :269632), so an
        // arrow verdict beside such a true did not come from the game, and the postfix declines it.
        //
        // Strictly this reports a PROBE, not a branch — the postfix cannot tell whose call it
        // answers, the same limit the arrows have. A foreign probe could answer Backspace true,
        // restart the shared cooldown, and leave vanilla's own probe false so the cascade reaches
        // the arrows after all; the jump would then be declined although the game did handle it.
        // Not reachable through the one mod that probes here: its repeat-capable Backspace/Delete
        // probe sits behind a selection and CANCELS when it fires, so __runOriginal returns above
        // this, and its other probe is press-edge-only, where GetKeyDown is timer-independent and
        // vanilla answers alike. A mod probing repeat-capably without cancelling would break the
        // inference, at the cost of one declined jump recovered on the next tick.
        private static bool _editBranchClaimed;

        // Vanilla's typing repeat, OBSERVED rather than reconstructed.
        //
        // Every key MenuManager.HandleTypingInput handles shares ONE timer, not one per key:
        // typingInputCooldown (Pug.Other:269210). IsKeyDown (Pug.Other:269693-269702) reports a key
        // as down on GetKeyDown, or on GetKey once that timer has elapsed OR is not running, and
        // restarts it on every true — 0.3 s after a fresh press, 0.05 s after a repeat
        // (Pug.Other:269696-269698). So a held arrow moves vanilla's caret twenty times a second,
        // while a postfix keyed on GetKeyDown alone fires exactly once: one word jump, then a
        // character-by-character crawl at vanilla's own repeat rate. That crawl is what this exists
        // to prevent, and docs/manual-tests.md checks for it by name.
        //
        // This USED to reconstruct the decision from the timer, read through API.Reflection in a
        // prefix, because the postfix runs after vanilla has already called Start() and the field no
        // longer says whether it HAD elapsed. That worked and was verified in game. It is gone
        // because the verdict itself is patchable: IsKeyDown has a single declaration and, in the
        // whole assembly, six call sites on five lines — all of them inside HandleTypingInput's own
        // else-if chain (Pug.Other:269628, :269632, :269636 twice, :269659, :269663), the doubled
        // line being Return and KeypadEnter sharing one branch. So a postfix here receives the
        // answer vanilla acts on, per key, and the member lookup, its warning latch, the
        // reconstructed predicate and the boxed-copy read all retire with it.
        //
        // Whether the JIT would inline a private method this small was the one thing reading could
        // not settle, and inlining is a known Harmony pitfall in this project
        // (docs/ck/world-and-mechanics.md records it for PetExtensions). Measured instead, in game:
        // the postfix fires, first observed call `Backspace -> False`. It is not inlined.
        //
        // The chain is why this is more precise than the timer was, and the improvement is
        // structural rather than a better condition. The timer says only that SOME key may repeat
        // this frame, so a Backspace auto-repeating alongside a held arrow armed a jump in a frame
        // where vanilla took its Backspace branch and moved no caret. Here that frame produces no
        // arrow verdict at all — the chain short-circuits after Backspace and never asks about the
        // arrows.
        //
        // That holds for the game's own calls, not for every call there could be. THIS IS THE ONE
        // PLACE that describes the mod which makes the difference; the guards in the postfix carry a
        // line each and point back here. Keeping the model in three places is what let three
        // descriptions of it drift apart and contradict each other.
        //
        // BetterTextInput ships an accessor assembly and calls MenuManager.IsKeyDown from its OWN
        // HandleTypingInput prefix, ahead of the chain that would have short-circuited. A postfix
        // cannot tell whose call it answers. Three properties of those calls decide what that costs:
        //
        //   1. The arrow probes are UNGATED — a || chain at the top of its HandleSpecialKeys with no
        //      modifier test at all. Its own word jump is gated on LeftControl much further down, so
        //      a probe fires in frames where that mod then does nothing. That word jump is itself
        //      press-edge-only — its probe passes the same flag — so on a repeat tick it does
        //      nothing, and this mod's jump is the only thing between a held key and vanilla's
        //      character-by-character crawl. Every guard below lets those ticks through.
        //   2. They pass checkOnlyOnPressedDown: true, dropping the GetKey-and-timer half of the
        //      condition (Pug.Other:269696) — the same opt-out Return and KeypadEnter use at
        //      :269636. So they answer on a press edge only: one stray verdict per fresh press, not
        //      the 20 Hz stream a held key produces.
        //   3. It probes BEFORE deciding whether to cancel, so a cancelled frame can still carry a
        //      formed verdict. It cancels for Home, End, Ctrl+A and the selection paths. Escape is
        //      the exception — probed first, returning before the arrows are reached.
        //
        // The clearing prefix below takes Priority.First, which also pins it ahead of those probes,
        // so a stray verdict is never erased and always reaches the postfix. Two guards there bound
        // what it can do, and they cover different frames: on a LeftControl frame that mod let
        // through it has also moved a word, so the caret-distance gate declines; on a frame it
        // cancelled, the distance can be zero — selecting text moves no caret — and the postfix
        // returns on __runOriginal before reading the verdict at all.
        //
        // A THIRD FRAME CLASS escapes both of those, and closing it is what _editBranchClaimed is
        // for: an arrow press edge in a frame vanilla's chain gives to Backspace or Delete. The body
        // ran, so __runOriginal is true; Backspace's RemoveCharBehindMarker decrements the index by
        // exactly one (Pug.Other:343485), which the distance gate cannot tell from vanilla's own
        // arrow step, and Delete's RemoveCharAtMarker moves it not at all, which reads as "nobody
        // jumped". Not confined to Alt and RightControl frames: with LeftControl held, that mod's
        // MoveToWord runs while vanilla's compensating ±1 does not, so the distance falls one short
        // of its jump — 1 for a two-character word, 0 for a one-character one — and the distance
        // gate passes both. This flag is what declines them.
        //
        // The signal that resolves it was already arriving here and being discarded: this postfix
        // sees EVERY keyCode vanilla probes, and a Backspace or Delete answering true is a positive
        // report that the cascade took that branch — after which the arrows are never asked about.
        // So a verdict beside it did not come from the game. An earlier version of this paragraph
        // called the class uncatchable and leaned on the jump landing on a real boundary anyway;
        // that is the "harmless by argument" reasoning MSM-31 was written to retire, and it was
        // wrong twice over — the signal exists, and it costs one bool.
        [HarmonyPatch(typeof(MenuManager), "IsKeyDown"), HarmonyPostfix]
        public static void MenuManager_IsKeyDown(KeyCode keyCode, bool __result)
        {
            if (!__result)
                return;
            if (keyCode == KeyCode.LeftArrow)
                _leftArrowFired = true;
            else if (keyCode == KeyCode.RightArrow)
                _rightArrowFired = true;
            else if (keyCode == KeyCode.Backspace || keyCode == KeyCode.Delete)
                _editBranchClaimed = true;
        }

        // Clears the verdicts before vanilla's body re-answers them.
        //
        // The postfix below already consumes them, so in an ordinary frame this clear finds nothing
        // to do. It earns its place in the one frame the consume cannot reach: the one AFTER our
        // postfix failed to run, which a foreign postfix of higher priority throwing would cause.
        // There the flags stand as the body left them, and only this clear retires them.
        //
        // A cancelled body is NOT that case, and reading it as one is the easy mistake: the previous
        // frame's postfix consumed on its way in, so the flags are already false and a body that
        // never runs writes nothing over them. Nor can the consume cover a prefix that throws — an
        // exception there takes the body and the postfixes with it, so neither half runs that frame.
        // Two mechanisms, one frame each, and neither is the other's fallback.
        //
        // It also records where the caret stood before anything moved it, which is what lets the
        // postfix tell "vanilla shifted ±1" from "somebody jumped". See the caretBefore use there.
        //
        // Priority.First is load-bearing for three readings, not one, and all are the same kind of
        // need: sample or clear state before another patch acts. Here it is the caret — another
        // mod's prefix on this same method can move it before vanilla's body runs (BetterTextInput
        // does, a whole word), and reading second would capture an already-jumped caret and report a
        // move of one, exactly inverting the test. It also pins this clear ahead of that mod's own
        // IsKeyDown probes, which the IsKeyDown postfix above relies on; lowering the priority would
        // silently falsify that paragraph too. The third is MSM-12's row-and-key capture below, and
        // it is the one that fails hardest: that mod's Escape branch calls Deactivate(false) itself,
        // so running after it leaves activeInputField null and nothing is captured at all.
        //
        // Harmony orders equal-priority prefixes by load order, which is not ours to pin. That mod
        // declares no priority, so First settles it; a third patch also taking First would fall back
        // to registration order, and only [HarmonyBefore] would truly pin that — not worth it
        // against a case that does not exist. auto-rail-bridges is the corpus precedent, and for the
        // same reason as here rather than a different one: it needs its context captured before
        // PlacementPlus's prefix does its own placement work. (A different comment in that file —
        // on its postfix, not on the attribute — says "Harmony skips remaining prefixes", which is
        // stock-Harmony folklore and false under the HarmonyX this game ships, as the postfix below
        // documents with the citation. It does not affect the attribute's reason, but it is wrong
        // where it stands.)
        //
        // No gating on the input device. There is a row check, but only because the caret read needs
        // a row to ask; the flags are cleared for every frame regardless, which is what the postfix's
        // own consume relies on.
        [HarmonyPatch(typeof(MenuManager), "HandleTypingInput"), HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void MenuManager_PreHandleTypingInput()
        {
            _leftArrowFired = false;
            _rightArrowFired = false;
            _editBranchClaimed = false;
            _caretBeforeBody = CaretUnknown;
            _cancelRowOnEntry = null;
            // The row is recorded BEFORE the caret is asked for, not inside the same condition. The caret
            // read is allowed to fail — TryCaretIndex returns false for a row whose fieldMask is unwired,
            // a state this mod reports rather than assumes — and folding the record into that condition
            // would tie MSM-12's cancel to a precondition it does not have, losing it without a symptom on
            // exactly the rows that are already degraded.
            if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem row)
            {
                // Recorded only when the back key is actually down, so the reference itself carries both
                // facts and the postfix has one thing to test instead of two that could disagree.
                //
                // The KEY could be read in the postfix just as well — an input edge is frame-stable, and
                // vanilla itself polls it more than once per frame. The ROW could not, which is what makes
                // this capture ordering-dependent, unlike the caret read beside it: BetterTextInput's own
                // prefix calls Deactivate(false) for Escape, and once it has, activeInputField is null and
                // this `if` never opens — no row, no cancel. The Priority.First on the attribute is what
                // keeps us ahead of it (First 800 against its unstated Normal 400). Lower the priority and
                // MSM-12 stops working whenever that mod is installed, in silence and only for Escape; see
                // the attribute's own comment.
                if (Manager.input.IsMenuBackButtonDown())
                    _cancelRowOnEntry = row;
                if (row.Viewport.TryCaretIndex(out int caret))
                    _caretBeforeBody = caret;
            }
        }

        // Where the caret stood when this frame's HandleTypingInput was entered, or CaretUnknown if
        // no drill-in row held the field or its counter could not be read. Consumed by the postfix
        // alongside the arrow verdicts, for the same reason they are.
        private const int CaretUnknown = -1;
        private static int _caretBeforeBody = CaretUnknown;

        // MSM-12. The drill-in row that held activeInputField when this frame's HandleTypingInput was
        // entered, AND the back key was down — set only when both hold, so a non-null value already means
        // "this row's edit was asked to be abandoned". The postfix cannot recover the row itself: by then
        // Deactivate has set activeInputField to null. Consumed there alongside the arrow verdicts, for
        // the same reason they are — a row recorded while some other field held focus must not reach a
        // cancel in a later frame.
        private static ModSettingsMenu.UI.ListDetailItem _cancelRowOnEntry;

        // Latched for the session. It used to sit beside a second latch for an unreadable cooldown
        // field; that one retired with the reflection read, so this is the only typing-path warning
        // left and the reason for keeping them apart is gone with it.
        //
        // BetterTextInput reaches this: it cancels for Escape, Home, End, Ctrl+A and the selection
        // paths (see the IsKeyDown postfix above for the full model). Escape is in this list and not
        // in that one, deliberately — that list is about frames carrying an arrow VERDICT, and
        // Escape returns before the arrows are probed, while this guard keys on the arrow being
        // physically down, which Escape does not prevent. Its IME paths cannot reach here at all:
        // they need typingActionWasClicked false, and a probe of an arrow that IS down sets it
        // (Pug.Other:269695) — which this guard's own condition guarantees. Its own Ctrl+Arrow word jump
        // is NOT one of those frames, so this fires from a coincidence rather than from a mod that
        // has taken the feature over — which is what the message has to convey.
        private static bool _warnedForeignTypingPrefix;

        private static void WarnForeignTypingPrefixOnce()
        {
            if (_warnedForeignTypingPrefix)
                return;
            _warnedForeignTypingPrefix = true;
            // Reports the frame it saw, and claims nothing beyond it. The condition moved when the
            // guard did: it used to require that no arrow verdict existed, and now fires whenever
            // the body was skipped with an arrow down — including frames where a verdict existed and
            // was deliberately discarded. So the wording says what happened once.
            //
            // Deliberately not "word jumps keep working" either, though it is true of the one mod
            // that reaches this today: the condition is any foreign prefix returning false, and a
            // patch that cancels every frame satisfies it every frame, for which that reassurance
            // would be exactly wrong. Latched and one-frame, so the honest scope is the frames this
            // one saw. The previous wording erred the other way — it promised permanent breakage —
            // and both errors come from a class-generic condition described as if it were about one
            // mod.
            Debug.LogWarning(
                "[ModSettingsMenu] Another mod's patch skipped MenuManager.HandleTypingInput while an arrow key was down, so a word "
                    + "jump in a settings list was not applied for that frame — the game's own caret movement was skipped by the same "
                    + "patch, so nothing is half-applied and no text is lost. Word jumps still work in frames that patch lets through; "
                    + "how often it takes one over is that patch's business. Logged once per session."
            );
        }

        // Cursor navigation (Home/End, Ctrl+Arrow word jumps) for a drill-in row, as a POSTFIX on
        // MenuManager.HandleTypingInput rather than a poll inside ListDetailItem.Update(). That
        // private method handles the raw arrow keys itself, with NO Ctrl check (Pug.Other:269659-
        // 269666):
        //
        //   else if (IsKeyDown(KeyCode.LeftArrow))  { activeInputField.MoveCharMarker(-1); }
        //   else if (IsKeyDown(KeyCode.RightArrow)) { activeInputField.MoveCharMarker(1); }
        //
        // It runs every frame for whatever holds activeInputField, from a DIFFERENT MonoBehaviour
        // than ours — so one physical Ctrl+Left keypress fires both vanilla's single-character move
        // AND a word jump, and MoveCharMarker is non-virtual and reached only through an interface,
        // so a row cannot intercept vanilla's own call to shortcut it. Which one wins would then
        // depend on Unity's script execution order between two unrelated MonoBehaviours — traced on
        // "abc def   ", Ctrl+Left from the end: MenuManager first shifts −1, then a word jump
        // re-derived from the (already shifted) caret lands correctly on 4; ListDetailItem first
        // jumps to 4, then vanilla's trailing −1 fires and lands on 3, inside the space run. A
        // postfix removes the race rather than hoping to win it: it always runs AFTER vanilla's own
        // arrow handling for the frame, so a word-jump target is computed from the caret's FINAL
        // position and cannot be undone by a later ±1.
        //
        // Home/End live here too, even though vanilla never touches those keycodes (zero hits in the
        // decompile) and so has no ordering hazard of its own — splitting the polling across two
        // mechanisms (a postfix here, a separate poll in ListDetailItem.Update there) would be worse
        // than one method covering all of it.
        [HarmonyPatch(typeof(MenuManager), "HandleTypingInput"), HarmonyPostfix]
        // __runOriginal is HarmonyX's own name for "did the original body run" (0Harmony:10168), bound
        // by parameter name rather than by type, so it needs no HarmonyLib reference and stays inside
        // the Roslyn sandbox. WritePostfixes guarantees the variable exists even with no prefix at all
        // (0Harmony:10671-10676).
        public static void MenuManager_HandleTypingInput(bool __runOriginal)
        {
            // Consumed, not merely read, and ahead of every early return. The early part is what
            // matters most: the IsKeyDown postfix sets these flags for EVERY text field in the game,
            // long before the row check below, so consuming here is what stops a verdict raised
            // while some vanilla field had focus from reaching a drill-in row in a later frame.
            //
            // The clearing prefix does not make this redundant, and the two cover different frames
            // rather than one backing the other up. The game ships HarmonyX, which does NOT skip the
            // remaining prefixes when one returns false: WritePrefixes calls every prefix
            // unconditionally and folds a false return into one accumulator, emitting its single
            // branch — over the original BODY — only after the loop (0Harmony:10287-10323). So the
            // prefix runs in every frame this method is entered at all. The frame it cannot reach is
            // the one where an exception in a foreign prefix takes the body and the postfixes with
            // it; the frame THIS cannot reach is the one after our own postfix was skipped. See the
            // prefix's own comment.
            bool leftFired = _leftArrowFired;
            bool rightFired = _rightArrowFired;
            bool editClaimed = _editBranchClaimed;
            int caretBefore = _caretBeforeBody;
            var cancelRow = _cancelRowOnEntry;
            _leftArrowFired = false;
            _rightArrowFired = false;
            _editBranchClaimed = false;
            _caretBeforeBody = CaretUnknown;
            _cancelRowOnEntry = null;

            // MSM-12 — the back key abandons an edit instead of committing it. Two placements, two
            // different reasons, and each fails silently if moved:
            //
            //   AHEAD of the __runOriginal guard below, because BetterTextInput handles Escape in its own
            //   prefix and returns false, so __runOriginal is false on exactly the frame this must act.
            //   Behind that guard this works alone and silently does nothing with that mod installed.
            //
            //   AHEAD of the `activeInputField is not ListDetailItem` guard, because Deactivate has
            //   already nulled the field — the row is reachable only through what the prefix kept.
            //
            // It deliberately does NOT ask which input device is in use, and must stay ahead of the
            // guard below that does. Vanilla's back-key branch (Pug.Other:269652) is NOT controller-proof:
            // the on-screen-keyboard block returns only when the keyboard actually opens, and
            // GetControllerTextInput answers false on the fallback platform always (Pug.Other:288045) and
            // on Steam whenever the overlay cannot show it (Pug.Other:286913). Execution then falls out
            // of that block at Pug.Other:269625 into this very branch while SystemPrefersKeyboardAndMouse
            // is false — so a device test here would decline exactly the players vanilla is cancelling
            // for. The on-screen-keyboard path needs no exclusion of its own: it answers through
            // TrySetInputText, where the back key is not down and this row is therefore never recorded.
            //
            // The field having moved away from the row is what says the edit ended; the recorded row says
            // it was asked to be abandoned. Together they reconstruct the intent Deactivate(bool commit)
            // throws away (Pug.Other:343542). Restoring the seed is the whole action — the commit that
            // follows the transition then finds an unchanged value and writes nothing.
            if (cancelRow != null && Manager.input.activeInputField != (object)cancelRow)
                cancelRow.CancelEdit();

            if (Manager.input.activeInputField is not ModSettingsMenu.UI.ListDetailItem row)
                return;
            // Controller text arrives through the on-screen keyboard in one callback, with the caret
            // already at the end — keyboard/mouse only, deliberately.
            if (!Manager.input.SystemPrefersKeyboardAndMouse())
                return;

            // Home/End need no index at all: MoveCharMarker is relative AND clamped (Pug.Other:
            // 343455), so a full-length move in either direction lands exactly on the end.
            //
            // They keep plain GetKeyDown rather than the repeat-aware arrow verdicts below, and that
            // is not an oversight: a second Home does nothing the first did not, so repeating one is
            // a no-op rather than a missing feature. Vanilla never reads these keycodes at all, so
            // there is no verdict of its own to observe here even if one were wanted.
            //
            // They also sit AHEAD of the __runOriginal guard below, deliberately, and both of that
            // guard's reasons miss them. It exists because a word jump extends vanilla's own ±1 and
            // has nothing to extend when the body was cancelled — Home and End extend nothing, being
            // this mod's own feature on keycodes the game never reads — and because a stale arrow
            // verdict might be acted on, which they never consult, reading GetKeyDown directly.
            //
            // What that costs is one known interaction, read off BetterTextInput's source rather
            // than measured: it handles Home and End itself and cancels, so both move the caret. The
            // endpoints agree (its MoveToStart/MoveToEnd write 0 and the text length; ours goes
            // through MoveCharMarker, clamped to the same bounds), so the caret lands identically.
            // Its Ctrl+A path is the one that does not compose: it selects and cancels, and a Home
            // in the same frame would drive the caret to 0 while the selection it drew stays on
            // screen. Rarer than the frame the guard fixes, and not invisible. Left as is rather
            // than guarded, because guarding it would need the same signal the guard below has and
            // these branches deliberately do not consult.
            int length = row.pugText.GetTextLength();
            if (Input.GetKeyDown(KeyCode.Home))
            {
                row.MoveCharMarker(-length);
                return;
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                row.MoveCharMarker(length);
                return;
            }
            // Ctrl is the Windows word-jump convention; Alt is the macOS one (Ctrl+Arrow is a
            // desktop-switch shortcut there, so the game never sees it). This machine runs a
            // Windows build under CrossOver, so either physical keyboard convention is plausible —
            // accept both rather than picking one.
            bool wordModifier =
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!wordModifier)
                return;

            // Left before right, matching the order of vanilla's own else-if chain (Pug.Other:
            // 269659-269666), so a frame with both arrows held resolves the same way it does there.
            // Both flags can be set in one frame only if vanilla asked about both, which its chain
            // does not — but the order is written to match anyway, so the two cannot disagree.
            //
            // These are vanilla's OWN verdicts now, not an over-set of them. The distinction is not
            // a nicety: the shared timer this used to read says a key may repeat, never WHICH branch
            // will claim the frame, so a Backspace auto-repeating alongside a held arrow armed a
            // jump in a frame where vanilla moved no caret at all. It cost nothing — the jump is
            // computed from where the caret IS, so it still landed on a word boundary — but it was
            // harmless by argument. Now the chain itself decides: in a vanilla install a Backspace
            // frame returns before the arrows are asked about, so no verdict exists to act on. A mod
            // that probes the arrows ahead of the chain can produce one anyway, which is what
            // _editBranchClaimed handles — see the IsKeyDown postfix.
            //
            // The verdict does not say WHETHER it is a press or a repeat, and this code does not
            // ask: both should jump, so one bool is the whole question. Worth stating because the
            // absence looks like a limitation and is not one — a postfix holds keyCode and runs
            // inside the same MenuManager.Update() frame, so Input.GetKeyDown(keyCode) there is
            // literally vanilla's own first disjunct (Pug.Other:269696) and separates the two
            // exactly. Nothing here needs it. Holding past the 0.3 s arming delay taking a second
            // jump is therefore not a change this brought: the previous shape led with the same
            // GetKeyDown term and behaved identically.
            //
            // What DID change: a press landing in a frame Backspace or Delete has claimed is now
            // dropped rather than acted on, because the old shape read GetKeyDown regardless of the
            // chain. In a vanilla install the chain achieves that by never asking about the arrows
            // there; where a foreign probe asks anyway, _editBranchClaimed does. Dropping it is the
            // right half of the trade — vanilla's own ±1 is absent in
            // exactly those frames too, so the row moves as one thing rather than two — and a held
            // key recovers on the next tick 0.05 s later. Only a tap short enough to fall entirely
            // inside such a frame is lost, and it is lost the way vanilla loses it.
            //
            // Nothing to fall back to any more, and nothing that can fail to be read: with no
            // verdict the direction is 0 and the frame passes. What used to degrade here was the
            // reflection read; it is gone.
            // No body, no jump. A word jump extends vanilla's own ±1, and a cancelled body leaves
            // nothing to extend — while possibly still leaving an arrow verdict behind, since a
            // foreign prefix can probe the arrows before deciding to cancel. Acting on that would
            // move the caret a word in the middle of somebody else's shortcut, and the distance gate
            // below would not decline it: a mod that selects text moves no caret, so the distance
            // reads zero. Keyed on __runOriginal rather than on the verdict because the verdict says
            // a key was down, not whose question that answered; this is HarmonyX's own record of
            // whether the game handled typing at all. Costs nothing where no such mod is loaded,
            // because then the body always runs. Full model: the IsKeyDown postfix above.
            if (!__runOriginal)
            {
                if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow))
                    WarnForeignTypingPrefixOnce();
                return;
            }

            int direction =
                leftFired ? -1
                : rightFired ? 1
                : 0;
            if (direction == 0)
                return;
            // Reading the caret's own counter rather than its on-screen position is what leaves this
            // a plain subtraction, and the reason is weaker than "vanilla always shifted by ±1" — it
            // does not always. Its arrow handling sits in an else-if chain that Backspace, Delete,
            // Return and the menu back button preempt (Pug.Other:269628-269666), and its clamp
            // absorbs the move at either end (Pug.Other:343458). What licenses the subtraction is
            // that the counter reflects whatever actually happened, shift or none: `current` is
            // where the caret IS, so the jump is the difference to the boundary and nothing else.
            //
            // It was not always a subtraction. Recovering the index from the blinker gave the value
            // from BEFORE that shift, because RadicalMenuOptionTextInput.Update repositions the
            // blinker only once per frame (Pug.Other:343386-343388) and MoveCharMarker never touches
            // it — so a compensation term had to subtract the shift, and had to infer from the
            // pre-shift index whether vanilla's own clamp (Pug.Other:343458) had absorbed it. One
            // case escaped that inference and cost a character: IsKeyDown counts a held key via a
            // repeat timer, so a Backspace auto-repeating in the same frame as the arrow keydown
            // sends vanilla down its Backspace branch instead (Pug.Other:269628-269631), where no
            // shift happens at all and the correction then over-corrected. Reading the counter
            // retires the term and that case with it.
            //
            // No index, no jump. Vanilla's own single-character move has already run for this frame
            // and stands on its own, so doing nothing here leaves the caret one character from where
            // it was — a weaker version of what was asked for, and nothing else. The alternative is
            // worse in kind, not just in degree: WordBoundary would scan the string from a position
            // the caret is not at, and MoveCharMarker would then apply that distance as a relative
            // step, so the caret would land somewhere with no relation to any word.
            if (!row.Viewport.TryCaretIndex(out int current))
                return;

            // Somebody already jumped this frame, so this one would be the second. Vanilla moves the
            // caret by exactly ±1 (Pug.Other:269661, :269665) and the prefix read the counter before
            // any of that ran, so a greater distance means a different hand moved it. Named no mod
            // on purpose: any patch doing the same is covered, and a name would go stale. Against
            // the one that exists this is an equivalence rather than a heuristic — its MoveToWord
            // targets one short of the boundary so vanilla's step lands on it, which makes distance
            // 1 mean "it found nothing to move to". That holds on frames vanilla's arrow branch
            // claims; the frame it does not hold on is the last paragraph of the IsKeyDown postfix
            // above.
            //
            // Distance 0 still jumps. In a vanilla install the case that produces it is the clamp —
            // MoveCharMarker bounds to [0, length] (Pug.Other:343458), so Ctrl+Left at index 0 gives
            // a verdict and no movement — and standing still is not evidence that someone else
            // acted. Unknown means the prefix could not read a counter, the same fault TryCaretIndex
            // reports above, and falls through to jumping: the behaviour before this gate, which is
            // right for the install where nothing else patches this row.
            if (caretBefore != CaretUnknown && Mathf.Abs(current - caretBefore) > 1)
                return;

            // Vanilla gave this frame to Backspace or Delete, so the arrow verdict is not its own.
            // Its chain is an else-if cascade: once one of those answers true it never asks about
            // the arrows, so a verdict existing alongside can only have come from a foreign probe.
            //
            // "This frame", strictly. A held Backspace claims only its repeat TICKS — IsKeyDown runs
            // it through the same shared cooldown, so at 60 fps roughly every third frame answers
            // true and the rest fall through to the arrows. So holding Backspace while tapping a
            // word jump still jumps, in the frames vanilla handled the arrow itself, and that is
            // correct rather than a gap this misses: vanilla moved the caret there too. Worth
            // stating because the flag reads like "no jumping while deleting", which it is not.
            // Without this the distance gate lets it through, because RemoveCharBehindMarker
            // decrements the index by exactly one (Pug.Other:343485) — indistinguishable from
            // vanilla's own arrow step — and RemoveCharAtMarker moves it not at all, which reads as
            // "nobody jumped". Free where nothing else patches this: vanilla never produces both.
            if (editClaimed)
                return;

            row.MoveCharMarker(row.Viewport.WordBoundary(current, direction) - current);
        }
    }
}
