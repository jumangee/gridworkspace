using System;
using System.Globalization;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The wire format of the editor's view state: where the camera was, at what zoom, and the
    /// four view toggles — "show invisible", "show subgrids", "show grid borders" and "show
    /// conveyor network" (ADR-0022, ADR-0032, ADR-0040).
    ///
    /// It lives in <c>Vgs.Model</c>, next to <see cref="NodeStateFormat"/>, for the same reason:
    /// a format that silently mis-parses turns into a schema that opens at the wrong place, and
    /// this is the only layer that can be exercised without starting the game
    /// (<c>tools/offline-check.ps1</c>).
    ///
    /// <b>The value grew from four numbers to seven, and old worlds still open.</b> The parser
    /// accepts three to seven fields; a missing field takes its default. That is the whole
    /// migration: a world saved before the conveyor network existed keeps its camera and gets
    /// the network shown, which is what the new build shows by default anyway. Extra fields
    /// beyond seven are rejected outright — a value with a shape this code does not know is not
    /// a view state.
    ///
    /// <b>Invariant culture, like every other persisted number here.</b> On a locale with a
    /// decimal comma <c>0,26</c> would be read as two values, and the schema would open at a
    /// camera position nobody chose.
    /// </summary>
    public static class GridViewFormat
    {
        /// <summary>Separator between fields. A single space, like the node position format.</summary>
        private const char Separator = ' ';

        /// <summary>Number of fields the current format writes.</summary>
        public const int FieldCount = 7;

        /// <summary>Fewest fields any build has written: camera x, camera y, zoom.</summary>
        public const int MinFieldCount = 3;

        /// <summary>
        /// Round-trip formatting: the shortest text that parses back to the same float. Without
        /// it a camera can drift a fraction of a unit on every save/load cycle, and the drift
        /// accumulates because the written value becomes the next starting point.
        /// </summary>
        private const string RoundTrip = "R";

        /// <summary>
        /// Formats the view state as
        /// <c>"x y zoom hidden subgrids borders conveyors"</c>. Never returns null.
        /// </summary>
        public static string Format(Vector2 camera, float zoom, bool showHidden,
                                    bool showSubgrids, bool showGridBorders, bool showConveyors)
        {
            return camera.X.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + camera.Y.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + zoom.ToString(RoundTrip, CultureInfo.InvariantCulture)
                   + Separator
                   + (showHidden ? "1" : "0")
                   + Separator
                   + (showSubgrids ? "1" : "0")
                   + Separator
                   + (showGridBorders ? "1" : "0")
                   + Separator
                   + (showConveyors ? "1" : "0");
        }

        /// <summary>
        /// Parses a stored view state. False for anything unusable: an empty string, fewer than
        /// three fields, more than seven, a non-numeric field, or a non-finite number.
        ///
        /// Failing softly is deliberate, exactly as it is for a node position: a corrupt value
        /// must mean "no saved view, start centred", not an exception in the screen's open path.
        /// </summary>
        public static bool TryParse(string text, out Vector2 camera, out float zoom,
                                    out bool showHidden, out bool showSubgrids,
                                    out bool showGridBorders, out bool showConveyors)
        {
            camera = Vector2.Zero;
            zoom = 0f;
            showHidden = false;
            showSubgrids = true;
            showGridBorders = true;
            showConveyors = true;

            if (string.IsNullOrEmpty(text)) return false;

            // Split by hand: the value has at most seven short fields, and string.Split would
            // allocate an array per open for nothing.
            float[] fields = new float[FieldCount];
            int count = 0;
            int start = 0;

            while (start <= text.Length)
            {
                int end = text.IndexOf(Separator, start);
                if (end < 0) end = text.Length;

                if (count >= FieldCount) return false;          // an eighth field: not our format
                if (end <= start) return false;                  // an empty field

                float value;
                if (!TryParseFloat(text.Substring(start, end - start), out value)) return false;
                fields[count++] = value;

                start = end + 1;
            }

            if (count < MinFieldCount) return false;

            camera = new Vector2(fields[0], fields[1]);
            zoom = fields[2];

            if (float.IsNaN(camera.X) || float.IsNaN(camera.Y)) return false;
            if (float.IsInfinity(camera.X) || float.IsInfinity(camera.Y)) return false;
            if (float.IsNaN(zoom) || float.IsInfinity(zoom)) return false;
            if (zoom <= 0f) return false;

            // Each flag is read as "non-zero" rather than "equals 1": a future build may write
            // more states into the same field, and a value it understands must not be lost here.
            // A field that was never written keeps the default passed in above.
            showHidden = count >= 4 && fields[3] != 0f;
            if (count >= 5) showSubgrids = fields[4] != 0f;
            if (count >= 6) showGridBorders = fields[5] != 0f;
            if (count >= 7) showConveyors = fields[6] != 0f;
            return true;
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
