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

        // MSM-28: once-per-open naming diagnostics for a discovered mod's rows, read by
        // ForeignConfigDiscovery.Discover(). Off by default. Bound directly on MSM's own ConfigFile
        // in Init() rather than through SectionBuilder — see BindNamingDiagnostics for why it is its
        // own bind rather than a second .Toggle() call.
        public static SettingHandle<bool> NamingDiagnostics;

        public void EarlyInit()
        {
            var info = ((IMod)this).GetModInfo();
            if (info != null && info.AssetBundles != null && info.AssetBundles.Count > 0)
                AssetBundle = info.AssetBundles[0];
            else
                Debug.LogWarning("[ModSettingsMenu] no AssetBundle — menu prefab will be unavailable.");

            // Dev-only test fixtures for exercising the discovery path (the list-widget drill-in, and
            // the Choice shapes further down) against something other than a real foreign mod's
            // config — raw CoreLib ConfigFiles created OUTSIDE ConfigStore.ForMod, so
            // ConfigStore.IsOwn doesn't recognize them and ForeignConfigDiscovery treats them exactly
            // like a real 3rd-party mod's settings. Several files, and how they are divided is
            // load-bearing rather than cosmetic: Bind and every later write end in ConfigFile.Save,
            // which asks each entry in that file for its description, so a fixture that refuses to give
            // one takes the whole file's writes with it. Hence one file for the lists, one for the
            // ordinary Choice shapes, and one PER fixture whose description throws — see
            // BindThrowingFixture, which owns that last rule so a caller cannot break it.
            // Gated on DevFlags.Is("TestFixtures") (see DevFlags.generated.cs, regenerated from
            // the MOD_DEV_FLAGS env var by CLIBuildHelper.Build on every build) — OFF by default,
            // so a normal build never ships these into a real player's settings screen; opt in
            // locally with `MOD_DEV_FLAGS=TestFixtures ../utils/build.sh` while iterating on these
            // widgets. What each fixture is for is in docs/manual-tests.md; see .envrc.example.
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
                // as two "(detected)" sections while clicking through. Every AcceptableValueList on
                // this machine lives in a config file MSM itself created — its consumers' own Choice
                // widgets bind one each — and ConfigStore.IsOwn excludes exactly those from discovery.
                // So without these there is nothing to see the discovered Choice path against, and that
                // stays true for as long as no third-party mod binds one.
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
                // Still the exact path, with two tokens the reconstruction could not handle and this
                // branch is indifferent to. The comma is the separator the description format joins on.
                // The quote is the one that discriminates the read paths: it is in Escape's set, so
                // GetSerializedValue() would render it `Say \"hi\"` — the row would display backslashes
                // and its own value would stop matching. Without it nothing here could tell the new read
                // from the old one, since Escape leaves every other character in these tokens alone.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceComma",
                    "Alpha",
                    new ConfigDescription(
                        "A string choice whose tokens contain a comma and a quote.",
                        new AcceptableValueList<string>("Alpha", "Beta, and more", "Say \"hi\"")
                    ),
                    clientScope
                );
                // A plain integer value list. It used to demonstrate the reconstruction — int was not
                // reachable through the exact branch, so its tokens came out of ToDescriptionString()
                // and had to pass convert-then-IsValid. ReadExactValues names int now, so this reads
                // exactly, and what it guards is the OTHER half: a non-string type is written back
                // through TomlTypeConverter.ConvertToValue, so a token that did not come from
                // ChoiceToken.Of would not survive the trip. An int renders the same either way, which
                // is exactly why it is the quiet check — ChoiceFloats is where a mismatch is loud.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceInts",
                    4,
                    new ConfigDescription("An int choice, read exactly.", new AcceptableValueList<int>(1, 2, 4, 8)),
                    clientScope
                );
                // An enum Choice carries no constraint (AcceptableValueList cannot hold one — its T is
                // IEquatable, which no enum satisfies) and reaches Choice one case earlier, from its
                // member names. Here to catch a regression in the member-name round trip: the read the
                // string cases now share with it, which used to be enum-only, and the converted write.
                // NOT the enum-only guard one line further on — this value always sits on a member
                // name, so it never reaches it. ChoiceFlags below is what does.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceEnum",
                    TestChoice.Second,
                    new ConfigDescription("An enum choice, to check the member-name path still round-trips."),
                    clientScope
                );
                // One acceptable value: the cycle has nowhere to go. The wrap arithmetic must survive a
                // length of 1, and a write that changes nothing must not raise the restart flag.
                // testListOrderOnlySingle covers the same shape on the list path.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceSingle",
                    "Only",
                    new ConfigDescription("A one-option choice.", new AcceptableValueList<string>("Only")),
                    clientScope
                );
                // The read-only Choice, ViewOnly for the same reason LongReadOnly is: the one access
                // level that reads read-only in every session. Worth its own fixture because a locked
                // row takes a different path through the widget (MakeValueReadOnly, the early return in
                // Adjust) while still having to DISPLAY the right token — and the display line is one of
                // the two this change rewrote. It is also the shape a real foreign Choice usually has
                // here: ConfigScope defaults to Server, which IsReadOnly treats as locked at the title
                // screen, and the title screen is where this walk is easiest to run.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceReadOnly",
                    "Medium",
                    new ConfigDescription("A read-only copy of ChoiceStrings.", new AcceptableValueList<string>("Low", "Medium", "High")),
                    new ConfigScope(ConfigAccessLevel.ViewOnly)
                );
                // The [Flags] combination the Choice case's `break` exists for. ChoiceEnum always sits ON
                // a member name and so never reaches that guard; a combination has no member name, and
                // Enum.GetNames never contains one, so idx is always -1 here. Without the guard one
                // keypress would clobber "Alpha, Beta" down to "Alpha" in a foreign mod's own file.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceFlags",
                    TestFlags.Alpha | TestFlags.Beta,
                    new ConfigDescription("A [Flags] enum holding a combination; cycling must leave it untouched."),
                    clientScope
                );
                // The decimal separator against a REAL constraint, and the fixture this mod's exact-read
                // path exists for. It used to have two legal outcomes — a three-option Choice on an
                // invariant culture, a read-only Info row on a comma-decimal one, because the
                // description split into fragments. Now there is one: ReadExactValues reads the floats
                // themselves and no separator is ever involved, so this must be 0.5 / 1.5 / 2.5 on every
                // machine. It is also the only fixture that exercises the repaired round trip, since
                // float is where ToString() (culture) and ConvertToValue (invariant) used to disagree —
                // cycling must land on the value shown, not on ten times it.
                // The deterministic version of the old trap survives as RefuseSplitValue below, which
                // still takes the reconstruction: it is a DescriptionOnlyValues, not a value list.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceFloats",
                    1.5f,
                    new ConfigDescription(
                        "A float choice, read exactly; must show 0.5/1.5/2.5 whatever the culture.",
                        new AcceptableValueList<float>(0.5f, 1.5f, 2.5f)
                    ),
                    clientScope
                );
                // The reconstruction's SUCCESS case, and the only one — every other parse-path fixture
                // ends in a rejection. It was ChoiceInts until this file's exact read took int over, and
                // a shipped path whose sole positive test moved elsewhere is how a working route quietly
                // stops being one. It stays reachable because DescriptionOnlyValues is not an
                // AcceptableValueList, so the cascade declines it and the parse runs.
                //
                // The non-canonical spellings are the check. A token is stored as the VALUE's rendering,
                // not the fragment that produced it, so these must arrive as 0.5 / 1.5 / 2.5 and cycling
                // must land on what is shown. Drop that normalisation and the tokens keep their trailing
                // zeros while the widget reads back "1.5" — index -1, and the row snaps to the first
                // option on every other press. Visible on any machine; no decimal separator involved.
                choiceFile.Bind(
                    "Settings",
                    "ChoiceReconstructed",
                    1.5,
                    new ConfigDescription(
                        "A reconstructed choice; must show 0.5/1.5/2.5, not the spellings it was given.",
                        new DescriptionOnlyValues(typeof(double), "# Acceptable values: 0.50, 1.5, 2.50")
                    ),
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

                // Rejections that CoreLib's own constraint classes cannot produce on the RECONSTRUCTION
                // path — no supported T renders an unparseable token there, and its Clamp corrects an
                // off-set value at bind. (A blank MEMBER needs no subclass at all; that is
                // RefuseBlankInSet further down, and it lands one branch earlier.) Each needs DescriptionOnlyValues (below), which is also the
                // "a third party's own subclass, judged by whether its description matches" case
                // TryTokens documents and nothing else exercises. All four must stay read-only Info
                // rows AND log one line each naming why.
                choiceFile.Bind(
                    "Settings",
                    "RefuseEmptyToken",
                    "Alpha",
                    new ConfigDescription(
                        "A value set with a blank entry; must stay read-only.",
                        new DescriptionOnlyValues(typeof(string), "# Acceptable values: Alpha, , Gamma")
                    ),
                    clientScope
                );
                choiceFile.Bind(
                    "Settings",
                    "RefuseUnconvertible",
                    1,
                    new ConfigDescription(
                        "A value set with a token that is not an int; must stay read-only.",
                        new DescriptionOnlyValues(typeof(int), "# Acceptable values: 1, two, 3")
                    ),
                    clientScope
                );
                choiceFile.Bind(
                    "Settings",
                    "RefuseInvalid",
                    "Alpha",
                    new ConfigDescription(
                        "A value set whose own constraint rejects the values it prints; must stay read-only.",
                        new DescriptionOnlyValues(typeof(string), "# Acceptable values: Alpha, Beta", valid: false)
                    ),
                    clientScope
                );
                // The culture trap, reproduced deterministically: this is verbatim what a comma-decimal
                // machine's ToDescriptionString() produces for a set of (0.5, 5.0, 0.0). Every fragment
                // converts and every fragment is valid, so the per-token checks all pass. Held value
                // 0.5, which the split ate — caught by the held-value check.
                choiceFile.Bind(
                    "Settings",
                    "RefuseSplitValue",
                    0.5,
                    new ConfigDescription(
                        "A value set a decimal separator split into fragments; must stay read-only.",
                        new DescriptionOnlyValues(typeof(double), "# Acceptable values: 0,5, 5, 0")
                    ),
                    clientScope
                );
                // The same split with a held value the split left INTACT: "5" is among the fragments,
                // so the held-value check passes and only the duplicate check refuses it. Without that
                // check this row would offer 0/5/5/0, hide 0.5 entirely, and freeze on → because both
                // neighbours of index 1 render as the value it already holds.
                choiceFile.Bind(
                    "Settings",
                    "RefuseSplitDuplicate",
                    5.0,
                    new ConfigDescription(
                        "The same split, held value intact — only the duplicate check catches it.",
                        new DescriptionOnlyValues(typeof(double), "# Acceptable values: 0,5, 5, 0")
                    ),
                    clientScope
                );
                // A blank value in a REAL AcceptableValueList. Reachable with stock CoreLib — its
                // constructor rejects only a zero-length array, never a blank element — so this is the
                // one rejection that needs no subclass, and the exact branch is the only thing between
                // it and a row that writes "" into a foreign mod's file.
                choiceFile.Bind(
                    "Settings",
                    "RefuseBlankInSet",
                    "Alpha",
                    new ConfigDescription(
                        "A real value list with a blank entry; must stay read-only.",
                        new AcceptableValueList<string>("Alpha", "  ", "Gamma")
                    ),
                    clientScope
                );
                // A constraint that throws where MSM asks it a question. Nothing else exercises the
                // per-entry guard in BuildSection, and that guard is the difference between losing this
                // row and losing every mod's settings at once: Populate() has no handler of its own.
                // Alone in its file, losing the row also loses the box — a section with no rows is
                // dropped whole — so the property this still demonstrates is that the SCREEN survives.
                BindThrowingFixture(
                    "TestThrowingConstraint",
                    "ThrowingConstraint",
                    "Alpha",
                    "A constraint whose description throws; this row and its box may be lost, nothing else.",
                    new DescriptionOnlyValues(typeof(string), "unused", throwOnDescribe: true),
                    clientScope,
                    info
                );
                // The other half. A real AcceptableValueList<float> whose description is unreadable: the
                // exact read never asks for one, so this must render as a normal Choice over 0.5/1.5/2.5
                // — and on every machine, which is what makes it the check the culture-dependent
                // ChoiceFloats cannot be. If it ever appears as a read-only Info row, the values are
                // coming from a description again.
                //
                // Read the two boxes together: the same unreadable description, opposite outcomes —
                // ThrowingConstraint's row is lost because the parse asks, this one survives because
                // the exact read does not. A change that made the exact path consult a description
                // again would collapse them onto the same result, which is the regression neither
                // reports alone.
                BindThrowingFixture(
                    "TestExactNoDescription",
                    "ChoiceExactNoDescription",
                    1.5f,
                    "A float choice whose description throws; must still be a working Choice.",
                    new ThrowingDescriptionValues(0.5f, 1.5f, 2.5f),
                    clientScope,
                    info
                );

                // Two sections in one file, both NAMED — unlike emptySectionFile below, whose first
                // section has none. Their casing ("Zebra" vs "alpha") is the actual point: sorting
                // them proves the display order is case-insensitive, which a same-case pair could not
                // show. Ordinary values, because what is under test is the heading and the order, not
                // the widgets.
                var groupFile = new ConfigFile("TestGroupFixtures/config.cfg", saveOnInit: true, info);
                groupFile.Bind("Zebra", "lastAlphabetically", true, new ConfigDescription("Its section sorts last; it must render second."));
                groupFile.Bind("alpha", "firstAlphabetically", true, new ConfigDescription("Its section sorts first despite the lower case."));
                groupFile.Bind("alpha", "second", 3, new ConfigDescription("A second row under the same heading."));

                // Exactly ONE section, which must render NO heading — the case the rule exists for,
                // and the one a two-section fixture cannot show.
                var singleGroupFile = new ConfigFile("TestSingleGroupFixtures/config.cfg", saveOnInit: true, info);
                singleGroupFile.Bind("OnlySection", "value", true, new ConfigDescription("Its file has one section; no heading may appear."));

                // A section with an EMPTY name — CoreLib's own encoding for every line it files before
                // a file's first [Header] — beside a named one. ADR-011 spends its longest decision on
                // this branch and nothing exercised it before this fixture: the unnamed row must
                // render with NO heading above it, while the named section still gets one — proving
                // both that the empty group is suppressed AND that it still counts toward the
                // two-group threshold that decides whether headings appear at all.
                var emptySectionFile = new ConfigFile("TestEmptySectionFixtures/config.cfg", saveOnInit: true, info);
                emptySectionFile.Bind(
                    "",
                    "beforeAnyHeader",
                    true,
                    new ConfigDescription("Filed under the empty section; must render with no heading above it.")
                );
                emptySectionFile.Bind("named", "afterHeader", true, new ConfigDescription("Its own named section; must get a heading."));
            }
        }

        /// <summary>Dev-only. Binds one fixture whose constraint refuses to describe itself, into a
        /// <see cref="ConfigFile"/> of its own.
        ///
        /// The file is created HERE rather than passed in, and that is the whole point of the helper:
        /// a caller never holds one, so a second throwing fixture cannot be put beside the first. That
        /// rule used to be an instruction to the reader, and the reader it was written for was me,
        /// after the failure it describes.
        ///
        /// Why it matters. ConfigEntryBase's constructor ends in `BoxedValue = defaultValue`, which goes
        /// through the Value setter and fires OnSettingChanged for any value differing from the field
        /// default — so a whole-file Save runs BEFORE Bind reaches `Entries[definition] = entry`. Alone
        /// in a file, that Save sees no entries and succeeds. Beside another throwing one it throws out
        /// of the constructor and the entry is never registered: no row, no log line, and a section with
        /// no rows is dropped whole. The symptom is a missing BOX, not a missing row.
        ///
        /// The catch is narrow and then checked, which is the difference between swallowing an expected
        /// throw and swallowing evidence. Bind's own trailing Save throws for the same reason and is
        /// harmless, because registration already happened — but TomlTypeConverter raises this same
        /// exception type for a missing converter, and the constructor case above raises it too. So the
        /// registration is verified afterwards rather than assumed, and its absence is reported loudly.
        /// Without that, the one-per-file rule would be enforced by a comment and hidden by the code
        /// under it.</summary>
        private static void BindThrowingFixture<T>(
            string folder,
            string key,
            T def,
            string description,
            AcceptableValueBase values,
            ConfigScope scope,
            LoadedMod info
        )
        {
            var file = new ConfigFile($"{folder}/config.cfg", saveOnInit: false, info);
            try
            {
                file.Bind("Settings", key, def, new ConfigDescription(description, values), scope);
            }
            catch (System.InvalidOperationException)
            {
                // Expected: the save asks this constraint for a description and it refuses.
            }
            if (!file.Entries.ContainsKey(new ConfigDefinition("Settings", key)))
                Debug.LogError(
                    $"[ModSettingsMenu] fixture '{key}' was not registered — its own file already held a throwing "
                        + "entry, so the constructor's save threw before Bind could record it. One per file."
                );
        }

        /// <summary>Dev-only, for the ChoiceEnum fixture above. Three members so cycling has somewhere
        /// to wrap, in an order no sort would produce — the tokens are Enum.GetNames, which is VALUE
        /// order (declaration order only while the values are implicit, as here), and a stray sort
        /// would be invisible against alphabetical members. Giving these explicit values would break
        /// the order the manual test expects.</summary>
        private enum TestChoice
        {
            Second,
            First,
            Third,
        }

        /// <summary>Dev-only, for the ChoiceFlags fixture. A combination has no member name, which is
        /// the state the Choice case's `break` exists for.</summary>
        [System.Flags]
        private enum TestFlags
        {
            None = 0,
            Alpha = 1,
            Beta = 2,
            Gamma = 4,
        }

        /// <summary>Dev-only. The only way to reach TryTokens' rejections, because CoreLib's own
        /// constraints cannot produce them: AcceptableValueList's constructor refuses an empty set, no
        /// supported T renders a blank or unparseable token, and its Clamp corrects an off-set value at
        /// bind — the ctor's own BoxedValue assignment runs it. Clamp is the identity here, so a bound
        /// value survives exactly as written and the set-level check has something to catch.
        ///
        /// It doubles as the case TryTokens reasons about but nothing else exercises: a third party's
        /// own AcceptableValueBase subclass, judged solely by whether its description happens to match
        /// the prefix this file looks for.</summary>
        private sealed class DescriptionOnlyValues : AcceptableValueBase
        {
            private readonly string _line;
            private readonly bool _valid;
            private readonly bool _throwOnDescribe;

            public DescriptionOnlyValues(System.Type valueType, string line, bool valid = true, bool throwOnDescribe = false)
                : base(valueType)
            {
                _line = line;
                _valid = valid;
                _throwOnDescribe = throwOnDescribe;
            }

            public override object Clamp(object value) => value;

            public override bool IsValid(object value) => _valid;

            public override string ToDescriptionString() =>
                _throwOnDescribe
                    ? throw new System.InvalidOperationException("fixture: a third party's constraint throwing where MSM asks it a question")
                    : _line;
        }

        /// <summary>Dev-only. A REAL value list whose description is unreadable — the proof that the
        /// exact path never asks for one.
        ///
        /// It exists because the property it proves is invisible on most machines: the culture trap the
        /// exact read removes only appears where ToDescriptionString() renders a decimal comma, so on a
        /// dot-decimal host the parse and the exact read produce identical tokens and a walkthrough
        /// cannot tell them apart. Making the description throw replaces that dependency on the host
        /// with a deterministic one: if this renders as a Choice over its values, the description was
        /// not read, on any machine.
        ///
        /// Subclassing works because CoreLib seals neither the class nor the method, and it changes
        /// nothing the exact path touches — the base constructor fills AcceptableValues as usual.</summary>
        private sealed class ThrowingDescriptionValues : AcceptableValueList<float>
        {
            public ThrowingDescriptionValues(params float[] values)
                : base(values) { }

            public override string ToDescriptionString() =>
                throw new System.InvalidOperationException("fixture: the description must not be read where the values can be");
        }

        public void Init()
        {
            Debug.Log("[ModSettingsMenu] Mod initialized.");
            NamingDiagnostics = BindNamingDiagnostics();
            var section = ModSettings.Section(this).Toggle(out ShowForeignConfigs, "showForeignConfigs", true);
            if (DevFlags.Is("TestFixtures"))
                AddDeclaredFixtures(section);
            section.Build();
        }

        /// <summary>MSM-28's diagnostic switch, bound directly on MSM's own ConfigFile rather than
        /// through a SectionBuilder <c>.Toggle()</c> call. Every SectionBuilder widget adds a row to
        /// the menu, and this one is not for a player: it is aimed at a FOREIGN mod author who never
        /// took this mod's own dependency and so never opens this screen's box at all — for them, a
        /// line in a .cfg file they are already editing is the only surface that reaches them.
        /// ConfigStore.ForMod is keyed by modId and caches one ConfigFile per consumer, so this binds
        /// into the exact same file the ModSettings.Section(this) call right after it hands
        /// SectionBuilder — the bool just never becomes a SettingDef, and it shares MSM's own
        /// [Settings] section with showForeignConfigs.
        ///
        /// Guarded the way SectionBuilder.BindGuarded guards every widget bind, and for the same
        /// reason: ConfigFile.Bind ends in a filesystem write (Save()) with no exception handling of
        /// its own, and this call runs BEFORE ModSettings.Section below it. Unguarded, a Wine
        /// filesystem fault here would take the whole of Init() down with it — including MSM's own
        /// "Mod settings" box — for a setting nobody but a mod author reading the file will ever
        /// look at.</summary>
        private SettingHandle<bool> BindNamingDiagnostics()
        {
            var info = ((IMod)this).GetModInfo();
            try
            {
                var entry = ConfigStore
                    .ForMod(this, info.Metadata.name)
                    .Bind(
                        "Settings",
                        "reportForeignNamingStages",
                        false,
                        new ConfigDescription(
                            "For a foreign mod author, not a player: with this on, opening the settings menu logs one line per "
                                + "detected mod's config file, naming how many of its rows resolved under Mod Settings Menu's own "
                                + "term schema, how many under General Mod Config Menu's, and how many fell back to the raw key, "
                                + "plus the exact terms tried for that section's first row. Off by default, because it doubles "
                                + "every label lookup on a path already tuned to open without a stall."
                        )
                    );
                return new SettingHandle<bool>(entry);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModSettingsMenu] Could not bind the naming-diagnostics switch; it stays off for this session: {ex}");
                return new SettingHandle<bool>(false);
            }
        }

        // Dev-only fixtures for the DECLARED path — the counterpart to the raw ConfigFile
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
        private static void AddDeclaredFixtures(SectionBuilder section)
        {
            // The headings are placed BETWEEN the other fixtures rather than appended, because a
            // heading that sits alone at the end would exercise the rendering while showing nothing
            // about the thing it is for — grouping the rows beneath it. Like every other fixture
            // here they carry no loc term, which is deliberate twice over: the raw key names the
            // heading's position on screen while walking the checks, and a missing term falling
            // back to the key rather than to a blank row is itself one of them.
            section.Label("testLabelChoice");
            // The only declared Choice anywhere, and the only exercise of SectionBuilder's token
            // rendering. A float T on purpose: that is the one case where going through ChoiceToken
            // differs from the ToString() this used to do, so it is the whole check. It must read
            // 1.5 and cycle 0.5 / 1.5 / 2.5 on every machine, and the stored .cfg value must be the
            // token shown — on a comma-decimal host the old code stored "1,5" and made the loc key
            // depend on the machine, which no consumer's yaml can be right for twice.
            section.Choice(out _, "testChoiceFloat", new[] { 0.5f, 1.5f, 2.5f }, 1.5f);
            // The heading with the most rows under it, so navigation has to step over it in both
            // directions with a real neighbour on each side — the case a heading at either end of
            // the box cannot produce.
            section.Label("testLabelLists");
            section.List(out _, "testListFreeText", new[] { "Alpha", "Beta", "Gamma" });
            // The only fixture with enough word boundaries inside ONE entry to tell a repeating word
            // jump from the character-by-character crawl it replaced. WithSpaces cannot: discovery's
            // own heuristic refuses a token of more than two words, so a held key reaches the start
            // of the line in two jumps and ends up there whether it jumped or crawled. That limit is
            // the discovery path's, not the API's, which is why this one is declared.
            //
            // Number words on purpose, so where the caret stopped is readable off the screen ("it
            // stopped before 'seven'") instead of being counted in characters; their unequal lengths
            // are what keeps a fixed-size step from passing as a word jump. The first entry is also
            // wider than the field at 62 characters — five more than the `Overlong` fixture the docs
            // already call far wider — so the same check covers the view following a JUMPING caret
            // rather than a stepping one. The second is meant to fit without scrolling and separate
            // the two; nothing here measures the field's character capacity, so that is an intent
            // the manual walk confirms, not a fact this comment can assert.
            section.List(out _, "testListWordJump", new[] { "one two three four five six seven eight nine ten eleven twelve", "alpha beta gamma delta" });
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
            section.Label("testLabelEdgeCases");
            // Reaches BindGuarded without needing a filesystem fault: binding one key twice with
            // different types trips ConfigFile.Bind's unchecked cast. The Slider must be logged and
            // left out, the Toggle and everything after must survive — and RequiresRestart() after a
            // failed declaration must refuse rather than mark the setting before it.
            section.Toggle(out _, "testDupKey", true).Slider(out _, "testDupKey", 0f, 10f, 5f, 1f).RequiresRestart();
            // MSM-29: a Slider whose own bounds are reversed (min > max) — a mistake made building
            // the ConfigDescription, in the CALLER, before BindGuarded is ever entered. Unguarded,
            // that throw used to leave IMod.Init() outright and take the whole section down with it:
            // this mod's own box vanished from exactly this kind of swap, which is how this fixture
            // came to exist. The check it makes possible: the row itself is absent, the Toggle
            // declared right after it still appears, and — the part nothing could show before the
            // guard existed — the box renders at all.
            section.Slider(out _, "testReversedRange", 10f, 0f, 5f, 1f);
            section.Toggle(out _, "testAfterReversedRange", true);
            // RequiresRestart() after a HEADING must be refused too, and for a different reason than
            // the failed declaration above: nothing went wrong here, the row simply holds no value
            // and could never trigger a restart. Accepting it would leave the setting the consumer
            // actually meant unflagged, with a restart demand attached to a row that can never
            // change. Expected outcome is a warning naming this key.
            section.Label("testLabelRestartGuard").RequiresRestart();
            // The heading declared LAST, so the trailing segment of the sort is empty and the box's
            // final row is a heading — the two positions the sorting helper handles without a
            // special case and which nothing else here would put it in.
            section.Label("testLabelTrailing");

            // A declared group: the heading renders like a Label, and everything after it binds into
            // [testGroup] rather than [Settings]. Verified in the .cfg, not on screen — the screen
            // shows the same thing either way, which is exactly why this needs checking in the file.
            section.Group("testGroup");
            section.Toggle(out _, "testGroupedToggle", true);
            // A plain Label INSIDE a group: it must render a heading and must NOT change the section
            // its neighbours bind into. The two declarations look alike on screen and differ here.
            section.Label("testLabelInsideGroup");
            section.Stepper(out _, "testGroupedStepper", 0, 10, 1);
            // A second group, so the first one's end is a real boundary rather than the box's end.
            section.Group("testGroupTwo");
            section.Toggle(out _, "testSecondGroupToggle", false);
            // A group name CoreLib refuses. The declaration must be dropped whole — no heading, and
            // the one setting after it must still bind into [testGroupTwo], not vanish.
            section.Group("bad[name]");
            section.Toggle(out _, "testAfterBadGroup", true);
            // A declared movedFrom whose source has never existed. Its whole purpose is to stay
            // SILENT: this is the state every consumer reaches one launch after a real rename, and a
            // "nothing to migrate" line here would print on every start for every player forever.
            // It also gives the migration a source to be tested against by hand (Task 5, step 6),
            // which a fixture cannot stage on its own — a rename is a change to the code.
            section.Group("testGroupMoved", movedFrom: "testGroupFormer");
            section.Toggle(out _, "testMovedToggle", true);
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
