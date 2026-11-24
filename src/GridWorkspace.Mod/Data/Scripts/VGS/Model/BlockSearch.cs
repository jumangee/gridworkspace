using System;
using System.Collections.Generic;

namespace Vgs.Model
{
    /// <summary>
    /// The block filter of the search field (ADR-0023): which cards match what the player typed.
    ///
    /// <b>The rule is the game's own block-list filter, not an invention of this editor.</b> The
    /// terminal's block list hides a block unless <em>every</em> space-separated term of the
    /// query is a substring of the block's name <em>or</em> of its type line; the code is
    /// <c>MyTerminalControlPanel.blockSearch_TextChanged(string, bool)</c> (verified by IL, see
    /// <c>docs/01-research/api-terminal-search.md</c>):
    ///
    /// <code>
    /// string[] terms = text.Split(' ');
    /// string text1 = item.Text.ToString().ToLower();               // block name
    /// string text2 = item.ToolTip.ToolTips[0].Text.ToString().ToLower();   // block type
    /// foreach (string term in terms)
    ///     if (!text1.Contains(term) &amp;&amp; !text2.Contains(term)) visible = false;
    /// </code>
    ///
    /// So: <b>AND over the terms, OR over the two fields, substring, case-insensitive.</b> The
    /// editor follows it because "the same as in the terminal" is what the product owner asked
    /// for, and because a second, subtly different rule would teach the player two behaviours
    /// for one gesture.
    ///
    /// The two fields are the terminal's: the block's **name** and its **displayed type** — the
    /// localized definition name, not the engine subtype. A player types "timer" or "таймер";
    /// nobody types "LargeBlockTimer". The node therefore carries
    /// <see cref="Node.TypeDisplayName"/>, read from the game when the graph is built.
    ///
    /// Two deliberate details:
    ///
    ///  * <b>Terms are split once, when the query changes.</b> <see cref="SplitTerms"/> fills a
    ///    caller-owned list and <see cref="Matches"/> takes it, so the per-card test in the draw
    ///    path allocates nothing — <c>IndexOf</c> with <c>OrdinalIgnoreCase</c> is a scan, not a
    ///    <c>ToLower</c> per card per frame.
    ///  * <b>Case-insensitivity is ordinal.</b> The game lowercases with the current culture; the
    ///    difference shows up only where a culture maps a letter to a different letter (Turkish
    ///    dotless i). Ordinal is the more predictable of the two for block names and types, and it
    ///    needs no allocation.
    /// </summary>
    public static class BlockSearch
    {
        /// <summary>Separator between terms — a space, exactly as the game splits.</summary>
        public const char TermSeparator = ' ';

        /// <summary>
        /// Splits a query into terms, replacing the contents of <paramref name="into"/>.
        ///
        /// Terms are stored **as typed**: the case-insensitive comparison happens once, in
        /// <see cref="Matches"/>, through <see cref="StringComparison.OrdinalIgnoreCase"/>. Lowering
        /// them here as well would fold case twice, and two foldings in one rule is how a search
        /// starts disagreeing with itself.
        ///
        /// Empty terms are dropped. That is equivalent to the game's rule rather than a
        /// simplification of it: the game splits on every space, and an empty term is a substring
        /// of every string, so it never hides anything.
        /// </summary>
        public static void SplitTerms(string query, List<string> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(query)) return;

            int start = 0;
            while (start < query.Length)
            {
                int end = query.IndexOf(TermSeparator, start);
                if (end < 0) end = query.Length;

                if (end > start) into.Add(query.Substring(start, end - start));

                start = end + 1;
            }
        }

        /// <summary>
        /// True when the card matches every term of <paramref name="terms"/>: each term must occur
        /// in the block's name or in its displayed type. An empty term list matches everything,
        /// which is what "no query" has to mean.
        /// </summary>
        public static bool Matches(IList<string> terms, string name, string typeName)
        {
            if (terms == null || terms.Count == 0) return true;

            for (int i = 0; i < terms.Count; i++)
            {
                string term = terms[i];
                if (term == null || term.Length == 0) continue;

                if (Contains(name, term)) continue;
                if (Contains(typeName, term)) continue;
                return false;
            }

            return true;
        }

        private static bool Contains(string haystack, string term)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
