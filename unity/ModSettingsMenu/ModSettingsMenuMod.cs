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

            // Dev-only test fixtures for exercising the discovery path (the list-widget drill-in, and
            // the Choice shapes further down) against something other than a real foreign mod's
            // config — raw CoreLib ConfigFiles created OUTSIDE
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
                // A token WIDER than the row can render, which no other fixture provides. This used to
                // test a truncation guard: the base class trimmed any active row past maxWidth on
                // sight, so this token would show up already shortened, and committing that shortening
                // would have truncated the value in what, for a real mod, is its own config file. That
                // trim no longer runs (ListDetailItem.maxWidth is 0 — the row now scrolls the text
                // horizontally within the field mask instead of cutting it, see TextFieldViewport), so
                // this entry no longer exercises that guard directly. It still exercises the
                // horizontal scroll against a token wider than the field, and ListDetailItem.
                // CommittedText remains the reason an untouched long token is never rewritten even
                // though nothing forces it to be short any more.
                //
                // A long identifier, of the kind HeuristicSaysList used to refuse on its old
                // 32-character limit — it doubles as a check that such a value reaches the drill-in as
                // an editable list rather than a read-only Info row.
                //
                // Neighbours on both sides so both halves of the (now largely historical) truncation
                // guard are visible at once: the untouched ones must survive because the value is
                // assembled from the row list, and the long one must survive intact now that scrolling,
                // not trimming, is what keeps it on screen.
                testFile.Bind(
                    "Settings",
                    "Overlong",
                    "Before, AncientGuardianStatueFragmentPolishedObsidianVariantLarge, After",
                    new ConfigDescription(
                        "A token far wider than the row can render, to check it scrolls horizontally instead of being truncated, and that an untouched long token is never rewritten."
                    ),
                    clientScope
                );
                // Prose that happens to contain a comma — the case the length limit was meant to
                // catch and never did (its tokens are 23 and 15 characters, both under 32). It must
                // render as a read-only Info row, NOT as an editable list: committing it would
                // rejoin it on commas and quietly reformat the owning mod's sentence.
                testFile.Bind(
                    "Settings",
                    "ProseNotAList",
                    "This is a long sentence, and another one",
                    new ConfigDescription("Prose with a comma, to check it is not mistaken for a list."),
                    clientScope
                );
                // Word jumps need spaces, and none can be typed into a row (trim: 1 drops a lone
                // space, matching vanilla). A value loaded from a config may well contain them,
                // which is the case Ctrl/Alt+Arrow actually serves — so exercise it from here.
                // Two words per token deliberately: HeuristicSaysList refuses anything longer,
                // and a three-word token would arrive as a read-only Info row instead.
                testFile.Bind(
                    "Settings",
                    "WithSpaces",
                    "Item One, Item Two, Big Chest",
                    new ConfigDescription("Tokens containing spaces, to exercise word jumps."),
                    clientScope
                );

                // Discovered Choice fixtures, in a file of their own so the two concerns stay legible
                // as two "(detected)" sections while clicking through. No installed third-party mod
                // binds an AcceptableValueList at all — MSM is the only user of one on this machine —
                // so without these there is nothing to see the discovered Choice path against.
                var choiceFile = new ConfigFile("TestChoiceFixtures/config.cfg", saveOnInit: true, info);
                // The exact path: the type argument is string, so the values are read straight off the
                // constraint.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceStrings",
                    "Medium",
                    new ConfigDescription("A string choice.", new AcceptableValueList<string>("Low", "Medium", "High")),
                    clientScope
                );
                // Still the exact path, with a token containing the ", " the description format joins
                // on — which is precisely what the reconstruction below cannot survive, and what this
                // one is indifferent to. Also checks the value is stored raw, not TOML-escaped.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceComma",
                    "Alpha",
                    new ConfigDescription("A string choice whose tokens contain commas.", new AcceptableValueList<string>("Alpha", "Beta, and more")),
                    clientScope
                );
                // The reconstruction path: int is not reachable through the exact branch, so these
                // tokens come from ToDescriptionString() and have to pass convert-then-IsValid.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceInts",
                    4,
                    new ConfigDescription("An int choice, reconstructed from the description.", new AcceptableValueList<int>(1, 2, 4, 8)),
                    clientScope
                );
                // An enum Choice carries no constraint (AcceptableValueList cannot hold one — its T is
                // IEquatable, which no enum satisfies) and reaches Choice one case earlier, from its
                // member names. Here to catch a regression in the read/write path the string cases
                // above now share with it, which used to be enum-only.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceEnum",
                    TestChoice.Second,
                    new ConfigDescription("An enum choice, to check the member-name path still round-trips."),
                    clientScope
                );
                // The negative control: a constraint on a type no case handles, so nothing may promote
                // it to a Choice — it has to stay the read-only Info row it is today.
                choiceFile.Bind(
                    "Settings",
                    "RangeDouble",
                    1.5,
                    new ConfigDescription("A range of an unhandled numeric type; must stay a read-only Info row.", new AcceptableValueRange<double>(0.0, 10.0)),
                    clientScope
                );
            }
        }

        /// <summary>Dev-only, for the ChoiceEnum fixture above. Three members so cycling has somewhere
        /// to wrap, and deliberately not alphabetical so a sort would be visible.</summary>
        private enum TestChoice
        {
            First,
            Second,
            Third,
        }

        public void Init()
        {
            Debug.Log("[ModSettingsMenu] Mod initialized.");
            var section = ModSettings.Section(this).Toggle(out ShowForeignConfigs, "showForeignConfigs", true);
            if (DevFlags.Is("TestFixtures"))
                AddDeclaredListFixtures(section);
            section.Build();
        }

        // Dev-only fixtures for the DECLARED list path — the counterpart to the raw ConfigFile
        // fixtures in EarlyInit, which exercise discovery instead. Both are needed and neither
        // stands in for the other: a discovered list is always declared FreeText, because the
        // heuristic cannot know an entry set is closed. So OrderOnly is reachable through this path
        // alone — while ReadOnly is reachable both ways, and the two are worth telling apart: here
        // it is a DECLARATION, whereas the LongReadOnly fixture above arrives at the same rendering
        // through a ViewOnly scope. Only the declared one carries a defaults reconciliation, and
        // only the scoped one is skipped by a section reset.
        //
        // Declared through the real public API, in this mod's own section, so what is being checked
        // is the API a consumer actually calls rather than a SettingDef assembled by hand.
        private static void AddDeclaredListFixtures(SectionBuilder section)
        {
            section.List(out _, "testListFreeText", new[] { "Alpha", "Beta", "Gamma" });
            // Long enough that reordering has somewhere to travel, and that the arrow columns can be
            // walked past the visible edge — the read-only fixture in EarlyInit covers a long list
            // with no columns at all, which is a different chain.
            section.List(out _, "testListOrderOnly", new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth" }, ListEditing.OrderOnly);
            // A single entry, because that is the shape whose navigation has no neighbour to wrap to
            // and therefore takes ChainRowsForUIElementNavigation's empty-neighbour path.
            section.List(out _, "testListOrderOnlySingle", new[] { "Alone" }, ListEditing.OrderOnly);
            // Declared read-only, as opposed to the EarlyInit fixture that becomes read-only through
            // a ViewOnly scope. Same rendering, opposite origin — and the one that proves a consumer
            // can ask for it without any permission machinery.
            section.List(out _, "testListReadOnly", new[] { "Alpha", "Beta", "Gamma" }, ListEditing.ReadOnly);
            // A declaration that cannot work: no entries, and no way for the player to add one. It
            // exists to be opened, because the drill-in must REFUSE to open rather than push an
            // empty screen — an empty menuOptions crashes CK in three different places, one of them
            // inside base.Activate() before a key is ever pressed (see ListDetailScreen.Open).
            // The only fixture here whose expected outcome is a warning in Player.log.
            section.List(out _, "testListOrderOnlyEmpty", new string[0], ListEditing.OrderOnly);
            // Duplicate defaults: both reconciliation branches must collapse them to the first, and
            // the declaration must say so in the log. The two branches disagreed about this once.
            section.List(out _, "testListReadOnlyDuplicate", new[] { "Alpha", "Beta", "Alpha" }, ListEditing.ReadOnly);
            // The same duplicate at OrderOnly, which takes the OTHER reconciliation branch — the one
            // that keeps the player's order and dedupes through the case-insensitive match. The
            // ReadOnly fixture above cannot reach it.
            section.List(out _, "testListOrderOnlyDuplicate", new[] { "Alpha", "Beta", "Alpha" }, ListEditing.OrderOnly);
            // Reaches BindGuarded without needing a filesystem fault: binding one key twice with
            // different types trips ConfigFile.Bind's unchecked cast. The Slider must be logged and
            // left out, the Toggle and everything after must survive — and RequiresRestart() after a
            // failed declaration must refuse rather than mark the setting before it.
            section.Toggle(out _, "testDupKey", true).Slider(out _, "testDupKey", 0f, 10f, 5f, 1f).RequiresRestart();
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
            // Poll instead of trying to detect which teardown path fired: if the flag is still set
            // and neither of our own screens is anywhere in CK's menu stack, we are not "covered" by
            // either of them anymore (regardless of how we got here), so it is safe — and necessary
            // — to flush it here.
            //
            // Membership, NOT the top of the stack. Anything our own screens push sits above them
            // while staying entirely inside our own UI — the delete confirmation is exactly that, a
            // PushMenu(POP_UP). Testing the top mistook that for "the player has left", flushed the
            // flag mid-dialogue, and three frames later the restart prompt landed on CK's single
            // shared centerPopUpText while the delete dialogue still owned it: the restart text
            // appeared over the delete dialogue's own buttons, and confirming a deletion restarted
            // the game. HasMenuInStack (Pug.Other:269775) asks the right question, and it resolves
            // our menu types through TypeToMenu, which MenuPatch's prefix already answers.
            if (ModSettingsScreen.RestartPending)
            {
                bool stillInsideOurUI = Manager.menu.HasMenuInStack(SettingsMenuType) || Manager.menu.HasMenuInStack(ListDetailMenuType);
                if (!stillInsideOurUI)
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
