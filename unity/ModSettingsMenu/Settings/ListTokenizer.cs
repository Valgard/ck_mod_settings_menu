using System.Collections.Generic;

namespace ModSettingsMenu.Settings
{
    /// <summary>
    /// Splits a foreign comma-list ConfigEntry's raw string into its component tokens: split(','),
    /// trim, drop empties. The one tokenization rule every reader of a List-kind value agrees on —
    /// row population (ListDetailScreen.RebuildRows), the no-op-write comparison
    /// (ListDetailScreen.OnRowTextCommitted), the compact row preview (ListWidget.Preview), and the
    /// discovery-time list-vs-plain heuristic (ForeignConfigDiscovery.HeuristicSaysList) each used to
    /// duplicate this same four-line loop independently. That duplication already caused a real bug
    /// once (commit f9eb96f — the no-op-write comparison split raw text one way while RebuildRows
    /// split it another, so a value that was never actually edited still looked "changed" and wrote +
    /// rebuilt every time the drill-in was merely opened and closed); a single shared implementation
    /// makes the four call sites re-diverging impossible by construction.
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
    }
}
