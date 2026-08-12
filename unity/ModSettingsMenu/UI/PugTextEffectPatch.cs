using HarmonyLib;

namespace ModSettingsMenu.UI
{
    /// <summary>
    /// Suppresses a harmless log-noise pattern shared by every widget row in this framework
    /// (SettingTemplate's Label/Value, ListWidget's Label/Value, ListDetailScreen's ItemTemplate
    /// Label alike): Populate() deliberately sets each row's text BEFORE base.Activate() (the
    /// "build structure, then activate, then render layout" pattern documented on
    /// ModSettingsScreen/ListDetailScreen — LinearLayoutUIComponent skips inactive children, so
    /// heights would read 0 before activation). At that point the row's own PugText.Render() call
    /// self-initializes via PugText's own "if (!hasCalledAwake) Awake();" guard (present in
    /// PugText, not in PugTextEffect), so it still resolves and renders correctly — but its sibling
    /// PugTextEffectMenuOption has no such guard, so its own Awake() (which sets the private _text
    /// field) genuinely has not run yet (Unity defers a component's Awake() until its GameObject is
    /// truly active-in-hierarchy, not just activeSelf), and PugText.Render()'s ResetEffects() call
    /// reaches this effect with a null text reference. CK's own ResetEffect already handles that
    /// gracefully (warns, returns) — confirmed harmless via a throwaway diagnostic (every one of
    /// ~700 occurrences across a play session had activeInHierarchy == false at the exact call;
    /// tinting on selection works correctly once base.Activate() runs and the effect's real Awake()
    /// fires) — but it produces one warning line per row per menu-open, every open, for every
    /// widget kind, which is significant Player.log noise for something that is not a real fault.
    ///
    /// This prefix returns false (skipping the original method body, including its LogWarning)
    /// ONLY when the instance's own GameObject is not yet active-in-hierarchy — the exact,
    /// empirically-confirmed condition of the harmless case. It does NOT suppress a genuinely null
    /// text reference on an already-active instance (a real fault CK's own warning should still
    /// surface); the original method still runs for that case.
    /// </summary>
    [HarmonyPatch]
    public static class PugTextEffectPatch
    {
        [HarmonyPatch(typeof(PugTextEffectMenuOption), nameof(PugTextEffectMenuOption.ResetEffect)), HarmonyPrefix]
        public static bool ResetEffect_SkipPreActivation(PugTextEffectMenuOption __instance) => __instance.gameObject.activeInHierarchy;
    }
}
