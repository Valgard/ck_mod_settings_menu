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

            // Insert AT the caret instead of always at the text end. currentCharIndex is private on
            // the base class, so the caret position is recovered from the blinker via
            // TextFieldViewport.CaretIndex rather than read directly. MoveCharMarker below is
            // relative (Pug.Other:343455): the caret was at `at`, the text just grew by s.Length
            // there, so a +s.Length relative move lands on the right side of what was typed —
            // whether or not `at` was the end of the string.
            int at = Mathf.Clamp(row.Viewport.CaretIndex, 0, current.Length);
            text.SetText(current.Insert(at, s));
            text.Render(rewindEffectAnims: false);
            row.MoveCharMarker(s.Length);
            return false;
        }

        // Cursor navigation (Home/End, Ctrl+Arrow word jumps) for a drill-in row, as a POSTFIX on
        // MenuManager.HandleTypingInput rather than a poll inside ListDetailItem.Update(). That
        // private method handles the raw arrow keys itself, with NO Ctrl check (Pug.Other:269655-
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
        public static void MenuManager_HandleTypingInput()
        {
            if (Manager.input.activeInputField is not ModSettingsMenu.UI.ListDetailItem row)
                return;
            // Controller text arrives through the on-screen keyboard in one callback, with the caret
            // already at the end — keyboard/mouse only, deliberately.
            if (!Manager.input.SystemPrefersKeyboardAndMouse())
                return;

            // Home/End need no index at all: MoveCharMarker is relative AND clamped (Pug.Other:
            // 343455), so a full-length move in either direction lands exactly on the end.
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
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                return;

            int direction =
                Input.GetKeyDown(KeyCode.LeftArrow) ? -1
                : Input.GetKeyDown(KeyCode.RightArrow) ? 1
                : 0;
            if (direction == 0)
                return;
            // The blinker is only repositioned by RadicalMenuOptionTextInput.Update(), once per
            // frame (Pug.Other:343386-343388), and MoveCharMarker never touches it — so the index
            // recovered here is the one from BEFORE vanilla's arrow shift, which ran moments ago
            // inside this same call (Pug.Other:269659-269666). That stale value is the right base
            // for WordBoundary, but the wrong base for a relative move: MoveCharMarker will apply
            // our delta on top of an index vanilla has already nudged by `direction` — UNLESS
            // vanilla's own clamp (Pug.Other:343457) absorbed the shift, which happens exactly when
            // the caret was already sitting at that end. `current` is that pre-shift index, so it is
            // the right value to test: subtract the shift only where there was one, or a Ctrl+Left
            // at index 0 would push the caret forward instead of leaving it put.
            //
            // "Shifted, or clamped away — no third case" is not a guess: vanilla's arrow branch is
            // reached only when Backspace, Delete, Return and the menu back button are all up — its
            // else-if chain (Pug.Other:269626-269666) tests those first. Without that, `current`
            // could not stand in for "did vanilla move" at all.
            //
            // One case escapes it: IsKeyDown counts a held key via a repeat timer, so a Backspace
            // being auto-repeated in the same frame as our arrow keydown sends vanilla down the
            // Backspace branch instead (Pug.Other:269693-269701) — it never reaches MoveCharMarker,
            // so no shift happens at all, and this compensation then over-corrects by one. Not
            // handled: it would cost a second guard for a two-key combination nobody performs
            // deliberately, and the damage is a misplaced caret, not lost text.
            int current = row.Viewport.CaretIndexFromLocalX(row.Viewport.CaretLocalX);
            int vanillaShift = (direction < 0 ? current > 0 : current < length) ? direction : 0;
            row.MoveCharMarker(row.Viewport.WordBoundary(current, direction) - current - vanillaShift);
        }
    }
}
