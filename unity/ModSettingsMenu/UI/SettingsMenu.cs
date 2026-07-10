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
    public sealed class SettingsMenu : RadicalMenu, IScrollable
    {
        public Transform contentRoot;      // Options/Scroll — hosts the top LinearLayout
        public GameObject sectionTemplate; // inactive; SectionBox (Header + Hint + Widgets-box)
        public GameObject toggleTemplate;  // inactive; has a ToggleWidget + Label/Value

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

        // Rebuild on every open (Populate) — the vanilla PugTexts free their glyphs on disable
        // (freeResourcesOnDisable), so a once-only build shows empty on reopen. Populate builds the
        // structure + fills menuOptions; the layouts are rendered in RenderContent AFTER
        // base.Activate, because LinearLayout skips children while the hierarchy is inactive (their
        // heights would compute as 0).
        public override void Activate()
        {
            Populate();
            base.Activate();
            RenderContent();
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
                Debug.LogWarning("[ModSettingsMenu] SettingsMenu prefab not wired (contentRoot/toggleTemplate/sectionTemplate) — menu stays empty.");
                return;
            }
            RenderTitle();

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

            foreach (var section in ModSettings.Sections)
            {
                var sGo = BuildSection(section);
                _sectionRoots.Add(sGo);
                var box = sGo.GetComponent<SectionBox>();
                var container = (box != null && box.widgetContainer != null) ? box.widgetContainer : sGo.transform;

                foreach (var def in section.Settings)
                {
                    if (def.Kind != SettingKind.Toggle) continue;
                    var wGo = Object.Instantiate(toggleTemplate, container);   // nest INTO the box
                    wGo.SetActive(true);
                    wGo.name = "Toggle " + def.Key;
                    var toggle = wGo.GetComponent<ToggleWidget>();
                    toggle.Bind(def);            // renders label/value → dimensions available
                    toggle.SetParentMenu(this);
                    // The toggle template's WrapperUIComponent lets the box layout measure this row;
                    // only its (content-adaptive) height is set here.
                    SetRowHeight(wGo, RowHeightPx(toggle.labelText));
                    menuOptions.Add(toggle);
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
        private void RenderContent()
        {
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

        private static Transform ContainerOf(GameObject sGo)
        {
            var box = sGo.GetComponent<SectionBox>();
            return (box != null && box.widgetContainer != null) ? box.widgetContainer : sGo.transform;
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
                RenderStatic(box.header, section.DisplayName);
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
                    RenderStatic(box.hint, section.HintText);
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
                if (pt != null) RenderStatic(pt, "Mod Settings");
            }
        }

        // IScrollable — window height comes from the layout (basis for scroll clipping, #3).
        public void UpdateContainingElements(float scroll) { }
        public bool IsBottomElementSelected() => false;
        public bool IsTopElementSelected() => false;
        public float GetCurrentWindowHeight() => _layout != null ? _layout.GetUIComponentRenderHeight() : 0f;
    }
}
