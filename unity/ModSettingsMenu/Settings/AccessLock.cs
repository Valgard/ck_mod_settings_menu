using CoreLib.Data.Configuration;

namespace ModSettingsMenu.Settings
{
    /// <summary>The one answer to "may this entry be changed in this session, right now".
    ///
    /// It lived in ForeignConfigDiscovery as a private static while only discovery had a scope to
    /// ask about. Both paths have one now, and MSM-19's send façade would be a third caller — so
    /// it sits where all of them reach it rather than being reimplemented per caller.
    ///
    /// The order of the branches is load-bearing: ViewOnly and Client are answered without
    /// consulting a player at all, which is why both hold at the title screen and are observable
    /// in a single-player session. Only Server and Admin need a world.</summary>
    internal static class AccessLock
    {
        internal static bool IsLocked(ConfigScope scope)
        {
            if (scope == null)
                return false;
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
