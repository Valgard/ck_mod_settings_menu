using System.Collections.Generic;
using CoreLib.Util.Extension;
using PugMod;
using UnityEngine;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Public consumer API + section registry. A consumer calls
    /// ModSettings.Section(this) in its IMod.Init, chains widget declarations, and
    /// calls Build(). The Phase-2b menu renders everything in Sections.
    /// </summary>
    public static class ModSettings
    {
        private static readonly List<ModSection> _sections = new List<ModSection>();

        /// <summary>All registered sections; the 2b menu reads this to render.</summary>
        public static IReadOnlyList<ModSection> Sections => _sections;

        /// <summary>
        /// Begin a settings section for the calling mod. Resolves modId +
        /// displayName from the IMod ref via CoreLib's GetModInfo (Handlers.Contains).
        /// </summary>
        public static SectionBuilder Section(IMod consumer)
        {
            var info = consumer.GetModInfo();
            string modId = info.Metadata.name;
            string displayName = string.IsNullOrEmpty(info.Metadata.displayName)
                ? info.Metadata.name
                : info.Metadata.displayName;
            var section = new ModSection
            {
                ModId = modId,
                DisplayName = displayName,
                HintTerm = $"{modId}-Config/_hint"
            };
            var file = ConfigStore.ForMod(consumer, modId);
            return new SectionBuilder(section, file);
        }

        /// <summary>
        /// TEMP test-fixture overload: register a section under an explicit id + display
        /// name (used by the Phase-2b self-test to show multiple stacked sections). Real
        /// consumers use Section(IMod). Remove with the self-test at publish.
        /// </summary>
        internal static SectionBuilder Section(IMod consumer, string modId, string displayName)
        {
            var section = new ModSection
            {
                ModId = modId,
                DisplayName = displayName,
                HintTerm = $"{modId}-Config/_hint"
            };
            var file = ConfigStore.ForMod(consumer, modId);
            return new SectionBuilder(section, file);
        }

        internal static void Register(ModSection section)
        {
            // Duplicate modId → first wins, warn (spec error handling).
            if (_sections.Exists(s => s.ModId == section.ModId))
            {
                Debug.LogWarning($"[ModSettingsMenu] section for '{section.ModId}' already registered; ignoring duplicate.");
                return;
            }
            _sections.Add(section);
        }
    }
}
