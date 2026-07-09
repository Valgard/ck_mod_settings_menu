using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Own settings menu — the adapted vanilla UISettings prefab (this component swapped
    /// in for RadicalOptionsMenu). Layout follows CK's ControlMapper (ControlMappingMenu):
    /// RadicalMenu with autoPositioning=0 (its own menuOption layout off) + a
    /// LinearLayoutUIComponent on contentRoot that vertically stacks children. Each toggle
    /// gets a WrapperUIComponent so the layout measures it; the toggles stay in menuOptions
    /// for keyboard navigation. GetCurrentWindowHeight returns the layout's render height
    /// (feeds scroll). Section headers land under the same layout (Phase-2c).
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class SettingsMenu : RadicalMenu, IScrollable
    {
        public Transform contentRoot;      // Options/Scroll — hosts the LinearLayout
        public GameObject sectionTemplate; // reserved for Phase-2c section headers/boxes
        public GameObject toggleTemplate;  // inactive; has a ToggleWidget + Label/Value

        private const int ToggleHeightPx = 32; // ~2 units (matches menuEntryVirtualHeight)

        private UIScrollWindow _scroll;
        private LinearLayoutUIComponent _layout;

        public override void OnFirstOpened()
        {
            base.OnFirstOpened();
            Populate();
        }

        // Re-stack after activation (base.Activate re-renders the menu options); mirrors
        // ControlMappingMenu.Activate → RenderUIComponent.
        public override void Activate()
        {
            base.Activate();
            _layout?.RenderUIComponent(force: true);
        }

        public void Populate()
        {
            _scroll = GetComponent<UIScrollWindow>();
            if (contentRoot == null || toggleTemplate == null)
            {
                Debug.LogWarning("[ModSettingsMenu] SettingsMenu prefab not wired (contentRoot/toggleTemplate) — menu stays empty.");
                return;
            }
            RenderTitle();

            // contentRoot stacks its children via a vertical LinearLayout (like ControlMapper);
            // autoPositioning=0 means RadicalMenu no longer positions the options itself.
            _layout = contentRoot.GetComponent<LinearLayoutUIComponent>();
            if (_layout == null)
            {
                _layout = contentRoot.gameObject.AddComponent<LinearLayoutUIComponent>();
                _layout.horizontal = false;
                _layout.gapBetweenItems = 0;
            }

            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(contentRoot.GetChild(i).gameObject);
            menuOptions.Clear();

            foreach (var section in ModSettings.Sections)
            {
                foreach (var def in section.Settings)
                {
                    if (def.Kind != SettingKind.Toggle) continue;
                    var wGo = Object.Instantiate(toggleTemplate, contentRoot);
                    wGo.SetActive(true);
                    wGo.name = "Toggle " + def.Key;
                    StripMenuEffects(wGo);
                    // A WrapperUIComponent makes the LinearLayout measure + stack this row.
                    var wrap = wGo.GetComponent<WrapperUIComponent>() ?? wGo.AddComponent<WrapperUIComponent>();
                    wrap.renderHeightPixels = ToggleHeightPx;
                    var toggle = wGo.GetComponent<ToggleWidget>();
                    toggle.Bind(def);
                    toggle.SetParentMenu(this);
                    menuOptions.Add(toggle);
                }
            }

            _layout.RenderUIComponent(force: true); // stack the rows vertically
            if (_scroll != null)
            {
                _scroll.scrollingContent = contentRoot;
                _scroll.ResetScroll();
            }
        }

        // Cloned vanilla PugTexts carry a PugTextEffectMenuOption that NREs without a
        // menu-option context. DISABLE it — do NOT Destroy: PugText keeps a ref in its
        // effect list, so destroying leaves a dangling null → NRE in ManagedLateUpdate.
        private static void StripMenuEffects(GameObject go)
        {
            foreach (var fx in go.GetComponentsInChildren<PugTextEffectMenuOption>(true))
                fx.enabled = false;
        }

        // Vanilla RadicalOptionsMenu rendered the title; we removed it in the swap.
        private void RenderTitle()
        {
            foreach (var path in new[] { "Title/Title bigtext", "Title/Title bigtext shadow" })
            {
                var t = transform.Find(path);
                var pt = t != null ? t.GetComponent<PugText>() : null;
                if (pt == null) continue;
                StripMenuEffects(pt.gameObject);
                pt.localize = false;
                pt.Render("Mod Settings", rewindEffectAnims: false, force: true);
                pt.SetTempColor(pt.color, keepColorOnStart: true);
            }
        }

        // IScrollable — window height comes from the layout (basis for scroll clipping, #3).
        public void UpdateContainingElements(float scroll) { }
        public bool IsBottomElementSelected() => false;
        public bool IsTopElementSelected() => false;
        public float GetCurrentWindowHeight() => _layout != null ? _layout.GetUIComponentRenderHeight() : 0f;
    }
}
