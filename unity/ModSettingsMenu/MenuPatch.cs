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
            if (__instance is not ModSettingsMenu.UI.ListDetailItem)
                return true;

            // Same filtering the base class does — trim, newline strip, whitelist — minus the width
            // rejection. Our rows ship an empty characterWhiteList (prefab-verified), so only the trim
            // and newline rules can actually change anything here.
            if (__instance.trim)
                s = s.Trim();
            if (__instance.dontAllowNewLines)
                s = s.Replace("\n", "").Replace("\r", "");
            if (string.IsNullOrEmpty(s))
                return false;

            var text = __instance.pugText;
            text.SetText(text.GetText() + s);
            text.Render(rewindEffectAnims: false);
            // currentCharIndex is private on the base class, so the caret is moved through the public
            // clamped API rather than assigned directly: MoveCharMarker is relative (Pug.Other:343455).
            __instance.MoveCharMarker(s.Length);
            __instance.WasAutoActivated = false;
            return false;
        }
    }
}
