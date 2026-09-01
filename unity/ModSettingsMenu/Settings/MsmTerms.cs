namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// This mod's own localisation-term schema — the convention a mod author writes against to have
    /// their settings named in this menu. Both halves are documented here; only the first is
    /// composed here, because the second needs a token the caller holds:
    ///
    ///   Label(owner, key)         -> "&lt;Owner&gt;-Config/&lt;key&gt;"
    ///   a Choice option           -> that label, then "/&lt;token&gt;" (see SettingDef.ValueLabel)
    ///
    /// Two callers need it and reach it from opposite directions, which is why it is a class rather
    /// than an interpolation: <see cref="SectionBuilder"/> composes it from a registered consumer's
    /// mod id, and <see cref="ForeignConfigDiscovery"/> from the first segment of a discovered
    /// config file's path. The two agree only by construction, and until this existed they agreed
    /// only by both being written the same way.
    ///
    /// Authored in yaml as a namespace carrying the slash, with the key as a leaf under it — the
    /// generator parses two levels, so a per-option term reaches three segments by putting the
    /// first two in the namespace ("&lt;Owner&gt;-Config/&lt;key&gt;:" with each token beneath it).
    /// </summary>
    internal static class MsmTerms
    {
        /// <summary>The term for one setting's label. Owner is a consumer's mod id, or — for a
        /// discovered mod — the name its config file's folder carries.</summary>
        internal static string Label(string owner, string key) => owner + "-Config/" + key;

        // The agreement has a sharp edge: two rows produce the SAME term whenever the owners
        // coincide, which they do by convention. A consumer registering "<Mod>-Config/x" and a
        // second, unowned ConfigFile under the same folder with a key "x" would render the
        // registered setting's label on the discovered row. Not reachable through anything this
        // repo ships, and worth knowing before the schema is widened.

        /// <summary>The term for a section's hint line. The same shape with a reserved key, which
        /// is why it belongs beside Label rather than beside the caller: a consumer authoring both
        /// writes them under one yaml namespace, and they must keep agreeing on the prefix.</summary>
        internal static string Hint(string owner) => Label(owner, "_hint");
    }
}
