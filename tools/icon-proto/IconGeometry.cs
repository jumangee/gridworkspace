using System;
using System.Collections.Generic;
using System.Globalization;
using VRageMath;

namespace Vgs.IconProto
{
    /// <summary>
    /// One Font Awesome icon as flattened polygons in its own viewBox coordinates.
    ///
    /// Font Awesome 7 ships one &lt;path&gt; per icon file, and across the whole free set
    /// (2883 files: 2001 solid, 273 regular, 609 brands) the path data uses only the commands
    /// <c>M l c s a z</c>. This parser accepts the full set anyway, because a curated icon
    /// table may later include an icon from another family and a parser that silently drops a
    /// command is worse than one that is 60 lines longer.
    /// </summary>
    public sealed class IconShape
    {
        public readonly List<List<Vector2>> Contours = new List<List<Vector2>>();
        public float ViewWidth;
        public float ViewHeight;

        public int PointCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Contours.Count; i++) n += Contours[i].Count;
                return n;
            }
        }
    }

    public static class SvgPath
    {
        /// <summary>
        /// Parses SVG path data into flattened contours.
        /// <paramref name="tolerance"/> is in viewBox units: a curve is subdivided until it is
        /// closer than that to the polyline. The caller sets it from the target pixel size, so
        /// the flattening error stays sub-pixel at the size actually drawn.
        /// </summary>
        public static IconShape Parse(string data, float viewWidth, float viewHeight, float tolerance)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (tolerance <= 0f) tolerance = 0.1f;

            var shape = new IconShape { ViewWidth = viewWidth, ViewHeight = viewHeight };
            var lexer = new Lexer(data);

            List<Vector2> contour = null;
            Vector2 current = Vector2.Zero;
            Vector2 subpathStart = Vector2.Zero;
            Vector2 lastCubicControl = Vector2.Zero;
            Vector2 lastQuadControl = Vector2.Zero;
            bool hasCubic = false;
            bool hasQuad = false;
            char command = '\0';

            while (true)
            {
                lexer.SkipSeparators();
                if (lexer.AtEnd) break;

                if (lexer.NextIsCommand())
                {
                    command = lexer.ReadCommand();
                }
                else if (command == '\0')
                {
                    throw new FormatException("path data does not start with a command");
                }

                char upper = char.ToUpperInvariant(command);
                bool relative = char.IsLower(command);

                switch (upper)
                {
                    case 'M':
                    {
                        float x, y;
                        lexer.ReadNumber(out x);
                        lexer.ReadNumber(out y);
                        if (relative) { x += current.X; y += current.Y; }

                        contour = new List<Vector2>();
                        shape.Contours.Add(contour);
                        contour.Add(new Vector2(x, y));

                        current = new Vector2(x, y);
                        subpathStart = current;
                        hasCubic = false;
                        hasQuad = false;

                        // Additional coordinate pairs after a moveto are implicit linetos.
                        command = relative ? 'l' : 'L';
                        break;
                    }

                    case 'L':
                    {
                        float x, y;
                        lexer.ReadNumber(out x);
                        lexer.ReadNumber(out y);
                        if (relative) { x += current.X; y += current.Y; }

                        EnsureContour(shape, ref contour, current);
                        current = new Vector2(x, y);
                        contour.Add(current);
                        hasCubic = false;
                        hasQuad = false;
                        break;
                    }

                    case 'H':
                    {
                        float x;
                        lexer.ReadNumber(out x);
                        if (relative) x += current.X;

                        EnsureContour(shape, ref contour, current);
                        current = new Vector2(x, current.Y);
                        contour.Add(current);
                        hasCubic = false;
                        hasQuad = false;
                        break;
                    }

                    case 'V':
                    {
                        float y;
                        lexer.ReadNumber(out y);
                        if (relative) y += current.Y;

                        EnsureContour(shape, ref contour, current);
                        current = new Vector2(current.X, y);
                        contour.Add(current);
                        hasCubic = false;
                        hasQuad = false;
                        break;
                    }

                    case 'C':
                    {
                        float x1, y1, x2, y2, x, y;
                        lexer.ReadNumber(out x1); lexer.ReadNumber(out y1);
                        lexer.ReadNumber(out x2); lexer.ReadNumber(out y2);
                        lexer.ReadNumber(out x); lexer.ReadNumber(out y);
                        if (relative)
                        {
                            x1 += current.X; y1 += current.Y;
                            x2 += current.X; y2 += current.Y;
                            x += current.X; y += current.Y;
                        }

                        EnsureContour(shape, ref contour, current);
                        var c1 = new Vector2(x1, y1);
                        var c2 = new Vector2(x2, y2);
                        var end = new Vector2(x, y);
                        FlattenCubic(contour, current, c1, c2, end, tolerance, 0);
                        current = end;
                        lastCubicControl = c2;
                        hasCubic = true;
                        hasQuad = false;
                        break;
                    }

                    case 'S':
                    {
                        float x2, y2, x, y;
                        lexer.ReadNumber(out x2); lexer.ReadNumber(out y2);
                        lexer.ReadNumber(out x); lexer.ReadNumber(out y);
                        if (relative)
                        {
                            x2 += current.X; y2 += current.Y;
                            x += current.X; y += current.Y;
                        }

                        EnsureContour(shape, ref contour, current);
                        Vector2 c1 = hasCubic ? Reflect(current, lastCubicControl) : current;
                        var c2 = new Vector2(x2, y2);
                        var end = new Vector2(x, y);
                        FlattenCubic(contour, current, c1, c2, end, tolerance, 0);
                        current = end;
                        lastCubicControl = c2;
                        hasCubic = true;
                        hasQuad = false;
                        break;
                    }

                    case 'Q':
                    {
                        float x1, y1, x, y;
                        lexer.ReadNumber(out x1); lexer.ReadNumber(out y1);
                        lexer.ReadNumber(out x); lexer.ReadNumber(out y);
                        if (relative)
                        {
                            x1 += current.X; y1 += current.Y;
                            x += current.X; y += current.Y;
                        }

                        EnsureContour(shape, ref contour, current);
                        var q = new Vector2(x1, y1);
                        var end = new Vector2(x, y);
                        FlattenQuadratic(contour, current, q, end, tolerance, 0);
                        current = end;
                        lastQuadControl = q;
                        hasQuad = true;
                        hasCubic = false;
                        break;
                    }

                    case 'T':
                    {
                        float x, y;
                        lexer.ReadNumber(out x);
                        lexer.ReadNumber(out y);
                        if (relative) { x += current.X; y += current.Y; }

                        EnsureContour(shape, ref contour, current);
                        Vector2 q = hasQuad ? Reflect(current, lastQuadControl) : current;
                        var end = new Vector2(x, y);
                        FlattenQuadratic(contour, current, q, end, tolerance, 0);
                        current = end;
                        lastQuadControl = q;
                        hasQuad = true;
                        hasCubic = false;
                        break;
                    }

                    case 'A':
                    {
                        float rx, ry, rotation, largeArc, sweep, x, y;
                        lexer.ReadNumber(out rx); lexer.ReadNumber(out ry);
                        lexer.ReadNumber(out rotation);
                        lexer.ReadNumber(out largeArc);
                        lexer.ReadNumber(out sweep);
                        lexer.ReadNumber(out x); lexer.ReadNumber(out y);
                        if (relative) { x += current.X; y += current.Y; }

                        EnsureContour(shape, ref contour, current);
                        var end = new Vector2(x, y);
                        FlattenArc(contour, current, rx, ry, rotation, largeArc != 0f, sweep != 0f,
                                   end, tolerance);
                        current = end;
                        hasCubic = false;
                        hasQuad = false;
                        break;
                    }

                    case 'Z':
                    {
                        if (contour != null && contour.Count > 0)
                        {
                            Vector2 first = contour[0];
                            if ((first - contour[contour.Count - 1]).LengthSquared() > 1e-8f)
                                contour.Add(first);
                        }

                        current = subpathStart;
                        contour = null;
                        hasCubic = false;
                        hasQuad = false;
                        break;
                    }

                    default:
                        throw new FormatException("unsupported path command '" + command + "'");
                }
            }

            return shape;
        }

        private static void EnsureContour(IconShape shape, ref List<Vector2> contour, Vector2 current)
        {
            if (contour != null) return;
            contour = new List<Vector2>();
            contour.Add(current);
            shape.Contours.Add(contour);
        }

        private static Vector2 Reflect(Vector2 point, Vector2 control)
        {
            return new Vector2(2f * point.X - control.X, 2f * point.Y - control.Y);
        }

        private static void FlattenCubic(List<Vector2> output, Vector2 p0, Vector2 p1, Vector2 p2,
                                         Vector2 p3, float tolerance, int depth)
        {
            if (depth >= 16 || IsFlat(p0, p1, p2, p3, tolerance))
            {
                output.Add(p3);
                return;
            }

            Vector2 p01 = Mid(p0, p1);
            Vector2 p12 = Mid(p1, p2);
            Vector2 p23 = Mid(p2, p3);
            Vector2 p012 = Mid(p01, p12);
            Vector2 p123 = Mid(p12, p23);
            Vector2 p0123 = Mid(p012, p123);

            FlattenCubic(output, p0, p01, p012, p0123, tolerance, depth + 1);
            FlattenCubic(output, p0123, p123, p23, p3, tolerance, depth + 1);
        }

        private static void FlattenQuadratic(List<Vector2> output, Vector2 p0, Vector2 p1, Vector2 p2,
                                             float tolerance, int depth)
        {
            // A quadratic is the cubic with both controls at the quadratic control point.
            FlattenCubic(output, p0, p0 + (p1 - p0) * (2f / 3f), p2 + (p1 - p2) * (2f / 3f), p2,
                         tolerance, depth);
        }

        private static bool IsFlat(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance)
        {
            float dx = p3.X - p0.X;
            float dy = p3.Y - p0.Y;
            float len2 = dx * dx + dy * dy;

            if (len2 < 1e-12f)
            {
                return (p1 - p0).LengthSquared() <= tolerance * tolerance
                    && (p2 - p0).LengthSquared() <= tolerance * tolerance;
            }

            float d1 = Math.Abs((p1.X - p0.X) * dy - (p1.Y - p0.Y) * dx) / (float)Math.Sqrt(len2);
            float d2 = Math.Abs((p2.X - p0.X) * dy - (p2.Y - p0.Y) * dx) / (float)Math.Sqrt(len2);
            return d1 <= tolerance && d2 <= tolerance;
        }

        private static Vector2 Mid(Vector2 a, Vector2 b)
        {
            return new Vector2((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
        }

        /// <summary>
        /// Elliptical arc, SVG implementation notes F.6.5: endpoint parameterisation is turned
        /// into centre parameterisation and each sweep of at most 90 degrees becomes one cubic.
        /// </summary>
        private static void FlattenArc(List<Vector2> output, Vector2 start, float rx, float ry,
                                       float rotationDegrees, bool largeArc, bool sweep, Vector2 end,
                                       float tolerance)
        {
            if ((start - end).LengthSquared() < 1e-12f) return;

            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx < 1e-6f || ry < 1e-6f)
            {
                output.Add(end);
                return;
            }

            double phi = rotationDegrees * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi);
            double sinPhi = Math.Sin(phi);

            double dx2 = (start.X - end.X) / 2.0;
            double dy2 = (start.Y - end.Y) / 2.0;
            double x1p = cosPhi * dx2 + sinPhi * dy2;
            double y1p = -sinPhi * dx2 + cosPhi * dy2;

            double rx2 = rx * (double)rx;
            double ry2 = ry * (double)ry;
            double lambda = (x1p * x1p) / rx2 + (y1p * y1p) / ry2;
            if (lambda > 1.0)
            {
                double s = Math.Sqrt(lambda);
                rx = (float)(rx * s);
                ry = (float)(ry * s);
                rx2 = rx * (double)rx;
                ry2 = ry * (double)ry;
            }

            double sign = largeArc == sweep ? -1.0 : 1.0;
            double numerator = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
            double denominator = rx2 * y1p * y1p + ry2 * x1p * x1p;
            double factor = denominator <= 1e-12 ? 0.0 : sign * Math.Sqrt(Math.Max(0.0, numerator / denominator));

            double cxp = factor * rx * y1p / ry;
            double cyp = -factor * ry * x1p / rx;

            double cx = cosPhi * cxp - sinPhi * cyp + (start.X + end.X) / 2.0;
            double cy = sinPhi * cxp + cosPhi * cyp + (start.Y + end.Y) / 2.0;

            double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double theta2 = Angle((x1p - cxp) / rx, (y1p - cyp) / ry,
                                  (-x1p - cxp) / rx, (-y1p - cyp) / ry);

            double delta = theta2;
            if (!sweep && delta > 0) delta -= 2.0 * Math.PI;
            else if (sweep && delta < 0) delta += 2.0 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2.0));
            if (segments < 1) segments = 1;

            double step = delta / segments;
            double k = 4.0 / 3.0 * Math.Tan(step / 4.0);

            double cosTheta = Math.Cos(theta1);
            double sinTheta = Math.Sin(theta1);

            Vector2 point = start;
            for (int i = 0; i < segments; i++)
            {
                double thetaNext = theta1 + step * (i + 1);
                double cosNext = Math.Cos(thetaNext);
                double sinNext = Math.Sin(thetaNext);

                Vector2 c1 = EllipsePoint(cx, cy, rx, ry, cosPhi, sinPhi, cosTheta - k * sinTheta, sinTheta + k * cosTheta);
                Vector2 c2 = EllipsePoint(cx, cy, rx, ry, cosPhi, sinPhi, cosNext + k * sinNext, sinNext - k * cosNext);
                Vector2 p = EllipsePoint(cx, cy, rx, ry, cosPhi, sinPhi, cosNext, sinNext);

                FlattenCubic(output, point, c1, c2, p, tolerance, 0);

                point = p;
                cosTheta = cosNext;
                sinTheta = sinNext;
            }
        }

        private static Vector2 EllipsePoint(double cx, double cy, double rx, double ry,
                                            double cosPhi, double sinPhi, double x, double y)
        {
            return new Vector2((float)(cx + rx * x * cosPhi - ry * y * sinPhi),
                               (float)(cy + rx * x * sinPhi + ry * y * cosPhi));
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            if (len < 1e-12) return 0.0;

            double value = Math.Max(-1.0, Math.Min(1.0, dot / len));
            double angle = Math.Acos(value);
            return (ux * vy - uy * vx) < 0 ? -angle : angle;
        }

        private sealed class Lexer
        {
            private readonly string _text;
            private int _index;

            public Lexer(string text)
            {
                _text = text;
                _index = 0;
            }

            public bool AtEnd
            {
                get { return _index >= _text.Length; }
            }

            public void SkipSeparators()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ',') _index++;
                    else break;
                }
            }

            public bool NextIsCommand()
            {
                SkipSeparators();
                if (_index >= _text.Length) return false;
                char c = _text[_index];
                return char.IsLetter(c);
            }

            public char ReadCommand()
            {
                SkipSeparators();
                if (_index >= _text.Length) throw new FormatException("unexpected end of path data");
                return _text[_index++];
            }

            public void ReadNumber(out float value)
            {
                SkipSeparators();
                if (_index >= _text.Length) throw new FormatException("unexpected end of path data");

                int start = _index;
                if (_text[_index] == '+' || _text[_index] == '-') _index++;

                bool digits = false;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                {
                    _index++;
                    digits = true;
                }

                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        _index++;
                        digits = true;
                    }
                }

                if (!digits) throw new FormatException("expected a number at offset " + start);

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    int exponentStart = _index;
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                    bool exponentDigits = false;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        _index++;
                        exponentDigits = true;
                    }

                    if (!exponentDigits) _index = exponentStart;
                }

                string token = _text.Substring(start, _index - start);
                value = float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
