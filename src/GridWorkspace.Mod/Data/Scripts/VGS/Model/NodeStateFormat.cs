using System;
using System.Collections.Generic;
using System.Globalization;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The wire format of a node's persisted state: how a canvas position becomes a string and
    /// back.
    ///
    /// It lives in <c>Vgs.Model</c>, not in <c>Vgs.Game</c>, for one reason: the round trip is
    /// the part that can silently corrupt a layout, and this is the only layer that can be
    /// exercised without starting the game (<c>tools/offline-check.ps1</c>). The storage
    /// plumbing around it — which component, which key — is <c>Vgs.Game.NodeStateStore</c>.
    ///
    /// Why a plain string and not JSON or MyIni: the value is two floats, it is read once per
    /// block while the graph is built, and a document parser on that path would cost more than
    /// the whole node. Two numbers and a space is the whole format.
    ///
    /// <b>Invariant culture is not optional.</b> On a locale with a decimal comma
    /// <c>0,26.ToString()</c> produces "0,26", and a parser expecting "." would read a different
    /// number — or, worse, read "0" and "26" as two valid values. Both directions therefore go
    /// through <see cref="CultureInfo.InvariantCulture"/> explicitly; the current culture of the
    /// player's machine never enters the string.
    /// </summary>
    public static class NodeStateFormat
    {
        /// <summary>Separator between the two numbers. A single space, not a comma.</summary>
        private const char Separator = ' ';

        /// <summary>
        /// Round-trip ("R") formatting: the shortest text that parses back to the same float.
        /// Without it a position can shift by a fraction of a unit on every save/load cycle.
        /// </summary>
        private const string RoundTrip = "R";

        /// <summary>
        /// Formats a position as <c>"x y"</c>. Never returns null and never throws.
        /// </summary>
        public static string FormatPosition(Vector2 position)
        {
            return position.X.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + position.Y.ToString(RoundTrip, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses <c>"x y"</c> back into a position. Returns false for anything else: an empty
        /// string, a single number, extra parts, a non-numeric part, or a value that is not a
        /// finite number.
        ///
        /// Failing softly is deliberate. A stored value can be corrupt — hand-edited storage,
        /// a truncated save, an older format — and the correct answer to corrupt data is "this
        /// node has no saved position, lay it out normally", not an exception in the graph build
        /// path.
        /// </summary>
        public static bool TryParsePosition(string text, out Vector2 position)
        {
            position = Vector2.Zero;
            if (string.IsNullOrEmpty(text)) return false;

            int split = text.IndexOf(Separator);
            if (split <= 0 || split >= text.Length - 1) return false;

            // Reject a third part: "1 2 3" is not a position, and accepting its prefix would
            // hide a format change instead of reporting it.
            if (text.IndexOf(Separator, split + 1) >= 0) return false;

            float x;
            float y;
            if (!TryParseFloat(text.Substring(0, split), out x)) return false;
            if (!TryParseFloat(text.Substring(split + 1), out y)) return false;

            // NaN and infinity survive float.TryParse but would poison every rectangle the
            // node touches, so they are rejected here rather than drawn.
            if (float.IsNaN(x) || float.IsNaN(y)) return false;
            if (float.IsInfinity(x) || float.IsInfinity(y)) return false;

            position = new Vector2(x, y);
            return true;
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Formats a list of entity ids as <c>"id id id"</c> (ADR-0017, an event controller's
        /// watch list). An empty list becomes an empty string rather than a stray separator.
        ///
        /// Invariant culture, like the position: the value is read back on a machine whose
        /// locale may format numbers differently, and a list that fails to parse would silently
        /// empty the controller on the next load.
        /// </summary>
        public static string FormatIds(IList<long> ids)
        {
            if (ids == null || ids.Count == 0) return string.Empty;

            var text = new System.Text.StringBuilder(ids.Count * 8);
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) text.Append(Separator);
                text.Append(ids[i].ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>
        /// Parses <c>"id id id"</c> into <paramref name="into"/>, which is cleared first.
        ///
        /// <b>Tolerant on purpose, unlike <see cref="TryParsePosition"/>.</b> A watch list is a
        /// set of independent ids: one unreadable token must not cost the others, so it is
        /// skipped. A repeated id is skipped too — a block cannot be watched twice, and the
        /// graph would otherwise grow a second port for the same block. Zero and negative ids
        /// are skipped as well: no entity has them, and they would draw a bar with no block
        /// behind it.
        /// </summary>
        public static void ParseIds(string text, List<long> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(text)) return;

            int start = 0;
            while (start < text.Length)
            {
                int end = text.IndexOf(Separator, start);
                if (end < 0) end = text.Length;

                if (end > start)
                {
                    long id;
                    if (long.TryParse(text.Substring(start, end - start), NumberStyles.Integer,
                                      CultureInfo.InvariantCulture, out id)
                        && id > 0
                        && !into.Contains(id))
                    {
                        into.Add(id);
                    }
                }

                start = end + 1;
            }
        }
    }
}
