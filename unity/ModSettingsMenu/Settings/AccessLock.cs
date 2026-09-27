using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>The one answer to "may this entry be changed in this session, right now".
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
        internal static bool IsLocked(ConfigScope scope)
        {
            // Unreachable through an Entry: CoreLib normalises every entry's scope on construction
            // (ConfigEntryBase.cs:78, Scope = scope ?? ConfigScope.Empty). Kept as fail-shut rather
            // than deleted, because the value it would have to stand in for is ConfigScope.Empty —
            // whose constructor defaults to Server, not Client. Returning false here would hand a
            // caller the opposite of what the same entry reports through CoreLib.
            if (scope == null)
                return true;
            if (scope.accessLevel == ConfigAccessLevel.ViewOnly)
                return true;
            if (scope.accessLevel == ConfigAccessLevel.Client)
                return false;
            // Server/Admin: Changeable() reads Manager.main.player; at the title screen there is no
            // player, so be conservative (locked) rather than risk an NRE.
            if (Manager.main == null || Manager.main.player == null)
                return true;
            return !scope.Changeable();
        }
    }
}
