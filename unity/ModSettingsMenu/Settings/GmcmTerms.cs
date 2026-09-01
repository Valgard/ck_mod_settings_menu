using System.Collections.Generic;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// General Mod Config Menu's localisation-term schema, rebuilt here so that a mod which already
    /// ships GMCM terms renders under them instead of under its raw keys. GMCM is the other config
    /// menu for Core Keeper and reads the same CoreLib ConfigFiles this discovery path does, so its
    /// convention is the one a foreign author is most likely to have already followed.
    ///
    /// Ported from GMCM 1.4.0's MiscHelper.GetLocalKey(filePath, params fix) — readable from an
    /// install at ModLoader/GeneralConfigMenu/Scripts/Scripts/MiscHelper.cs, or from mod.io as
    /// modfile 7840263 when it is not installed. Named here because this class is a port and
    /// nothing in the repository can check it against anything else. It drops the
    /// file extension off the last path segment, appends the extra parts, and then joins with a
    /// separator that changes for the last pair: `i &lt; length - 2 ? '_' : '/'`. That reads as one
    /// rule — every segment but the last is joined with '_', and the last is appended after a '/'
    /// — which is why the key sits AFTER the slash in a label term and BEFORE it in a value term
    /// (GMCM passes a trailing "" for the latter, pushing the key one place left).
    ///
    /// For PlacementPlus, whose file is "PlacementPlus/PlacementPlus.cfg" with a "General" section:
    ///   File      -> "PlacementPlus/PlacementPlus"
    ///   Label     -> "PlacementPlus_PlacementPlus_General/MaxBrushSize"
    ///   ValueBase -> "PlacementPlus_PlacementPlus_General_MaxBrushSize/"   (+ the token)
    ///
    /// Note what the second segment is: the FILE, not the mod. A mod whose config is the usual
    /// "config.cfg" therefore resolves to "&lt;Mod&gt;_config_&lt;Section&gt;/&lt;key&gt;", with a heading term of
    /// "&lt;Mod&gt;/config". That reads oddly and is nonetheless right — an author writing terms for
    /// GMCM wrote them against this.
    ///
    /// The point is to reproduce GMCM's output, not to improve on it: a term this builds that GMCM
    /// would not have built resolves to nothing, which is the same as having no second stage. Hence
    /// the split on '/' alone, where <see cref="ForeignConfigDiscovery"/>'s own OwnerFromPath also
    /// accepts '\\' — a backslash path would leave GMCM with an unusable term too, and an author
    /// names their terms after what GMCM actually asked for.
    ///
    /// Two boundaries worth naming. GMCM builds a fourth shape this does not: a per-SECTION
    /// heading, by feeding the composed file key back in ("&lt;Mod&gt;_&lt;file&gt;/&lt;Section&gt;"). MSM has no
    /// per-section heading to hang it on yet; the roadmap's grouping point is where it would
    /// arrive. And fidelity here is one-directional: it guarantees this builds nothing GMCM
    /// would not have, not that it finds everything GMCM finds — an author using GMCM's own
    /// LocalizationOverride tag names a term out of schema entirely, and reading that tag would
    /// need reflection, which the sandbox forbids.
    /// </summary>
    internal static class GmcmTerms
    {
        /// <summary>The term for the config file itself — GMCM's own heading for a mod's page.</summary>
        internal static string File(string configFilePath) => Compose(configFilePath);

        /// <summary>The term for one entry's label.</summary>
        internal static string Label(string configFilePath, string section, string key) => Compose(configFilePath, section, key);

        /// <summary>The prefix a per-option term is built on: append the token to it. Ends in '/',
        /// because GMCM composes this by passing an empty final part.</summary>
        internal static string ValueBase(string configFilePath, string section, string key) => Compose(configFilePath, section, key, "");

        private static string Compose(string configFilePath, params string[] extra)
        {
            // Split always yields at least one element, even for "" — which is what keeps the
            // indexing below total, and why RemoveEmptyEntries must not be added here as a
            // tidy-up: it would return an empty array for that input and throw on every menu
            // open for a config file with no path.
            var parts = new List<string>((configFilePath ?? "").Split('/'));
            int last = parts.Count - 1;
            int dot = parts[last].LastIndexOf('.');
            if (dot > 0)
                parts[last] = parts[last].Substring(0, dot);
            parts.AddRange(extra);

            int count = parts.Count;
            if (count == 1)
                return parts[0];
            return string.Join("_", parts.GetRange(0, count - 1)) + "/" + parts[count - 1];
        }
    }
}
