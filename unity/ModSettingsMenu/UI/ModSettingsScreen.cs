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
    /// so a bordered frame wraps just the options, with the heading + hint above it. Each row
    /// gets a WrapperUIComponent so its layout measures it; toggles stay in menuOptions for
    /// keyboard navigation. GetCurrentWindowHeight returns the top layout's render height (feeds scroll).
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class ModSettingsScreen : RadicalMenu, IScrollable
    {
        public Transform contentRoot;      // Options/Scroll — hosts the top LinearLayout
        public GameObject sectionTemplate; // inactive; SectionBox (Header + Hint + Widgets-box)
        public GameObject toggleTemplate;  // inactive widget row; has a SettingWidget + Label/Value (serialized name kept)
        public GameObject listTemplate;    // inactive list-widget row; has a ListWidget + ListWidgetBox (wired in the Editor)

        private const int RowPaddingPx = 6;      // vertical breathing room added to each row's text height
        // Inter-item gaps (contentRoot=6, SectionTemplate=12) live on the prefab's LinearLayouts, not here.
        // Content position is owned by UIScrollWindow, not this component (no anchor constant).

        // Row height follows the rendered text (PugText.dimensions.height, in units) + padding,
        // like CK's ControlMapper (renderHeightPixels = 16 * height). Single-line rows stay
        // compact; multi-line labels/hints grow automatically. Fallback to one line if unmeasured.
        private static int RowHeightPx(PugText pt)
        {
            float unitsHigh = (pt != null && pt.dimensions.height > 0f) ? pt.dimensions.height : 1f;
            return Mathf.RoundToInt(16f * unitsHigh) + RowPaddingPx;
        }

        private UIScrollWindow _scroll;
        private LinearLayoutUIComponent _layout;
        private readonly List<GameObject> _sectionRoots = new List<GameObject>();   // rendered inner-to-outer after activation
        private readonly List<ListWidget> _listWidgets = new List<ListWidget>();    // sized in RenderContent (rows grow to fit their items)

        // Rebuild on every open (Populate) — the vanilla PugTexts free their glyphs on disable
        // (freeResourcesOnDisable), so a once-only build shows empty on reopen. Populate builds the
        // structure + fills menuOptions; the layouts are rendered in RenderContent AFTER
        // base.Activate, because LinearLayout skips children while the hierarchy is inactive (their
        // heights would compute as 0).
        // Set true (in SettingWidget.Adjust) when a RequiresRestart-flagged setting actually changes
        // during this menu visit; reset on open (Activate); consumed on leave (Deactivate) to raise
        // CK's restart prompt. Static: MenuInstance is a singleton (MenuPatch), so no per-instance
        // plumbing from the widgets is needed.
        internal static bool RestartPending;

        public override void Activate()
        {
            RestartPending = false;   // fresh visit — only changes made from now on count
            Populate();
            base.Activate();
            RenderContent();
        }

        // Leaving the Mod Settings screen (RadicalMenu's deactivate/back hook). If a restart-required
        // setting changed this visit, mirror CK's own mods-changed flow: raise the vanilla restart popup.
        public override void Deactivate(bool pop)
        {
            base.Deactivate(pop);
            if (RestartPending)
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
                "Menu/RestartToApplyModChanges", null, menuInputCooldown: true, 0f, 1.5f,
                useUnscaledTime: true, 0f, 1f, localize: true, TextManager.FontFace.boldMedium,
                delegate (PopupResponse response)
                {
                    if (response.IsConfirm) Manager.platform.Restart();
                },
                new List<string> { "cancelDialogue", "yes" }, 10f, 0.8f, 0, 20f);
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
            if (contentRoot == null || toggleTemplate == null || sectionTemplate == null)
            {
                Debug.LogWarning("[ModSettingsMenu] menu prefab not wired (contentRoot/toggleTemplate/sectionTemplate) — menu stays empty.");
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
                    if (def.Kind == SettingKind.List && listTemplate != null)
                    {
                        var lGo = Object.Instantiate(listTemplate, container);
                        lGo.SetActive(true);
                        lGo.name = "List " + def.Key;
                        var lw = lGo.GetComponent<ListWidget>();
                        lw.Bind(def, this);
                        lw.SetParentMenu(this);
                        var lbox = lGo.GetComponent<ListWidgetBox>();
                        SetRowHeight(lGo, RowHeightPx(lbox != null ? lbox.label : null));
                        menuOptions.Add(lw);
                        _listWidgets.Add(lw);
                        continue;
                    }
                    var wGo = Object.Instantiate(toggleTemplate, container);   // nest INTO the box
                    wGo.SetActive(true);
                    wGo.name = def.Kind + " " + def.Key;
                    var widget = wGo.GetComponent<SettingWidget>();
                    widget.Bind(def);            // renders label/value → dimensions available
                    widget.SetParentMenu(this);
                    // The template's WrapperUIComponent lets the box layout measure this row;
                    // only its (content-adaptive) height is set here.
                    SetRowHeight(wGo, RowHeightPx(widget.labelText));
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
            // List rows first: render each item container and grow the row to fit — so the section
            // boxes below measure the grown rows and nothing overflows onto neighbours.
            foreach (var lw in _listWidgets)
                if (lw != null) lw.RenderItems();

            foreach (var sGo in _sectionRoots)
            {
                if (sGo == null) continue;
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

        // Re-render all section layouts now (used when a ListWidget toggle changes a row's height
        // mid-menu, so the boxes and the scroll window resize without a full reopen).
        public void RefreshLayout() => RenderContent();

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
            FindLayout(sGo);   // prefab-authored vertical layout: stacks heading + hint + box

            var box = sGo.GetComponent<SectionBox>();
            if (box != null && box.header != null)
            {
                // Auto-detected mods get a marker so their raw keys / inferred widgets read as
                // "discovered", not author-curated.
                string heading = section.Foreign
                    ? section.DisplayName + " " + Loc.T("ModSettingsMenu-UI/AutoDetected")
                    : section.DisplayName;
                RenderStatic(box.header, heading);
                SetRowHeight(box.header.gameObject, RowHeightPx(box.header));
            }
            if (box != null && box.hint != null)
            {
                // Hint is hidden unless the section declares one; the layout skips inactive
                // children, so hint-less sections collapse.
                bool hasHint = !string.IsNullOrEmpty(section.HintText);
                box.hint.gameObject.SetActive(hasHint);
                if (hasHint)
                {
                    RenderStatic(box.hint, Loc.T(section.HintTerm, section.HintText));
                    SetRowHeight(box.hint.gameObject, RowHeightPx(box.hint));
                }
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

        // Render a static (non-localized) string into a PugText. Colour + maskInteraction are NOT
        // set here: PugFont.Render paints every glyph from the (prefab-authored) style — its color
        // (bright header, dimmed hint) AND its maskInteraction (VisibleInsideMask, so glyphs clip to
        // the scroll viewport). localize=false renders the raw string instead of a loc term.
        private static void RenderStatic(PugText pt, string text)
        {
            if (pt == null) return;
            pt.localize = false;
            pt.Render(text, rewindEffectAnims: false, force: true);
        }

        // Vanilla RadicalOptionsMenu rendered the title; we removed it in the swap.
        private void RenderTitle()
        {
            foreach (var path in new[] { "Title/Title bigtext", "Title/Title bigtext shadow" })
            {
                var t = transform.Find(path);
                var pt = t != null ? t.GetComponent<PugText>() : null;
                if (pt != null) RenderStatic(pt, Loc.T("ModSettingsMenu-UI/Title"));
            }
        }

        // Templates under WidgetTemplates (SectionTemplate, ToggleTemplate, and future
        // Slider/Stepper ones) are instantiation sources only — never rendered. Force them
        // inactive at setup so a stray Editor activation can't leak a phantom row/section into
        // the menu. Instantiate works fine on inactive templates; the clones are SetActive(true).
        private void DeactivateTemplates()
        {
            var templates = transform.Find("WidgetTemplates");
            if (templates == null) return;
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
