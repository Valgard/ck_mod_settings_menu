using CoreLib.Data.Configuration;
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

        // Second free id for the list drill-in detail screen (distinct from SettingsMenuType 29314).
        public const RadicalMenu.MenuType ListDetailMenuType = (RadicalMenu.MenuType)29315;
        public static GameObject ListDetailPrefab { get; private set; }

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

            // Dev-only test fixtures for exercising the list-widget drill-in against something
            // other than a real foreign mod's config — a raw CoreLib ConfigFile created OUTSIDE
            // ConfigStore.ForMod, so ConfigStore.IsOwn doesn't recognize it and
            // ForeignConfigDiscovery treats it exactly like a real 3rd-party mod's list setting.
            // Gated on DevFlags.Is("TestFixtures") (see DevFlags.generated.cs, regenerated from
            // the MOD_DEV_FLAGS env var by CLIBuildHelper.Build on every build) — OFF by default,
            // so a normal build never ships these into a real player's settings screen; opt in
            // locally with `MOD_DEV_FLAGS=TestFixtures ../utils/build.sh` while iterating on this
            // widget. See .envrc.example.
            if (DevFlags.Is("TestFixtures"))
            {
                // Client scope (not CoreLib's Server default) so these stay editable at the title
                // screen too, where Manager.main.player is null — ForeignConfigDiscovery.IsReadOnly
                // conservatively treats a non-Client scope as read-only there (real foreign mods, incl.
                // PlacementPlus's own ExcludeItems, are typically Server-scoped and share that limit).
                var clientScope = new ConfigScope(ConfigAccessLevel.Client);
                var testFile = new ConfigFile("TestListFixtures/config.cfg", saveOnInit: true, info);
                testFile.Bind("Settings", "Short", "Alpha, Beta, Gamma", new ConfigDescription("A short test list."), clientScope);
                const string longValue =
                    "Item01, Item02, Item03, Item04, Item05, Item06, Item07, Item08, Item09, Item10, "
                    + "Item11, Item12, Item13, Item14, Item15, Item16, Item17, Item18, Item19, Item20";
                testFile.Bind("Settings", "Long", longValue, new ConfigDescription("A long test list (scroll-follow check)."), clientScope);
                // ViewOnly (not Server) is read-only unconditionally, regardless of Manager.main.player —
                // ForeignConfigDiscovery.IsReadOnly only treats Server/Admin as read-only AT THE TITLE
                // SCREEN specifically (no player yet); a real world session would make a Server-scoped
                // entry editable again. ViewOnly is the one access level IsReadOnly returns true for
                // unconditionally, so this stays a genuine read-only List regression check in any session.
                testFile.Bind(
                    "Settings",
                    "LongReadOnly",
                    longValue,
                    new ConfigDescription("A read-only copy of Long, to check the read-only List path."),
                    new ConfigScope(ConfigAccessLevel.ViewOnly)
                );
                // Exercises OnRowTextCommitted's RequiresRestart wiring (no other fixture above sets
                // requireReload, so there would otherwise be no way to trigger the restart prompt from
                // a list edit at all).
                testFile.Bind(
                    "Settings",
                    "ShortRestart",
                    "Alpha, Beta, Gamma",
                    new ConfigDescription("A restart-required copy of Short, to check the list RequiresRestart path."),
                    new ConfigScope(ConfigAccessLevel.Client, requireReload: true)
                );
                // A token WIDER than the row can render, which no other fixture provides. The base
                // class trims any active row past maxWidth on sight, so this one shows up already
                // shortened — and committing that shortening would truncate the value in what, for a
                // real mod, is its own config file. Guarded by ListDetailItem.CommittedText; this
                // entry is how you check that the guard actually holds.
                //
                // Neighbours on both sides so the two halves of the guard are visible at once: the
                // untouched ones must survive because the value is assembled from the row list, and
                // the long one must survive because it was never typed into.
                testFile.Bind(
                    "Settings",
                    "Overlong",
                    "Before, AncientGuardianStatueFragmentPolishedObsidianVariantLarge, After",
                    new ConfigDescription("A token too wide for the row, to check that display-side truncation is not written back."),
                    clientScope
                );
            }
        }

        public void Init()
        {
            Debug.Log("[ModSettingsMenu] Mod initialized.");
            ModSettings.Section(this).Toggle(out ShowForeignConfigs, "showForeignConfigs", true).Build();
        }

        public void ModObjectLoaded(Object obj)
        {
            if (obj is GameObject go)
            {
                if (go.GetComponent<ModSettingsScreen>() != null)
                    MenuPrefab = go;
                else if (go.GetComponent<ListDetailScreen>() != null)
                    ListDetailPrefab = go;
            }
        }

        public void Shutdown() { }

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

            // Backstop for RestartPending's normal consumption path (ModSettingsScreen.Deactivate,
            // pop=true only). MenuManager.PopAllMenus deactivates only the FIRST menu it finds with
            // popsOtherActiveMenus (both our screens have it set) and then breaks out of its own loop
            // before clearing the rest of the stack — so closing everything at once while the list
            // drill-in is open on top skips ModSettingsScreen.Deactivate(pop: true) entirely, and a
            // RequiresRestart edit made in the drill-in moments before would never get its prompt.
            // Poll instead of trying to detect which teardown path fired: if the flag is still set and
            // NEITHER of our own menu instances is currently the top of CK's menu stack, we are
            // unambiguously not "covered" by either of them anymore (regardless of how we got here),
            // so it is safe — and necessary — to flush it here.
            if (ModSettingsScreen.RestartPending)
            {
                var top = Manager.menu.GetTopMenu();
                if (top != MenuPatch.MenuInstance && top != MenuPatch.ListDetailInstance)
                {
                    ModSettingsScreen.RestartPending = false;
                    RequestRestartPrompt();
                }
            }

            if (_warmed)
                return;
            var menu = MenuPatch.MenuInstance;
            if (menu == null)
                return; // instance not created yet → retry next frame
            _warmed = true;
            if (ModSettings.Sections.Count > 0) // no consumer → don't spend 1 s at startup for nothing
                menu.PreWarm();
        }
    }
}
