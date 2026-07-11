using PugMod;

namespace ModSettingsMenu
{
    /// <summary>Resolves a localisation term for the active language. Framework-own
    /// strings use T(term) (yaml guarantees a value); consumer strings use
    /// T(term, fallback) to fall back to a code identifier (key/token) when the
    /// consumer ships no term. API.Localization.GetLocalizedTerm returns null if
    /// the term is unregistered.</summary>
    internal static class Loc
    {
        public static string T(string term) => API.Localization.GetLocalizedTerm(term) ?? term;
        public static string T(string term, string fallback) => API.Localization.GetLocalizedTerm(term) ?? fallback;
    }
}
