using System.Collections.Generic;
using CoreLib.Data.Configuration;
using CoreLib.Util.Extension;
using PugMod;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Owns one CoreLib ConfigFile per consumer mod (keyed by ModId). CoreLib does
    /// all System.IO inside its own trusted assembly via API.ConfigFilesystem, so
    /// this stays sandbox-clean (no skipSafetyChecks). Files land at the consumer-owned
    /// "&lt;ModId&gt;/config.cfg" in CoreLib's config filesystem. Every write the
    /// framework performs is followed by <see cref="Persist"/>, because CoreLib's own
    /// auto-save (SaveOnConfigSet) is a public flag another mod can switch off — General
    /// Mod Config Menu does exactly that on every file but CoreLib's.
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
            file = new ConfigFile($"{modId}/config.cfg", saveOnInit: true, info);
            _files[modId] = file;
            return file;
        }

        /// <summary>
        /// Writes the entry's file to disk unless CoreLib already did. CoreLib saves inside
        /// the value setter only while SaveOnConfigSet is on; when another mod has switched
        /// it off, the value would otherwise live in memory only and be lost on restart.
        /// The flag is deliberately not switched back on: that would fight the other mod's
        /// own handling of the file instead of sidestepping it. One save per write — a file
        /// holds a handful of lines, and saving at once loses nothing to a crash.
        /// </summary>
        internal static void Persist(ConfigEntryBase entry)
        {
            var file = entry?.ConfigFile;
            if (file != null && !file.SaveOnConfigSet)
                file.Save();
        }
    }
}
