using System.Collections.Generic;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// The one rule every reader and writer of a List-kind value agrees on, in both directions:
    /// <see cref="Tokenize"/> splits a stored string into tokens (split(','), trim, drop empties)
    /// and <see cref="Join"/> puts tokens back together under exactly the same rule.
    ///
    /// Readers: row-list seeding (ListDetailScreen.Populate), the compact row preview
    /// (ListWidget.Preview), and the discovery-time list-vs-plain heuristic
    /// (ForeignConfigDiscovery.HeuristicSaysList). Writer: ListDetailScreen.OnRowTextCommitted,
    /// which joins the session's row list and compares it against the tokenized stored value.
    ///
    /// Each of these used to carry its own copy of the same four-line loop, and that duplication
    /// caused a real bug once (commit f9eb96f — the no-op-write comparison split raw text one way
    /// while the row build split it another, so a value nobody had edited still looked "changed"
    /// and wrote + rebuilt every time the drill-in was merely opened and closed). Join exists for
    /// the same reason and was added after the write path had already grown its own hand-written
    /// "skip the empties" loop: the drop-empties rule now lives in exactly one place, so the two
    /// sides of the comparison cannot drift apart again.
    /// </summary>
    internal static class ListTokenizer
    {
        public static List<string> Tokenize(string value)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(value))
                return tokens;
            foreach (var raw in value.Split(','))
            {
                var token = raw.Trim();
                if (token.Length > 0)
                    tokens.Add(token);
            }
            return tokens;
        }

        /// <summary>Joins tokens into a stored value under the same rule Tokenize applies when
        /// reading: trim, drop empties — and strip commas, because a comma INSIDE a token would
        /// split it in two on the way back. Safe to hand the drill-in's row list directly, blanks
        /// included: a row the user left empty is visible on screen and absent from the value.
        ///
        /// The comma strip lives here rather than at the call site so that
        /// <c>Tokenize(Join(x))</c> yields x back for ANY input. It used to sit in
        /// ListDetailScreen.OnRowTextCommitted, which made this method total only by the grace of
        /// its single caller — and this class exists precisely because a rule kept in one call site
        /// is a rule waiting to diverge.</summary>
        public static string Join(IEnumerable<string> tokens)
        {
            var kept = new List<string>();
            if (tokens == null)
                return "";
            foreach (var raw in tokens)
            {
                var token = Sanitize(raw);
                if (token.Length > 0)
                    kept.Add(token);
            }
            return string.Join(",", kept);
        }

        /// <summary>What a single token may look like: trimmed, and without the separator. Applied
        /// by <see cref="Join"/> to everything it writes, and available to callers that hold a token
        /// BEFORE it reaches Join — a text row, say, whose displayed value should already match what
        /// will be stored rather than showing a comma that silently vanishes on save. One rule, two
        /// moments of use; the rule itself lives only here.</summary>
        public static string Sanitize(string token) => token == null ? "" : token.Replace(",", "").Trim();
    }
}
