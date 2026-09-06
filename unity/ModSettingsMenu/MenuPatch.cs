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

        // Suppress CK's own hover-driven reselection while a list-detail row is being actively
        // edited. UIMouse re-derives menu selection from a hover raycast every frame and calls this
        // method regardless of text-input state; letting it through moves RadicalMenu.selectedIndex
        // to whatever the mouse passes over, which (a) plays the menu-select SFX and (b) drives
        // PugTextEffectMenuOption.PugTextEffectLateUpdate — a per-frame, selectedIndex-only check
        // that tints the hovered row's text blue and the row being edited back to grey, entirely
        // independent of RadicalMenuOption's OnSelected/OnDeselected (which ListDetailItem already
        // suppresses during an edit — insufficient here, since this effect never goes through them).
        // Skipping SelectOptionIndex here leaves it exactly where it was before the hover, so the
        // edited row keeps looking selected and nothing else reacts to a change that didn't happen.
        // Does NOT stop a click on a different row from switching (that's UIMouse_TrySelectNewElement
        // below, a separate mechanism this patch alone cannot reach) — see that patch's note.
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.SelectOption)), HarmonyPrefix]
        public static bool MenuManager_SelectOption(UIelement option)
        {
            if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem active && (object)option != active)
                return false;
            return true;
        }

        // The actual "click on a different row while editing switches focus there" bug lives here,
        // NOT in RadicalMenuOption's OnLeftClicked/OnActivated chain (ListDetailItem's own guards on
        // those are insufficient — see below). UIMouse.TrySelectNewElement, called every frame from
        // the hover raycast for BOTH plain hover and the click itself, contains its own hardcoded
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
        // UIManager.HideAllInventoryAndCraftingUI ends with, guarded by textInputIsActive:
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
        [HarmonyPatch(typeof(UIManager), nameof(UIManager.HideAllInventoryAndCraftingUI)), HarmonyPrefix]
        public static void UIManager_HideAllInventoryAndCraftingUI()
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
        // That holds for the game's own calls, not for every call there could be, and the
        // difference is not theoretical: BetterTextInput ships an accessor assembly and calls
        // MenuManager.IsKeyDown for both arrows from its OWN HandleTypingInput prefix, ahead of the
        // chain that would have short-circuited. A postfix cannot tell whose call it is answering,
        // so with that mod loaded an arrow verdict can exist in a frame vanilla never asks about
        // arrows. What comes back is much less than what this removes, and the reason is the second
        // parameter: every one of those probes passes checkOnlyOnPressedDown: true, which drops the
        // GetKey-and-timer half of the condition (Pug.Other:269696) — the same opt-out Return and
        // KeypadEnter use at :269636. So the probes answer true on a press edge only, one frame per
        // press, where the surplus removed here was a HELD arrow's 20 Hz repeat stream. A stray
        // verdict per fresh press, not the stream back. Whether the clearing prefix below happens to
        // run after that probe and erase it is Harmony's load order, which is not a guarantee. Not
        // measured, because the mod is not loaded on this machine; docs/roadmap.md MSM-32 owns that
        // pairing and now records this alongside it.
        [HarmonyPatch(typeof(MenuManager), "IsKeyDown"), HarmonyPostfix]
        public static void MenuManager_IsKeyDown(KeyCode keyCode, bool __result)
        {
            if (!__result)
                return;
            if (keyCode == KeyCode.LeftArrow)
                _leftArrowFired = true;
            else if (keyCode == KeyCode.RightArrow)
                _rightArrowFired = true;
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
        // No gating on the active field or the input device. This is two assignments, the guards
        // would cost more than they save, and every one of them would be a second place to keep in
        // step with the postfix's own.
        [HarmonyPatch(typeof(MenuManager), "HandleTypingInput"), HarmonyPrefix]
        public static void MenuManager_PreHandleTypingInput()
        {
            _leftArrowFired = false;
            _rightArrowFired = false;
        }

        // Latched for the session. It used to sit beside a second latch for an unreadable cooldown
        // field; that one retired with the reflection read, so this is the only typing-path warning
        // left and the reason for keeping them apart is gone with it.
        //
        // No mod in the corpus does this to us today. BetterTextInput is the only one with a prefix
        // on this method, and while an arrow is held it returns TRUE — its own IsKeyDown probe sets
        // typingActionWasClicked through GetKey (Pug.Other:269695), so it falls through to the
        // pass-through return. It cancels for Escape, Home/End, Ctrl+A and the selection and IME
        // paths. So this is hardening against the class, not against that mod.
        private static bool _warnedForeignTypingPrefix;

        private static void WarnForeignTypingPrefixOnce()
        {
            if (_warnedForeignTypingPrefix)
                return;
            _warnedForeignTypingPrefix = true;
            // Names what takes over, not only what is lost. With the body skipped vanilla moves no
            // caret either, so the row does not half-work: the key does nothing at all rather than
            // jumping once and then crawling, which is what the previous shape degraded to.
            Debug.LogWarning(
                "[ModSettingsMenu] Another mod's patch is skipping MenuManager.HandleTypingInput, so the game's own typing body does "
                    + "not run and word jumps have nothing to key off — Ctrl+Arrow (or Alt+Arrow) does nothing in a settings list "
                    + "while that patch is active. The game's own caret movement is skipped by the same patch, so nothing is "
                    + "half-applied and no text is lost. Logged once per session."
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
            _leftArrowFired = false;
            _rightArrowFired = false;

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
            // harmless by argument. Now the chain itself decides: a Backspace frame returns before
            // the arrows are asked about, so no verdict exists to act on.
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
            // chain. Dropping it is the right half of the trade — vanilla's own ±1 is absent in
            // exactly those frames too, so the row moves as one thing rather than two — and a held
            // key recovers on the next tick 0.05 s later. Only a tap short enough to fall entirely
            // inside such a frame is lost, and it is lost the way vanilla loses it.
            //
            // Nothing to fall back to any more, and nothing that can fail to be read: with no
            // verdict the direction is 0 and the frame passes. What used to degrade here was the
            // reflection read; it is gone.
            int direction =
                leftFired ? -1
                : rightFired ? 1
                : 0;
            if (direction == 0)
            {
                // Diagnosis only, and deliberately kept after the modifier check so it speaks for
                // frames where a word jump was actually asked for. It reports the state it can
                // observe — no verdict, and vanilla's body skipped — rather than a cause, and the
                // wording is chosen for that reason: a cancelled body USUALLY means IsKeyDown never
                // ran, but not always, since a mod can probe the arrows before deciding to cancel,
                // which BetterTextInput does (see the IsKeyDown postfix above). In that frame a
                // verdict exists, direction is non-zero, and this branch is never entered — which is
                // correct, and is why the guard keys on the absent verdict rather than on
                // __runOriginal alone. What is lost when nothing was published is the jump the
                // timer-reading shape would still have fired off GetKeyDown; that is the price of
                // taking the verdict from the one place vanilla makes it, and saying so out loud is
                // not.
                if (!__runOriginal && (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)))
                    WarnForeignTypingPrefixOnce();
                return;
            }
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
            row.MoveCharMarker(row.Viewport.WordBoundary(current, direction) - current);
        }
    }
}
