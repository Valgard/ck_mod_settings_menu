using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModSettingsMenu
{
    /// <summary>
    /// Phase 1: mounts an (empty) "Mod Settings" screen into the vanilla Options
    /// menu, proving the RadicalMenu mount mechanic (GMCM MenuPatch technique +
    /// HealthBars menu clone). Phase 2 replaces the clone body with an own prefab
    /// + LinearLayout box layout and populates it from the ModSettings registry.
    /// Harmony patch classes are auto-discovered by the loader (no PatchAll()).
    /// </summary>
    [HarmonyPatch]
    public static class MenuPatch
    {
        private static RadicalMenu MenuInstance;

        // Render a raw (non-localised) string and set its colour. Cloned vanilla
        // PugText inherits localize=true (→ "missing: <term>") and keeps a stale
        // tmpColor that Render() does not reset, so we set both explicitly.
        // color == null → the PugText's own style default (text.color getter =
        // style.color); used for the menu title (light) + its dark shadow.
        // Phase 3 replaces the raw strings with Loc.T terms.
        private static void RenderRaw(PugText text, string s, Color? color = null)
        {
            if (text == null) return;
            text.localize = false;
            text.Render(s, rewindEffectAnims: false, force: true);
            text.SetTempColor(color ?? text.color, keepColorOnStart: true);
        }

        // Add a "Mod Settings" entry to the Options menu by cloning the vanilla
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
            RenderRaw(entry.gameObject.GetComponentInChildren<PugText>(), "Mod Settings", PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR);
            entry.GetComponent<RadicalOptionsMenuOption_PushMenu>().menuToPush = ModSettingsMenuMod.SettingsMenuType;
        }

        // Instantiate our (Phase-1 empty) menu: clone the vanilla options menu, clear it, title it.
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Init)), HarmonyPostfix]
        public static void MenuManager_PostInit()
        {
            var menu = Object.Instantiate(Manager.menu.uiOptionsMenuPrefab, Manager.camera.uiCamera.transform)
                             .GetComponent<RadicalOptionsMenu>();
            menu.gameObject.SetActive(false);
            // Title bigtext + shadow keep their own style-default colours (light / dark).
            RenderRaw(menu.transform.Find("Title/Title bigtext").GetComponent<PugText>(), "Mod Settings");
            RenderRaw(menu.transform.Find("Title/Title bigtext shadow").GetComponent<PugText>(), "Mod Settings");
            var scroll = menu.transform.Find("Options/Scroll");
            menu.menuOptions.Clear();
            for (int i = scroll.childCount - 1; i >= 0; i--)
                Object.Destroy(scroll.GetChild(i).gameObject);
            MenuInstance = menu;
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
            return true;
        }
    }
}
