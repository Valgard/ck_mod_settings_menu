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

        // MSM's own master toggle (dogfooded via the public API). Read by ModSettingsScreen.Populate
        // to gate foreign discovery. Its ConfigFile is created via ConfigStore, so ConfigStore.IsOwn
        // excludes it from discovery - MSM never lists its own section as a "foreign" one.
        public static SettingHandle<bool> ShowForeignConfigs;

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
            ModSettings.Section(this)
                .Toggle(out ShowForeignConfigs, "showForeignConfigs", true)
                .Build();
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

        // Deferred restart prompt. ModSettingsScreen.Deactivate requests it, but the prompt must NOT be
        // shown synchronously during the menu-pop: StartNewDisplaySequence → Manager.menu.ShowPopUpMenu
        // → PushMenu(POP_UP) re-enters the menu stack mid-pop and orphans the Cancel/Yes buttons (they
        // persist across every later menu). Update fires it a few frames later, off that call stack,
        // once the stack has settled — mirroring CK's own Invoke("RestartToApplyModChanges", 0.1f).
        private static int _restartPromptCountdown = -1;
        internal static void RequestRestartPrompt() => _restartPromptCountdown = 3;

        public void Update()
        {
            if (_restartPromptCountdown >= 0 && _restartPromptCountdown-- == 0)
                ModSettingsScreen.ShowRestartPrompt();

            if (_warmed) return;
            var menu = MenuPatch.MenuInstance;
            if (menu == null) return;                 // instance not created yet → retry next frame
            _warmed = true;
            if (ModSettings.Sections.Count > 0)       // no consumer → don't spend 1 s at startup for nothing
                menu.PreWarm();
        }
    }
}
