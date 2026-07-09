using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Own settings menu — the adapted vanilla UISettings prefab (this component swapped
    /// in for RadicalOptionsMenu, + a UIScrollWindow + an inactive ToggleTemplate).
    /// Populate() runs in OnFirstOpened() (before ActivateTopMenu, after all consumer
    /// Inits) and stamps one ToggleWidget per registered Toggle setting; RadicalMenu's
    /// UpdatePosition() lays them out from menuEntryStartPositionY. The vanilla scroll
    /// viewport (SpriteMask) was disabled in the prefab — its sprite+material were
    /// unresolved (deadbeef) after the decompile import; toggles render unmasked.
    /// Section headers / boxes are a later polish step.
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class SettingsMenu : RadicalMenu, IScrollable
    {
        public Transform contentRoot;      // where toggle rows are stamped (Options/Scroll)
        public GameObject sectionTemplate; // reserved for Phase-2c section headers/boxes
        public GameObject toggleTemplate;  // inactive; has a ToggleWidget + Label/Value

        private UIScrollWindow _scroll;

        // Populate before ActivateTopMenu: PushMenu calls OnFirstOpened() first, then
        // ActivateTopMenu() — so menuOptions are filled before the menu positions +
        // activates them (which renders their PugTexts via OnParentMenuActivation).
        public override void OnFirstOpened()
        {
            base.OnFirstOpened();
            Populate();
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

            // Clear old content, then stamp toggles as menu options.
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
                    var toggle = wGo.GetComponent<ToggleWidget>();
                    toggle.Bind(def);
                    toggle.SetParentMenu(this);
                    menuOptions.Add(toggle);
                }
            }

            UpdatePosition(); // vanilla RadicalMenu option layout (menuEntryStartPositionY + spacing)
            if (_scroll != null)
            {
                _scroll.scrollingContent = contentRoot;
                _scroll.ResetScroll();
            }
        }

        // Cloned vanilla PugTexts carry a PugTextEffectMenuOption that NREs without a
        // menu-option context. DISABLE it — do NOT Destroy: PugText keeps a ref in its
        // effect list, so destroying leaves a dangling null → NRE in ManagedLateUpdate
        // (which also aborts glyph rendering → invisible). enabled=false is skipped safely.
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
                StripMenuEffects(pt.gameObject); // title PugText also carries the NRE-prone effect
                pt.localize = false;
                pt.Render("Mod Settings", rewindEffectAnims: false, force: true);
                pt.SetTempColor(pt.color, keepColorOnStart: true);
            }
        }

        // IScrollable — flat list is short; no custom scroll sizing yet.
        public void UpdateContainingElements(float scroll) { }
        public bool IsBottomElementSelected() => false;
        public bool IsTopElementSelected() => false;
        public float GetCurrentWindowHeight() => 0f;
    }
}
