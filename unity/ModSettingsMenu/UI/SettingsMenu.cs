using ModSettingsMenu.Settings;
using UnityEngine;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Own settings menu (GMCM-style). The Editor prefab carries this component +
    /// a UIScrollWindow, a LinearLayoutUIComponent content host (contentRoot), and
    /// two inactive templates (sectionTemplate with a SectionBox, toggleTemplate
    /// with a ToggleWidget). Populate() stamps one box per registered section and a
    /// toggle per Toggle setting, then lets LinearLayout stack them; IScrollable
    /// reports the stacked height to the scroll window.
    /// </summary>
    [RequireComponent(typeof(UIScrollWindow))]
    public sealed class SettingsMenu : RadicalMenu, IScrollable
    {
        public Transform contentRoot;      // hosts the LinearLayoutUIComponent
        public GameObject sectionTemplate; // inactive; has a SectionBox
        public GameObject toggleTemplate;  // inactive; has a ToggleWidget

        private LinearLayoutUIComponent _layout;
        private UIScrollWindow _scroll;

        public void Populate()
        {
            _scroll = GetComponent<UIScrollWindow>();
            _layout = contentRoot.GetComponent<LinearLayoutUIComponent>();
            menuOptions.Clear();

            foreach (var section in ModSettings.Sections)
            {
                var boxGo = Object.Instantiate(sectionTemplate, contentRoot);
                boxGo.SetActive(true);
                boxGo.name = "Section " + section.ModId;
                var box = boxGo.GetComponent<SectionBox>();
                if (box.header != null) box.header.Render(section.DisplayName);

                foreach (var def in section.Settings)
                {
                    if (def.Kind != SettingKind.Toggle) continue; // Phase 2b: toggles only
                    var wGo = Object.Instantiate(toggleTemplate, box.widgetContainer);
                    wGo.SetActive(true);
                    wGo.name = "Toggle " + def.Key;
                    var toggle = wGo.GetComponent<ToggleWidget>();
                    toggle.Bind(def);
                    toggle.SetParentMenu(this);
                    menuOptions.Add(toggle);
                }
            }

            if (_layout != null) _layout.RenderUIComponent(true);
            if (_scroll != null)
            {
                _scroll.scrollingContent = contentRoot;
                _scroll.ResetScroll();
            }
        }

        // IScrollable — LinearLayout owns the stacking; report its height.
        public void UpdateContainingElements(float scroll) { }
        public bool IsBottomElementSelected() => false;
        public bool IsTopElementSelected() => false;
        public float GetCurrentWindowHeight() => _layout != null ? _layout.GetUIComponentRenderHeight() : 0f;
    }
}
