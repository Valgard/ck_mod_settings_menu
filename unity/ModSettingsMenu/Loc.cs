using PugMod;

namespace ModSettingsMenu
{
    /// <summary>Resolves a localisation term for the active language. Framework-own
    /// strings use T(term) (yaml guarantees a value); consumer strings use
    /// T(term, fallback) to fall back to a code identifier (key/token) when the
    /// consumer ships no term. API.Localization.GetLocalizedTerm returns null if
    /// the term is unregistered — which is what every fallback here is built on,
    /// and it is a property of the shipped I2Languages asset rather than of the
    /// code: its OnMissingTranslation is Fallback. Were that serialized value ever
    /// ShowTerm or ShowWarning, stage 1 would answer every lookup with a non-empty
    /// string — the term itself, or a "&lt;!-Missing Translation-!&gt;" marker — and no
    /// later stage would ever run, silently.</summary>
    internal static class Loc
    {
        public static string T(string term) => Lookup(term) ?? term;

        public static string T(string term, string fallback) => Lookup(term) ?? fallback;

        /// <summary>Two terms in preference order, then the raw fallback — the resolution a
        /// discovered entry needs, where MSM's own schema is tried first and a foreign convention
        /// (see <see cref="Settings.GmcmTerms"/>) second.
        ///
        /// A separate name rather than a third parameter on T, because `Loc.T(def.Term,
        /// def.GmcmTerm)` compiles: it would bind the second term as the FALLBACK and render that
        /// raw term as the visible menu text wherever the first one is missing — which is every
        /// row of a mod that ships no MSM terms, i.e. exactly the case this chain exists for. The
        /// bug is silent, and no test in this repo would catch it.
        ///
        /// Either term may be null (a registered consumer has no second stage); a null or empty
        /// term is skipped, never looked up. An empty FALLBACK is answered with null rather than
        /// with itself: the fallback is the one stage that is not a lookup, and it carries a value
        /// this mod does not control — CoreLib accepts a blank config key. Returning "" there would
        /// hand the caller the blank text the empty-is-a-miss rule below exists to prevent, through
        /// the one door that rule cannot see.</summary>
        public static string TFirstOf(string preferred, string alternate, string fallback) =>
            Lookup(preferred) ?? Lookup(alternate) ?? (string.IsNullOrEmpty(fallback) ? null : fallback);

        /// <summary>One stage of a chain: the term's translation, or null when this stage has
        /// nothing to say. Empty INPUT is skipped without a lookup; an empty RESULT is reported as
        /// a miss, and that second half is the load-bearing one.
        ///
        /// I2 has a path that answers a lookup with a non-null empty string: a term whose stored
        /// cell holds "---", its own convention for "deliberately blank", returns success with
        /// string.Empty (I2.decompiled.cs:6935-6938). Passed through, that value stops a `??`
        /// chain dead — later stages never run and the row renders as nothing at all: not the
        /// foreign term, not the raw key. A genuinely empty cell falls through correctly, so the
        /// two cases an author cannot tell apart would behave in opposite ways.
        ///
        /// Treating empty as a miss also protects the row itself. PugText.Render reports Rect.zero
        /// for empty text, and a row whose columns are both blank collapses to no height —
        /// navigable by keyboard, invisible, unclickable.
        ///
        /// One thing this canNOT report as a miss: a term that EXISTS with an empty cell for the
        /// active language. Because OnMissingTranslation is Fallback, I2 answers such a lookup with
        /// the first non-empty cell in any OTHER language and calls it a hit. For the chain above
        /// that means a stage-1 term authored in one language only beats a stage-2 term authored in
        /// the player's. That is the stage order working as designed; it is recorded here because
        /// the symptom — English text in a German game — reads like a broken chain and is not
        /// one.
        ///
        /// Internal rather than private: ForeignConfigDiscovery's naming diagnostic (MSM-28) asks
        /// this exact question a second time, per stage, purely to COUNT which one answered a
        /// discovered row's label. It has to see precisely what Lookup sees — empty-is-a-miss
        /// included — or the count could disagree with what TFirstOf actually rendered, which is
        /// the one outcome that would make the diagnostic worse than none at all.</summary>
        internal static string Lookup(string term)
        {
            if (string.IsNullOrEmpty(term))
                return null;
            string result = API.Localization.GetLocalizedTerm(term);
            return string.IsNullOrEmpty(result) ? null : result;
        }
    }
}
