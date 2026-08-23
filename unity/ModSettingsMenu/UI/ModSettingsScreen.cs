using System.Collections.Generic;
using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Own settings menu — the adapted vanilla UISettings prefab (this component swapped
    /// in for RadicalOptionsMenu). Layout follows CK's ControlMapper (ControlMappingMenu):
    /// nested LinearLayoutUIComponents. contentRoot stacks one instance per registered
    /// section; each section instance stacks [Header, Hint, Widgets-box] vertically, and the
    /// Widgets box (a LinearLayout with a 9-slice background) stacks that section's toggles —
    /// so a bordered frame wraps just the options, with the heading + hint above it. Each WIDGET
    /// ROW gets a WrapperUIComponent so its (composite Label+Value) layout measures it; Header and
    /// Hint are each a single, standalone PugText and deliberately do NOT — their own natural
    /// rendered height is already correct, and a same-GameObject WrapperUIComponent would only
    /// silently compete with it for LinearLayoutUIComponent's own
    /// GetComponent&lt;UIComponentMonoBehaviour&gt;() lookup, whichever wins depending on component
    /// order (a regression once found and fixed the hard way — don't reintroduce it). Toggles stay
    /// in menuOptions for keyboard navigation. GetCurrentWindowHeight returns the top layout's
    /// render height (feeds scroll).
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class ModSettingsScreen : RadicalMenu, IScrollable
    {
        public Transform contentRoot; // Options/Scroll — hosts the top LinearLayout
        public GameObject sectionTemplate; // inactive; SectionBox (Header + Hint + Widgets-box)
        public GameObject settingTemplate; // inactive widget row; has a SettingWidget + Label/Value
        public GameObject listTemplate; // inactive list-widget row; has a ListWidget + ListWidgetBox (wired in the Editor)

        internal const int RowPaddingPx = 6; // vertical breathing room added to each row's text height

        // Inter-item gaps (contentRoot=6, SectionTemplate=12) live on the prefab's LinearLayouts, not here.
        // Content position is owned by UIScrollWindow, not this component (no anchor constant).

        // Row height follows the rendered text (PugText.dimensions.height, in units) + padding,
        // like CK's ControlMapper (renderHeightPixels = 16 * height). Single-line rows stay
        // compact; multi-line labels/hints grow automatically. Fallback to one line if unmeasured.
        // Internal (not private): ListDetailScreen's item rows share this same formula.
        internal static int RowHeightPx(PugText pt)
        {
            float unitsHigh = (pt != null && pt.dimensions.height > 0f) ? pt.dimensions.height : 1f;
            return RowHeightPx(unitsHigh);
        }

        // Same convention for a pre-measured content height (units): list rows feed the item
        // container's rendered height here, so they share RowPaddingPx with the text-based rows.
        private static int RowHeightPx(float unitsHigh) => Mathf.RoundToInt(16f * unitsHigh) + RowPaddingPx;

        // A drill-in row measured from its FRAME instead of its text — the same 16-px-per-unit
        // convention, deliberately WITHOUT RowPaddingPx. The padding exists to give text-measured
        // rows breathing room around glyphs that end exactly at their bounds; a frame already
        // encloses that air, so adding it again would space the frames apart by a value nobody
        // chose. Shared by ListDetailItem and ListAddRow so the two cannot drift.
        internal static int FrameHeightPx(SpriteRenderer frame) => Mathf.RoundToInt(16f * frame.size.y);

        private UIScrollWindow _scroll;
        private LinearLayoutUIComponent _layout;
        private readonly List<GameObject> _sectionRoots = new List<GameObject>(); // rendered inner-to-outer after activation
        private readonly List<ListWidget> _listWidgets = new List<ListWidget>(); // single-line rows; height set in RenderContent from the preview's rendered text height

        // Rebuild on every open (Populate) — the vanilla PugTexts free their glyphs on disable
        // (freeResourcesOnDisable), so a once-only build shows empty on reopen. Populate builds the
        // structure + fills menuOptions; the layouts are rendered in RenderContent AFTER
        // base.Activate, because LinearLayout skips children while the hierarchy is inactive (their
        // heights would compute as 0).
        // Set true (in SettingWidget.Adjust, or ListDetailScreen.OnRowTextCommitted for a
        // RequiresRestart-flagged list) when a restart-required setting actually changes during this
        // menu visit; consumed on leave (Deactivate, pop=true only — see below) to raise CK's restart
        // prompt. Static: MenuInstance is a singleton (MenuPatch), so no per-instance plumbing from
        // the widgets is needed.
        //
        // Deliberately NOT reset in Activate(): opening the list drill-in pushes it on top via
        // MenuManager.PushMenu, which — since both screens have popsOtherActiveMenus=true (the
        // prefab default) — calls THIS screen's Deactivate(pop: FALSE) (merely covered, not left) and,
        // on the drill-in's own PopMenu() close, calls Activate() again to resume THIS screen. A flag
        // set while covered (a restart-required list edit made inside the drill-in) must survive that
        // round-trip; resetting here on every Activate() treated "resuming after a covered visit" the
        // same as "a genuinely fresh visit" and discarded it before the player ever actually left.
        internal static bool RestartPending;

        public override void Activate()
        {
            Populate();
            base.Activate();
            RenderContent();
        }

        // Leaving the Mod settings screen (RadicalMenu's deactivate/back hook). If a restart-required
        // setting changed this visit, mirror CK's own mods-changed flow: raise the vanilla restart popup.
        // pop=false means we are merely being covered by a child menu (the list drill-in) and will
        // resume — not actually leaving — so RestartPending must be left untouched for that case (see
        // the field's own comment above for why).
        public override void Deactivate(bool pop)
        {
            base.Deactivate(pop);
            if (pop && RestartPending)
            {
                RestartPending = false;
                // Defer the prompt OFF this Deactivate call stack. StartNewDisplaySequence pushes a
                // popup menu (Manager.menu.ShowPopUpMenu → PushMenu(POP_UP)); pushing it while we are
                // still inside the menu-stack pop that triggered this Deactivate re-enters the stack,
                // so the popup never pops and its Cancel/Yes buttons persist across every later menu.
                // ModSettingsMenuMod.Update shows it a few frames later, once the pop has settled —
                // the same reason CK's own restart flow uses Invoke("RestartToApplyModChanges", 0.1f).
                ModSettingsMenuMod.RequestRestartPrompt();
            }
        }

        // CK's exact "restart to apply mod changes" popup (Pug.Other ModChanged / RestartToApplyModChanges):
        // the shipped Menu/RestartToApplyModChanges term (localized in every language) with Cancel/Yes
        // buttons; Yes → Manager.platform.Restart() (CK's real relaunch). Reusing CK's popup + term + restart
        // means no own dialog, no own localization — identical look to the game's own mod-changed prompt.
        internal static void ShowRestartPrompt()
        {
            Manager.menu.centerPopUpText.StartNewDisplaySequence(
                "Menu/RestartToApplyModChanges",
                null,
                menuInputCooldown: true,
                0f,
                1.5f,
                useUnscaledTime: true,
                0f,
                1f,
                localize: true,
                TextManager.FontFace.boldMedium,
                delegate(PopupResponse response)
                {
                    if (response.IsConfirm)
                        Manager.platform.Restart();
                },
                new List<string> { "cancelDialogue", "yes" },
                10f,
                0.8f,
                0,
                20f
            );
        }

        // CK's own popup — the same call ShowRestartPrompt makes, so look and localization are the
        // game's. Fires from an ordinary input frame, so unlike the restart prompt it needs NO
        // deferral: that one is only requested from Deactivate — calling it there directly,
        // inside the menu-stack pop, would re-enter.
        private void ConfirmReset(ModSection section)
        {
            Manager.menu.centerPopUpText.StartNewDisplaySequence(
                "ModSettingsMenu-UI/ResetConfirm",
                // The mod's display name is a LITERAL, not a loc term, so localizePlaceholders must
                // be false below — otherwise CK looks the name up as a term and renders "<missing>".
                new string[] { section.DisplayName },
                menuInputCooldown: true,
                fadeTime: 0f,
                staticTime: 1.5f,
                useUnscaledTime: true,
                yPosition: 0f,
                textBackgroundAlpha: 1f,
                localize: true,
                fontFace: TextManager.FontFace.boldMedium,
                optionsCallback: delegate(PopupResponse response)
                {
                    if (!response.IsConfirm)
                        return;
                    // This delegate is invoked BY PopUpText.OptionPressed, which only starts its
                    // fade-out coroutine AFTER the callback returns, and its own caller only pops
                    // this popup off the menu stack once OptionPressed itself returns. An exception
                    // escaping from here would skip both: CK's shared popup stays the top menu and
                    // every menu (including this one) stops reacting, with no error shown. So the
                    // restore and the redraw are guarded SEPARATELY — a failure in one must not stop
                    // the other — and each failure is logged with the section's name, since a config
                    // write that throws partway (e.g. a failing ConfigFilesystem write) can leave
                    // that section's file on disk with only some entries reset.
                    try
                    {
                        if (SectionReset.ApplyAndCheckRestart(section))
                            RestartPending = true;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[ModSettingsMenu] reset of '{section.DisplayName}' failed partway; its config may be partly restored: {e}");
                    }
                    try
                    {
                        RefreshSection(section);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[ModSettingsMenu] redrawing '{section.DisplayName}' after reset failed: {e}");
                    }
                },
                options: new List<string> { "cancelDialogue", "yes" },
                minWidth: 10f,
                backgroundAlpha: 0.8f,
                priority: 0,
                textMaxWidth: 20f,
                localizePlaceholders: false
            );
        }

        // Redraw only the rows of the section that was reset. NOT Populate() — that destroys and
        // rebuilds every row and would discard the selection (and with it the hint bar's context).
        private void RefreshSection(ModSection section)
        {
            foreach (var option in menuOptions)
            {
                var row = option as ISectionRow;
                if (row != null && row.Section == section)
                    row.Refresh();
            }
        }

        // Pay the one-time first-enable cost (bundle asset load / shader-variant compile, ~1 s
        // under Wine) once at load instead of on the first open: build the real rows, then fire
        // the OnEnable cascade with a same-frame SetActive cycle. NOT RadicalMenu.Activate() — so
        // no HUD toggle, SFX, or menu-stack push; OnEnable runs synchronously inside SetActive(true),
        // and disabling in the same frame means no active frame is ever rendered (no flash).
        public void PreWarm()
        {
            Populate();
            gameObject.SetActive(true);
            gameObject.SetActive(false);
        }

        public void Populate()
        {
            _scroll = GetComponent<UIScrollWindow>();
            if (contentRoot == null || settingTemplate == null || sectionTemplate == null)
            {
                Debug.LogWarning("[ModSettingsMenu] menu prefab not wired (contentRoot/settingTemplate/sectionTemplate) — menu stays empty.");
                return;
            }
            RenderTitle();
            DeactivateTemplates();

            // contentRoot stacks one instance per section via its (prefab-authored) vertical
            // LinearLayout; autoPositioning=0 means RadicalMenu no longer positions options itself.
            _layout = FindLayout(contentRoot.gameObject);

            // Detach old rows BEFORE Destroy (which is deferred to end-of-frame): otherwise on a
            // reopen the still-present old sections are counted by the layout this frame and push
            // the freshly-built ones off-screen.
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                var old = contentRoot.GetChild(i).gameObject;
                // Each section root nests several PugTexts (header, hint, every widget's label/value
                // text) that all pool their glyph SpriteRenderers (usePooledResources). Destroying the
                // root without releasing them first leaks pooled glyphs on every reopen of this screen
                // (reproduced: after several open/edit/reopen cycles, ALL rows across every section
                // rendered text-less). GetComponentsInChildren covers every widget kind uniformly
                // instead of naming each type's text fields.
                foreach (var text in old.GetComponentsInChildren<PugText>(includeInactive: true))
                    text.Clear();
                old.transform.SetParent(null, worldPositionStays: false);
                Object.Destroy(old);
            }
            menuOptions.Clear();
            _sectionRoots.Clear();
            _listWidgets.Clear();

            // Boxes render alphabetically by DisplayName — a stable, findable order regardless of mod
            // load/registration order. Sort a LOCAL copy so the registry keeps its insertion order.
            // Options WITHIN a box keep declaration order (the consumer's author intent).
            var sortedSections = new List<ModSection>(ModSettings.Sections);
            // GMCM-style generic discovery: fold in every foreign CoreLib config, unless the user
            // turned it off via MSM's own master toggle (null before Init -> default on).
            bool showForeign = ModSettingsMenuMod.ShowForeignConfigs == null || ModSettingsMenuMod.ShowForeignConfigs.Value;
            if (showForeign)
                sortedSections.AddRange(ForeignConfigDiscovery.Discover());
            sortedSections.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));

            foreach (var section in sortedSections)
            {
                var sGo = BuildSection(section);
                _sectionRoots.Add(sGo);
                var box = sGo.GetComponent<SectionBox>();
                var container = (box != null && box.widgetContainer != null) ? box.widgetContainer : sGo.transform;

                foreach (var def in OrderedSettings(section))
                {
                    if (def.Kind == SettingKind.List)
                    {
                        if (listTemplate == null)
                        {
                            Debug.LogWarning($"[ModSettingsMenu] List setting '{def.Key}' but listTemplate is unwired — rendering as an inert row.");
                        }
                        else
                        {
                            var lGo = Object.Instantiate(listTemplate, container);
                            lGo.SetActive(true);
                            lGo.name = "List " + def.Key;
                            var lw = lGo.GetComponent<ListWidget>();
                            lw.Bind(def, section);
                            lw.SetParentMenu(this);
                            // Row height is set in RenderContent (SetRowHeight(RowHeightPx(RenderAndMeasure)))
                            // after activation, like the normal rows — it depends on the preview's rendered
                            // single-line text height, not the item count.
                            menuOptions.Add(lw);
                            _listWidgets.Add(lw);
                            continue;
                        }
                    }
                    var wGo = Object.Instantiate(settingTemplate, container); // nest INTO the box
                    wGo.SetActive(true);
                    wGo.name = def.Kind + " " + def.Key;
                    var widget = wGo.GetComponent<SettingWidget>();
                    widget.Bind(def, section); // renders label/value → dimensions available
                    widget.SetParentMenu(this);
                    // The template's WrapperUIComponent lets the box layout measure this row;
                    // only its (content-adaptive) height is set here.
                    //
                    // Measure BOTH columns. The label is single-line by nature, but the value is
                    // whatever a foreign mod stored — and a long string wraps, so a row sized from
                    // the label alone leaves the extra lines hanging over the row below it. Only a
                    // read-only Info row can reach that state (every other kind renders a short,
                    // generated value), which is why it stayed hidden: until the list-detection
                    // heuristic was sharpened, prose with a comma was misread as a list and drawn as
                    // a compact preview instead.
                    SetRowHeight(wGo, Mathf.Max(RowHeightPx(widget.labelText), RowHeightPx(widget.valueText)));
                    menuOptions.Add(widget);
                }
            }

            if (_scroll != null)
            {
                _scroll.scrollingContent = contentRoot;
                _scroll.ResetScroll();
            }
        }

        // Render section content inner-to-outer AFTER base.Activate (the hierarchy is active now, so
        // LinearLayout counts the rows + computes real heights): each box sizes its 9-slice
        // background to its toggles, each section stacks [heading, hint, box], then the top layout
        // stacks the sections.
        internal void RenderContent()
        {
            // List rows first: render each row's preview and size it via the SAME path as the normal
            // rows — SetRowHeight(RowHeightPx(..)) — so the boxes below measure them and nothing
            // overflows. (A list row is single-line now; RenderAndMeasure just re-renders its preview.)
            foreach (var lw in _listWidgets)
                if (lw != null)
                    SetRowHeight(lw.gameObject, RowHeightPx(lw.RenderAndMeasure()));

            foreach (var sGo in _sectionRoots)
            {
                if (sGo == null)
                    continue;
                // Inner layouts first (box, and the heading sub-group if the prefab has one), so the
                // section-root layout measures their real heights; then the section root, then the top.
                ContainerOf(sGo).GetComponent<LinearLayoutUIComponent>()?.RenderUIComponent(force: true);
                sGo.transform.Find("Heading")?.GetComponent<LinearLayoutUIComponent>()?.RenderUIComponent(force: true);
                sGo.GetComponent<LinearLayoutUIComponent>()?.RenderUIComponent(force: true);
            }
            _layout?.RenderUIComponent(force: true);
            // contentRoot's position is owned by UIScrollWindow (LateUpdate → SetScrollablePosition),
            // so no manual anchoring here — an anchor set now is overwritten the same frame.
        }

        // Keyboard / controller navigation moves the selection through menuOptions, but the base
        // RadicalMenu never scrolls the viewport to follow it — every vanilla scrollable menu wires
        // that itself (ControlMapper's ActionMappingSelected; the chooseCharacter/selectWorld option
        // OnSelected overrides). RadicalMenu.SelectOptionIndex calls this hook right after the
        // freshly-selected option's OnSelected — keep that row on screen.
        protected override void OnSelectedOptionChanged()
        {
            base.OnSelectedOptionChanged();
            ScrollSelectedIntoView();
        }

        // CK asks the TOP menu for its footer prompts every frame from MenuManager.LateUpdate
        // (UpdateHelperButtons). Note it calls GetHelpButtonsToShow() UNCONDITIONALLY as soon as a
        // row is selected — UseCustomHelpButtons only decides the selection-less case — so the
        // override below is what actually drives the bar.
        public override bool UseCustomHelpButtons => true;

        // Reused across calls instead of allocating a fresh List every time: CK polls this at
        // least once per frame (twice once a row is selected — see the comment above
        // UseCustomHelpButtons), same as CK's own menus keep a pre-built list in a field rather
        // than allocate per call.
        private readonly List<MenuHelperButtons.HelpButtonTypes> _helpButtons = new List<MenuHelperButtons.HelpButtonTypes>();

        public override List<MenuHelperButtons.HelpButtonTypes> GetHelpButtonsToShow()
        {
            var buttons = base.GetHelpButtonsToShow();
            if (!SectionReset.CanReset(SelectedSection()))
                return buttons;
            // NEVER mutate the base result in place: RadicalMenu.GetHelpButtonsToShow returns
            // Manager.menu.defaultHelpButtons — the SHARED list instance — so appending to IT
            // would permanently add a reset prompt to every vanilla menu in the game. Copy its
            // entries into our own reused field instead, then add.
            //
            // RESET_DEFAULTS is a fully-built but unused vanilla slot: the Global Objects (Main
            // Manager) prefab wires its root, its per-platform glyph (keyboard "R", the Y-position
            // face button) and a PugText carrying the shipped, localized term "Menu/Reset" — and no
            // vanilla menu ever requests it. So this costs no prefab work and no own localization.
            _helpButtons.Clear();
            _helpButtons.AddRange(buttons);
            _helpButtons.Add(MenuHelperButtons.HelpButtonTypes.RESET_DEFAULTS);
            return _helpButtons;
        }

        // The section the currently selected row belongs to, or null when nothing selectable is
        // focused. GetSelectedMenuOption is RadicalMenu's own accessor (it is what CK's
        // UpdateHelperButtons uses too).
        private ModSection SelectedSection()
        {
            var row = GetSelectedMenuOption() as ISectionRow;
            return row == null ? null : row.Section;
        }

        // Rewired action id for OpenProfile. Chosen over MenuSecondaryActivate (221) because it is
        // the action that actually sits on the button CK's RESET_DEFAULTS hint glyph depicts: an
        // in-game probe showed Triangle reports 220 + 223 while Square reports 221 + 222, and the
        // hint's sprite is the Y/Triangle one it shares with the openProfile hint. 223 is also the
        // only candidate no vanilla menu consumes — its accessor IsOpenProfileButtonDown() has zero
        // callers in Pug.Other, whereas 222 drives the join-game session refresh and 220/221 are
        // dispatched through InputReceiver.OnOptions()/OnAlternate().
        private const int ResetActionId = 223;

        // Poll the reset input. Gated on being the TOP menu, which also covers the two cases that
        // must not react: the list drill-in is open (a different menu is on top), and the
        // confirmation popup is open (PushMenu(POP_UP) puts it on top).
        //
        // MUST be Update(), never LateUpdate(): RadicalMenu declares a PRIVATE LateUpdate(), so a
        // LateUpdate here would hide it and Unity would stop calling the base one — silently
        // breaking CK's own per-frame menu work. Neither RadicalMenu nor UIComponentMonoBehaviour
        // declares Update(), so this name is free.
        private void Update()
        {
            if (Manager.menu == null || Manager.menu.GetTopMenu() != (RadicalMenu)this)
                return;
            var section = SelectedSection();
            if (!SectionReset.CanReset(section))
                return;
            bool keyboard = Input.GetKeyDown(KeyCode.R);
            bool gamepad = Manager.input != null && Manager.input.GetButtonDown(ResetActionId);
            if (!keyboard && !gamepad)
                return;
            ConfirmReset(section);
        }

        // Scroll the viewport so the selected row follows keyboard / controller navigation.
        //
        // Positions are measured in contentRoot's (the scroll root's) local space: sum localPosition.y
        // up the parent chain (row -> widgets box -> section -> contentRoot), because MSM's rows are
        // nested — unlike CK's own 1-level scrollable menus, which pass transform.localPosition.y raw.
        // The row's WrapperUIComponent pivot decides where that origin sits (MiddleLeft = centre,
        // TopLeft = top edge already), so it is normalised to a top edge first, mirroring CK's
        // UIComponentMonoBehaviour.ScrollIntoView pivot correction.
        //
        // Two cases, because CK's MoveScrollToIncludePosition only handles elements that FIT the
        // window (it keeps a point inside [-windowHeight + padding, -padding]; with padding past
        // windowHeight/2 that band inverts and the scroll overshoots):
        //   * Row fits          -> include it fully (centre, half-height padding — CK's convention).
        //   * Row taller than    -> can't be included fully; pin its TOP just under the window top so
        //     the viewport         as much of it as fits shows, instead of overshooting off-screen.
        //                          Defensive: every current row (toggle + compact list) is single-line
        //                          and fits, but a wrapped multi-line label could still exceed it.
        private void ScrollSelectedIntoView()
        {
            if (_scroll == null || contentRoot == null)
                return;
            if (selectedIndex < 0 || selectedIndex >= menuOptions.Count)
                return;
            var option = menuOptions[selectedIndex];
            if (option == null)
                return;

            // Selecting by mouse hover must not scroll the page — CK gates its own ScrollIntoView the
            // same way (ScrollIntoViewIfNotUsingMouse). Keyboard / controller nav leaves this false.
            if (Manager.input.SystemIsUsingMouse())
                return;

            float origin = 0f;
            for (Transform t = option.transform; t != null && t != contentRoot; t = t.parent)
                origin += t.localPosition.y;

            var wrap = option.GetComponent<WrapperUIComponent>();
            float height = wrap != null ? wrap.GetUIComponentRenderHeight() : 1f;
            bool topPivot = wrap != null && wrap.GetUIComponentPivotPosition() == WrapperUIComponent.PivotPosition.TopLeft;
            float topEdge = topPivot ? origin : origin + height / 2f;

            if (height <= _scroll.windowHeight)
            {
                float center = topEdge - height / 2f;
                _scroll.MoveScrollToIncludePosition(center, height / 2f);
            }
            else
            {
                const float TopMarginUnits = 0.25f;
                float delta = -TopMarginUnits - (contentRoot.localPosition.y + topEdge);
                _scroll.MoveScroll(delta);
            }
        }

        private static Transform ContainerOf(GameObject sGo)
        {
            var box = sGo.GetComponent<SectionBox>();
            return (box != null && box.widgetContainer != null) ? box.widgetContainer : sGo.transform;
        }

        // Order a section's options per its OptionSort: AsDeclared keeps the builder-chain order;
        // ByKey/ByLabel sort a LOCAL copy by the raw key / the localized label (Loc.T(term,key) — so
        // ByLabel follows the active language). The section's Settings list itself stays untouched.
        private static List<SettingDef> OrderedSettings(ModSection section)
        {
            var list = new List<SettingDef>(section.Settings);
            switch (section.OptionSort)
            {
                case OptionSort.ByKey:
                    list.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.OrdinalIgnoreCase));
                    break;
                case OptionSort.ByLabel:
                    list.Sort((a, b) => string.Compare(Loc.T(a.Term, a.Key), Loc.T(b.Term, b.Key), System.StringComparison.OrdinalIgnoreCase));
                    break;
            }
            return list;
        }

        // Build one section (Option A): instantiate the sectionTemplate and render its heading
        // (DisplayName) plus an optional hint ABOVE a bordered box. The section root stacks
        // [Header, Hint, Widgets] vertically; the caller nests the toggles into the Widgets box,
        // whose LinearLayout carries a 9-slice background (32x32_itemui_border) that auto-sizes
        // to them. Header (bright) and hint (dimmed) are distinct prefab-styled PugTexts, so
        // they render differently. Returns the section-root GameObject.
        private GameObject BuildSection(ModSection section)
        {
            var sGo = Object.Instantiate(sectionTemplate, contentRoot);
            sGo.SetActive(true);
            sGo.name = "Section " + section.ModId;
            FindLayout(sGo); // prefab-authored vertical layout: stacks heading + hint + box

            var box = sGo.GetComponent<SectionBox>();
            if (box != null && box.header != null)
            {
                // Auto-detected mods get a marker so their raw keys / inferred widgets read as
                // "discovered", not author-curated.
                string heading = section.Foreign ? section.DisplayName + " " + Loc.T("ModSettingsMenu-UI/AutoDetected") : section.DisplayName;
                box.header.RenderPlain(heading);
            }
            if (box != null && box.hint != null)
            {
                // Hint is hidden unless the section declares one; the layout skips inactive
                // children, so hint-less sections collapse.
                bool hasHint = !string.IsNullOrEmpty(section.HintText);
                box.hint.gameObject.SetActive(hasHint);
                if (hasHint)
                    box.hint.RenderPlain(Loc.T(section.HintTerm, section.HintText));
            }
            return sGo;
        }

        // Find the GameObject's (prefab-authored) vertical LinearLayout. Its horizontal flag +
        // inter-item gap live in the prefab now, so the code only locates it.
        private static LinearLayoutUIComponent FindLayout(GameObject go)
        {
            var l = go.GetComponent<LinearLayoutUIComponent>();
            if (l == null)
                Debug.LogWarning($"[ModSettingsMenu] '{go.name}' has no LinearLayoutUIComponent (expected in the prefab).");
            return l;
        }

        // Set a row's content-adaptive height on its (prefab-authored) WrapperUIComponent, which
        // lets the parent LinearLayout measure + stack it.
        private static void SetRowHeight(GameObject go, int px)
        {
            var wrap = go.GetComponent<WrapperUIComponent>();
            if (wrap == null)
            {
                Debug.LogWarning($"[ModSettingsMenu] '{go.name}' has no WrapperUIComponent (expected in the prefab).");
                return;
            }
            wrap.renderHeightPixels = px;
        }

        // Vanilla RadicalOptionsMenu rendered the title; we removed it in the swap.
        private void RenderTitle()
        {
            foreach (var path in new[] { "Title/Title bigtext", "Title/Title bigtext shadow" })
            {
                var t = transform.Find(path);
                if (t != null)
                    t.GetComponent<PugText>().RenderPlain(Loc.T("ModSettingsMenu-UI/Title"));
            }
        }

        // Templates under WidgetTemplates (SectionTemplate, SettingTemplate, ListTemplate) are
        // instantiation sources only — never rendered. Force them inactive at setup so a stray
        // Editor activation can't leak a phantom row/section into the menu. Instantiate works
        // fine on inactive templates; the clones are SetActive(true).
        private void DeactivateTemplates()
        {
            var templates = transform.Find("WidgetTemplates");
            if (templates == null)
                return;
            for (int i = 0; i < templates.childCount; i++)
                templates.GetChild(i).gameObject.SetActive(false);
        }

        // IScrollable — window height comes from the layout (basis for scroll clipping, #3).
        public void UpdateContainingElements(float scroll) { }

        public bool IsBottomElementSelected() => false;

        public bool IsTopElementSelected() => false;

        public float GetCurrentWindowHeight() => _layout != null ? _layout.GetUIComponentRenderHeight() : 0f;
    }
}
