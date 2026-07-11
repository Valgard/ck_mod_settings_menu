using CoreLib.Util.Extension;
using ModSettingsMenu.Settings;
using ModSettingsMenu.UI;
using PugMod;
using UnityEngine;

namespace ModSettingsMenu
{
    /// <summary>
    /// Mod bootstrap. The Pugstorm mod loader instantiates this class on game
    /// start and calls the IMod lifecycle methods. Harmony patch classes are
    /// auto-discovered by the loader — there is no PatchAll() call.
    /// </summary>
    public sealed class ModSettingsMenuMod : IMod
    {
        // Free id outside the vanilla RadicalMenu.MenuType enum; distinct from GMCM(1493)/HealthBars(19901).
        public const RadicalMenu.MenuType SettingsMenuType = (RadicalMenu.MenuType)29314;

        // Set in EarlyInit; MenuPatch instantiates MenuPrefab in the Options postfix.
        public static AssetBundle AssetBundle { get; private set; }
        public static GameObject MenuPrefab { get; private set; }

        public void EarlyInit()
        {
            var info = ((IMod)this).GetModInfo();
            if (info != null && info.AssetBundles != null && info.AssetBundles.Count > 0)
                AssetBundle = info.AssetBundles[0];
            else
                Debug.LogWarning("[ModSettingsMenu] no AssetBundle — menu prefab will be unavailable.");
        }

        public void Init()
        {
            Debug.Log("[ModSettingsMenu] Mod initialized.");
        }

        public void ModObjectLoaded(Object obj)
        {
            if (obj is GameObject go && go.GetComponent<ModSettingsScreen>() != null)
                MenuPrefab = go;
        }

        public void Shutdown()
        {
        }

        // One-shot guard: pre-warm the menu on the first frame the instance exists (MenuManager.Init
        // postfix has run). All IMod.Init — including consumers — run before the first Update, so the
        // registry is already populated here.
        private bool _warmed;

        public void Update()
        {
            if (_warmed) return;
            var menu = MenuPatch.MenuInstance;
            if (menu == null) return;                 // instance not created yet → retry next frame
            _warmed = true;
            if (ModSettings.Sections.Count > 0)       // no consumer → don't spend 1 s at startup for nothing
                menu.PreWarm();
        }
    }
}
