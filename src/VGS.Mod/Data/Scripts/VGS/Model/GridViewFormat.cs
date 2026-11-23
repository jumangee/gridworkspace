using System;
using System.Globalization;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The wire format of the editor's view state: where the camera was, at what zoom, and
    /// whether blocks hidden from the schema were being shown (ADR-0022).
    ///
    /// It lives in <c>Vgs.Model</c>, next to <see cref="NodeStateFormat"/>, for the same reason:
    /// a format that silently mis-parses turns into a schema that opens at the wrong place, and
    /// this is the only layer that can be exercised without starting the game
    /// (<c>tools/offline-check.ps1</c>).
    ///
    /// <b>The value is four numbers, not three.</b> The fourth field is the "show hidden" toggle:
    /// it is part of how the construct is being looked at, so it belongs with the camera rather
    /// than in a separate key — one read per open instead of two, and one value that cannot get
    /// out of step with itself.
    ///
    /// <b>Invariant culture, like every other persisted number here.</b> On a locale with a
    /// decimal comma <c>0,26</c> would be read as two values, and the schema would open at a
    /// camera position nobody chose.
    ///
    /// <b>Forward compatibility by count, not by migration.</b> The parser accepts three fields
    /// as well as four: a world saved by a build that did not know about the flag keeps its
    /// camera and gets the flag's default. Extra fields beyond four are rejected outright — a
    /// value with a shape this code does not know is not a view state.
    /// </summary>
    public static class GridViewFormat
    {
        /// <summary>Separator between fields. A single space, like the node position format.</summary>
        private const char Separator = ' ';

        /// <summary>Number of fields the current format writes.</summary>
        public const int FieldCount = 4;

        /// <summary>
        /// Round-trip formatting: the shortest text that parses back to the same float. Without
        /// it a camera can drift a fraction of a unit on every save/load cycle, and the drift
        /// accumulates because the written value becomes the next starting point.
        /// </summary>
        private const string RoundTrip = "R";

        /// <summary>Formats the view state as <c>"x y zoom hidden"</c>. Never returns null.</summary>
        public static string Format(Vector2 camera, float zoom, bool showHidden)
        {
            return camera.X.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + camera.Y.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + zoom.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + (showHidden ? "1" : "0");
        }

        /// <summary>
        /// Parses a stored view state. False for anything unusable: an empty string, fewer than
        /// three fields, more than four, a non-numeric field, or a non-finite number.
        ///
        /// Failing softly is deliberate, exactly as it is for a node position: a corrupt value
        /// must mean "no saved view, start centred", not an exception in the screen's open path.
        /// </summary>
        public static bool TryParse(string text, out Vector2 camera, out float zoom, out bool showHidden)
        {
            camera = Vector2.Zero;
            zoom = 0f;
            showHidden = false;

            if (string.IsNullOrEmpty(text)) return false;

            // Split by hand: the value has at most four short fields, and string.Split would
            // allocate an array per open for nothing.
            float[] fields = new float[FieldCount];
            int count = 0;
            int start = 0;

            while (start <= text.Length)
            {
                int end = text.IndexOf(Separator, start);
                if (end < 0) end = text.Length;

                if (count >= FieldCount) return false;          // a fifth field: not our format
                if (end <= start) return false;                  // an empty field

                float value;
                if (!TryParseFloat(text.Substring(start, end - start), out value)) return false;
                fields[count++] = value;

                start = end + 1;
            }

            if (count < 3) return false;

            camera = new Vector2(fields[0], fields[1]);
            zoom = fields[2];

            if (float.IsNaN(camera.X) || float.IsNaN(camera.Y)) return false;
            if (float.IsInfinity(camera.X) || float.IsInfinity(camera.Y)) return false;
            if (float.IsNaN(zoom) || float.IsInfinity(zoom)) return false;
            if (zoom <= 0f) return false;

            // The flag is read as "non-zero" rather than "equals 1": a future build may write
            // more states into the same field, and a value it understands must not be lost here.
            showHidden = count >= FieldCount && fields[3] != 0f;
            return true;
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
