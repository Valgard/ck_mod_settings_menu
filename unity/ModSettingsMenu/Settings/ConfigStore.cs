using System.Collections.Generic;
using CoreLib.Data.Configuration;
using CoreLib.Util.Extension;
using PugMod;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Owns one CoreLib ConfigFile per consumer mod (keyed by ModId). CoreLib does
    /// all System.IO inside its own trusted assembly via API.ConfigFilesystem, so
    /// this stays sandbox-clean (no skipSafetyChecks). Files land at
    /// "ModSettingsMenu/&lt;ModId&gt;.cfg" in CoreLib's config filesystem; auto-save
    /// (SaveOnConfigSet) is on by default, so setting a value persists immediately.
    /// </summary>
    internal static class ConfigStore
    {
        private static readonly Dictionary<string, ConfigFile> _files = new Dictionary<string, ConfigFile>();

        internal static ConfigFile ForMod(IMod consumer, string modId)
        {
            if (_files.TryGetValue(modId, out var file))
                return file;
            // GetModInfo resolves the IMod ref to its LoadedMod via Handlers.Contains.
            var info = consumer.GetModInfo();
            file = new ConfigFile($"ModSettingsMenu/{modId}.cfg", saveOnInit: true, info);
            _files[modId] = file;
            return file;
        }
    }
}
