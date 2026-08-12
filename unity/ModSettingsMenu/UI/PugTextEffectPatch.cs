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
    /// This prefix skips the original method body (including its LogWarning) ONLY when BOTH:
    /// the instance's own GameObject is not yet active-in-hierarchy, AND it belongs to one of this
    /// mod's own two screens (ListDetailScreen or ModSettingsScreen) — the exact,
    /// empirically-confirmed condition of the harmless case, narrowed to where it was actually
    /// observed. The condition is deliberately NOT just !activeInHierarchy: ResetEffect's body does
    /// real work beyond the null check it warns on (glyph-jump recycling, the OnSelected/
    /// EndEffectImmediate call, cooloff-timer stops) for ANY PugTextEffectMenuOption in the whole
    /// game, not just this mod's rows — a bare !activeInHierarchy prefix would silently skip that
    /// real work for every other (foreign or vanilla) UI element that happens to be inactive when its
    /// own PugText renders, which is a genuine behavior change to code this mod doesn't own, not just
    /// log-noise suppression. Scoping to our own screens' component hierarchy keeps the fix as narrow
    /// as the confirmed case. A genuinely null text reference on an already-active instance, or on any
    /// instance outside our own two screens, still runs the original method unchanged.
    /// </summary>
    [HarmonyPatch]
    public static class PugTextEffectPatch
    {
        [HarmonyPatch(typeof(PugTextEffectMenuOption), nameof(PugTextEffectMenuOption.ResetEffect)), HarmonyPrefix]
        public static bool ResetEffect_SkipPreActivation(PugTextEffectMenuOption __instance) =>
            __instance.gameObject.activeInHierarchy
            || (__instance.GetComponentInParent<ListDetailScreen>(true) == null && __instance.GetComponentInParent<ModSettingsScreen>(true) == null);
    }
}
