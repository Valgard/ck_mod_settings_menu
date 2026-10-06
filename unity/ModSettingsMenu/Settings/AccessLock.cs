using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>Why an entry cannot be changed right now, if it cannot. The menu reads it to
    /// paint withheld rows and to word the section note; <see cref="LockReason.NoWorld"/> and <see cref="LockReason.ConditionUnmet"/> are the two
    /// that name a condition the player could meet, <see cref="LockReason.ViewOnly"/> is permanent.</summary>
    internal enum LockReason
    {
        None,
        NoWorld,
        ConditionUnmet,
        ViewOnly,
    }

    /// <summary>The one answer to "may this entry be changed in this session, right now,
    /// and if not, why".
    ///
    /// It lived in ForeignConfigDiscovery as a private static while only discovery had a scope to
    /// ask about. Both paths have one now, and MSM-19's send façade would be a third caller — so
    /// it sits where all of them reach it rather than being reimplemented per caller.
    ///
    /// The order of the branches is load-bearing, and for a harder reason than "no player needed":
    /// CoreLib's Changeable() reads Manager.main.player BEFORE it looks at the level at all
    /// (ConfigScope.cs:34, above its switch). Answering ViewOnly and Client here is therefore not
    /// an optimisation that saves a lookup — it is what keeps those two levels from reaching a
    /// dereference at the title screen, where there is no player. Only Server and Admin need a
    /// world, and they are the two that fall through to the guard below.</summary>
    internal static class AccessLock
    {
        internal static LockReason Reason(ConfigScope scope)
        {
            // Unreachable through an Entry: CoreLib normalises every entry's scope on construction
            // (ConfigEntryBase.cs:78, Scope = scope ?? ConfigScope.Empty). Kept as fail-shut rather
            // than deleted, because the value it would have to stand in for is ConfigScope.Empty —
            // whose constructor defaults to Server, not Client. Returning false here would hand a
            // caller the opposite of what the same entry reports through CoreLib.
            if (scope == null)
                return LockReason.ConditionUnmet;
            if (scope.accessLevel == ConfigAccessLevel.ViewOnly)
                return LockReason.ViewOnly;
            if (scope.accessLevel == ConfigAccessLevel.Client)
                return LockReason.None;
            // Server/Admin: Changeable() reads Manager.main.player; at the title screen there is no
            // player, so be conservative (locked, NoWorld) rather than risk an NRE.
            if (Manager.main == null || Manager.main.player == null)
                return LockReason.NoWorld;
            return scope.Changeable() ? LockReason.None : LockReason.ConditionUnmet;
        }
    }
}
