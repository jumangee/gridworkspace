using System;
using System.Collections.Generic;
using Vgs.Core;
using Vgs.IconAtlasBuilder;
using Vgs.Ui;
using VRageMath;

namespace Vgs.LinePreview
{
    /// <summary>
    /// Renders the connection lines of ADR-0046 to a PNG, using the <b>real</b> router and the
    /// <b>real</b> rasteriser — not a drawing made by hand.
    ///
    /// <b>Why this exists.</b> The decision it illustrates is about how a line looks: whether a
    /// corner reads as a bend, whether a horizontal run and a vertical one are the same weight,
    /// whether the watch line reads as dots. The offline gate can measure all three as numbers,
    /// and it does, but a number does not tell the owner of the product whether the picture is
    /// acceptable. This program produces the picture from the same code the game runs, so the
    /// thing being judged is the thing that ships.
    ///
    /// It draws the shapes the acceptance list of the ADR names: a connection to the right, one
    /// back to the left, a block driving itself, and a dotted watch line — over the editor's own
    /// colours and at the size of a 1920×1080 window — plus a 6× crop of each bend, where a
    /// staircase would be visible if there were one.
    ///
    /// Run it with <c>pwsh -File tools/line-preview/run.ps1</c>.
    /// </summary>
    internal static class Program
    {
        private const int Width = 1920;
        private const int Height = 1080;

        private static readonly Color Canvas = new Color(18, 22, 29, 255);
        private static readonly Color Card = new Color(38, 45, 58, 255);
        private static readonly Color CardBorder = new Color(70, 84, 105, 255);
        private static readonly Color Line = new Color(120, 220, 160, 190);
        private static readonly Color WatchLine = new Color(240, 200, 90, 210);
        private static readonly Color PortBar = new Color(28, 34, 44, 255);

        private static byte[] _rgba;

        /// <summary>
        /// Window pixels from the safe area's pixel grid: the safe-GUI rectangle is centred, so
        /// its origin is to the right of the window's. The game's sprite call applies this shift
        /// implicitly; the preview has no safe rectangle to resolve against, so it applies it
        /// when it writes pixels.
        /// </summary>
        private static int _paintOffsetX;

        /// <summary>
        /// The same client → pixel mapping the rasteriser uses, kept for the cards so that a
        /// card and the line that leaves it are placed by one rule rather than two.
        /// </summary>
        private static PixelTransform _transform;

        public static int Main(string[] args)
        {
            string outPath = args != null && args.Length > 0 ? args[0] : "line-preview.png";

            _rgba = new byte[Width * Height * 4];
            Fill(Canvas);

            // The transform the draw layer builds for a 1920×1080 window: the safe-GUI rectangle
            // is min(W, 4H/3) wide and centred, so its pixel grid starts 240 px inside the
            // window. This is the real thing, not the identity — the preview exercises the same
            // mapping the editor uses, including the offset that is easy to get wrong by a sign.
            const float safeWidth = 1440f;
            const float safeLeft = (Width - safeWidth) * 0.5f;

            var transform = PixelTransform.FromClientTransform(
                Width / safeWidth, -safeLeft / safeWidth, Width,
                1f, 0f, Height);

            // The rasteriser emits pixels in the safe area's grid, whose origin is the safe
            // area's own left edge; the window starts safeLeft to the left of it. The game's
            // sprite call applies this shift implicitly (the API resolves 0..1 against the safe
            // rectangle), so the preview applies it explicitly when it writes pixels.
            _transform = transform;
            _paintOffsetX = (int)Math.Round(-transform.Offset.X);

            // The clip is the safe area itself: in its own pixel grid that is 0..safeWidth.
            var clip = new Vector4(0f, 0f, safeWidth, Height);

            var route = new List<Vector4>(8);
            var path = new List<Vector2>(64);
            var pieces = new List<Vector4>(256);

            // ---- 1. target to the right and below: the three-segment case from the
            // requirement, where the line must read as straight, bend, bend, straight ----
            Vector2 from = new Vector2(0.30f, 0.14f);
            Vector2 to = new Vector2(0.58f, 0.28f);
            EdgeRouting.Build(from, to, VgsConstants.LineGutter, route);
            EdgeRouting.Round(route, new Vector2(Width, Height), path);
            LineRaster.Dotted(path, transform, VgsConstants.LineDotPixels, VgsConstants.LineDotGapPixels, clip, pieces);
            DrawCards(from, to);
            Paint(pieces, Line);

            // ---- 2. target to the left: the S, which now has two bends ----
            from = new Vector2(0.58f, 0.44f);
            to = new Vector2(0.30f, 0.58f);
            EdgeRouting.Build(from, to, VgsConstants.LineGutter, route);
            EdgeRouting.Round(route, new Vector2(Width, Height), path);
            LineRaster.Dotted(path, transform, VgsConstants.LineDotPixels, VgsConstants.LineDotGapPixels, clip, pieces);
            DrawCards(from, to);
            Paint(pieces, Line);

            // ---- 3. a block driving itself: the loop around the card ----
            var cardTopLeft = new Vector2(0.68f, 0.12f);
            var cardSize = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
            from = new Vector2(cardTopLeft.X + cardSize.X - 0.012f, cardTopLeft.Y + 0.024f);
            to = new Vector2(cardTopLeft.X + 0.012f, cardTopLeft.Y + 0.042f);
            EdgeRouting.BuildSelfLoop(from, to, cardTopLeft, cardSize, VgsConstants.LineGutter, route);
            EdgeRouting.Round(route, new Vector2(Width, Height), path);
            LineRaster.Dotted(path, transform, VgsConstants.LineDotPixels, VgsConstants.LineDotGapPixels, clip, pieces);
            DrawCard(cardTopLeft, cardSize);
            Paint(pieces, Line);

            // ---- 4. a watch line: dots, not dashes ----
            from = new Vector2(0.34f, 0.72f);
            to = new Vector2(0.62f, 0.86f);
            EdgeRouting.Build(from, to, VgsConstants.LineGutter, route);
            EdgeRouting.Round(route, new Vector2(Width, Height), path);
            LineRaster.Dotted(path, transform, VgsConstants.WatchDotPixels,
                              VgsConstants.WatchDotGapPixels, clip, pieces);
            DrawCards(from, to);
            Paint(pieces, WatchLine);

            PngWriter.WriteRgba(outPath, _rgba, Width, Height);
            Console.WriteLine("wrote " + outPath + " (" + Width + "x" + Height + ")");

            // ---- 5. a 6× crop of one bend, where a staircase would show ----
            WriteBendCrop(outPath.Replace(".png", "-bend.png"), 6);

            // ---- 6. the three line styles side by side, at 1:1 and at 8× ----
            //
            // This is the picture the decision of ADR-0047 is judged by: the solid line the owner
            // rejected as "thicker than 2 px", the dense rhythm that replaced it, and the open
            // rhythm of a watch line. Same ink height in all three; only the rhythm differs.
            WriteStyleComparison(outPath.Replace(".png", "-styles.png"));

            ReportCost(transform, clip);
            return 0;
        }

        /// <summary>
        /// Renders the same straight run three times — solid, dense dots, open dots — at 1:1 in
        /// the top half and at 8× in the bottom half, so the weight and the rhythm can be compared
        /// rather than argued about.
        /// </summary>
        private static void WriteStyleComparison(string outPath)
        {
            const int stripWidth = 420;
            const int stripHeight = 96;
            const int zoom = 8;

            var rgba = new byte[stripWidth * stripHeight * 4];
            for (int i = 0; i < stripWidth * stripHeight; i++)
            {
                rgba[i * 4] = Canvas.R;
                rgba[i * 4 + 1] = Canvas.G;
                rgba[i * 4 + 2] = Canvas.B;
                rgba[i * 4 + 3] = 255;
            }

            var transform = new PixelTransform(new Vector2(stripWidth, stripHeight), Vector2.Zero);
            var clip = new Vector4(0f, 0f, stripWidth, stripHeight);

            // Three runs, 24 px apart: solid, dense, open.
            var rows = new[]
            {
                new Vector4(20f, 20f, stripWidth - 20f, 20f),
                new Vector4(20f, 48f, stripWidth - 20f, 48f),
                new Vector4(20f, 76f, stripWidth - 20f, 76f)
            };

            var pieces = new List<Vector4>(512);
            var scratch = new List<Vector4>(512);

            // Solid: the same weight as the dots, with no gaps — what the owner rejected.
            SolidRun(rows[0], VgsConstants.LineDotPixels, pieces);
            PaintInto(rgba, stripWidth, stripHeight, pieces, Line);

            // Dense: a connection (ADR-0047).
            DottedRun(rows[1], VgsConstants.LineDotPixels, VgsConstants.LineDotGapPixels,
                      clip, scratch);
            PaintInto(rgba, stripWidth, stripHeight, scratch, Line);

            // Open: a watch line (ADR-0017).
            DottedRun(rows[2], VgsConstants.WatchDotPixels, VgsConstants.WatchDotGapPixels,
                      clip, scratch);
            PaintInto(rgba, stripWidth, stripHeight, scratch, Line);

            int bigWidth = stripWidth * zoom;
            int bigHeight = stripHeight * zoom;
            var big = new byte[bigWidth * bigHeight * 4];

            for (int y = 0; y < bigHeight; y++)
            {
                for (int x = 0; x < bigWidth; x++)
                {
                    int src = ((y / zoom) * stripWidth + (x / zoom)) * 4;
                    int dst = (y * bigWidth + x) * 4;
                    big[dst] = rgba[src];
                    big[dst + 1] = rgba[src + 1];
                    big[dst + 2] = rgba[src + 2];
                    big[dst + 3] = rgba[src + 3];
                }
            }

            PngWriter.WriteRgba(outPath, big, bigWidth, bigHeight);
            Console.WriteLine("wrote " + outPath + " (" + bigWidth + "x" + bigHeight + ")");
        }

        /// <summary>
        /// A solid run of the given pixel height — the pre-ADR-0047 look, kept here so the
        /// comparison picture has something to compare against.
        /// </summary>
        private static void SolidRun(Vector4 run, float heightPixels, List<Vector4> into)
        {
            into.Clear();

            float top = (float)Math.Round(run.Y - heightPixels * 0.5f);
            into.Add(new Vector4(run.X, top, run.Z, top + (float)Math.Round(heightPixels)));
        }

        /// <summary>
        /// A dotted run through the real rasteriser, along the same straight line. The strip is
        /// already in pixels, so the path is written in "one unit per pixel" and the transform is
        /// the identity.
        /// </summary>
        private static void DottedRun(Vector4 run, float dotPixels, float gapPixels, Vector4 clip,
                                      List<Vector4> into)
        {
            var path = new List<Vector2>
            {
                new Vector2(run.X, run.Y),
                new Vector2(run.Z, run.W)
            };

            var identity = new PixelTransform(new Vector2(1f, 1f), Vector2.Zero);
            LineRaster.Dotted(path, identity, dotPixels, gapPixels, clip, into);
        }

        /// <summary>Writes one list of pixel rectangles into a raw RGBA buffer.</summary>
        private static void PaintInto(byte[] rgba, int width, int height, List<Vector4> pieces,
                                      Color colour)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Vector4 piece = pieces[i];
                for (int y = (int)piece.Y; y < (int)piece.W; y++)
                {
                    for (int x = (int)piece.X; x < (int)piece.Z; x++)
                    {
                        if (x < 0 || y < 0 || x >= width || y >= height) continue;
                        Put(rgba, width, x, y, colour);
                    }
                }
            }
        }

        /// <summary>
        /// Prints how many rectangles one connection costs, at the sizes the editor really draws
        /// — the number the frame budget in ADR-0046 is argued from.
        ///
        /// It is a measurement, not a guess: the shapes are the routes the router produces for
        /// cards at their real positions, and the counts come from the rasteriser itself.
        /// </summary>
        private static void ReportCost(PixelTransform transform, Vector4 clip)
        {
            var route = new List<Vector4>(8);
            var path = new List<Vector2>(64);
            var pieces = new List<Vector4>(256);

            string[] names =
            {
                "level, target right",
                "target right and below",
                "target left (S)",
                "self-loop",
                "watch line (dotted)"
            };

            Vector2[,] ends =
            {
                { new Vector2(0.30f, 0.20f), new Vector2(0.64f, 0.20f) },
                { new Vector2(0.30f, 0.20f), new Vector2(0.64f, 0.36f) },
                { new Vector2(0.64f, 0.52f), new Vector2(0.30f, 0.68f) },
                { new Vector2(0f, 0f), new Vector2(0f, 0f) },
                { new Vector2(0.34f, 0.72f), new Vector2(0.62f, 0.86f) }
            };

            Console.WriteLine();
            Console.WriteLine("rectangles per line (1920x1080, the sizes the editor draws):");

            for (int i = 0; i < names.Length; i++)
            {
                Vector2 from = ends[i, 0];
                Vector2 to = ends[i, 1];

                if (i == 3)
                {
                    var cardTopLeft = new Vector2(0.68f, 0.18f);
                    var cardSize = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
                    from = new Vector2(cardTopLeft.X + cardSize.X - 0.012f, cardTopLeft.Y + 0.024f);
                    to = new Vector2(cardTopLeft.X + 0.012f, cardTopLeft.Y + 0.042f);
                    EdgeRouting.BuildSelfLoop(from, to, cardTopLeft, cardSize,
                                              VgsConstants.LineGutter, route);
                }
                else
                {
                    EdgeRouting.Build(from, to, VgsConstants.LineGutter, route);
                }

                EdgeRouting.Round(route, new Vector2(Width, Height), path);

                if (i == 4)
                {
                    LineRaster.Dotted(path, transform, VgsConstants.WatchDotPixels,
                                      VgsConstants.WatchDotGapPixels, clip, pieces);
                }
                else
                {
                    LineRaster.Dotted(path, transform, VgsConstants.LineDotPixels,
                                      VgsConstants.LineDotGapPixels, clip, pieces);
                }

                Console.WriteLine("  " + names[i].PadRight(24) + route.Count + " segment(s), "
                                  + path.Count + " path point(s), " + pieces.Count + " rectangle(s)");
            }
        }

        /// <summary>
        /// Renders one connection to a small canvas and scales it up, nearest neighbour, so that
        /// every pixel of the rasteriser is visible. This is the picture that answers "is the bend
        /// smooth, or is it a staircase?" — at 1:1 a six-pixel chord is invisible.
        /// </summary>
        private static void WriteBendCrop(string outPath, int zoom)
        {
            const int smallWidth = 160;
            const int smallHeight = 90;

            var rgba = new byte[smallWidth * smallHeight * 4];
            for (int i = 0; i < smallWidth * smallHeight; i++)
            {
                rgba[i * 4] = Canvas.R;
                rgba[i * 4 + 1] = Canvas.G;
                rgba[i * 4 + 2] = Canvas.B;
                rgba[i * 4 + 3] = 255;
            }

            var transform = new PixelTransform(new Vector2(smallWidth, smallHeight), Vector2.Zero);
            var clip = new Vector4(0f, 0f, smallWidth, smallHeight);

            var route = new List<Vector4>(8);
            var path = new List<Vector2>(64);
            var pieces = new List<Vector4>(256);

            EdgeRouting.Build(new Vector2(0.10f, 0.20f), new Vector2(0.80f, 0.70f), 0.02f, route);
            EdgeRouting.Round(route, new Vector2(smallWidth, smallHeight), path);
            LineRaster.Dotted(path, transform, VgsConstants.LineDotPixels, VgsConstants.LineDotGapPixels, clip, pieces);

            for (int i = 0; i < pieces.Count; i++)
            {
                Vector4 piece = pieces[i];
                for (int y = (int)piece.Y; y < (int)piece.W; y++)
                {
                    for (int x = (int)piece.X; x < (int)piece.Z; x++)
                    {
                        if (x < 0 || y < 0 || x >= smallWidth || y >= smallHeight) continue;
                        Put(rgba, smallWidth, x, y, Line);
                    }
                }
            }

            int bigWidth = smallWidth * zoom;
            int bigHeight = smallHeight * zoom;
            var big = new byte[bigWidth * bigHeight * 4];

            for (int y = 0; y < bigHeight; y++)
            {
                for (int x = 0; x < bigWidth; x++)
                {
                    int sx = x / zoom;
                    int sy = y / zoom;
                    int src = (sy * smallWidth + sx) * 4;
                    int dst = (y * bigWidth + x) * 4;
                    big[dst] = rgba[src];
                    big[dst + 1] = rgba[src + 1];
                    big[dst + 2] = rgba[src + 2];
                    big[dst + 3] = rgba[src + 3];
                }
            }

            PngWriter.WriteRgba(outPath, big, bigWidth, bigHeight);
            Console.WriteLine("wrote " + outPath + " (" + bigWidth + "x" + bigHeight + ")");
        }

        /// <summary>
        /// The two cards a line joins, at their real size, with the port bars the dots sit in —
        /// so the picture shows where a line starts and that it is not hidden under the bar.
        ///
        /// <paramref name="from"/> is an output dot: it sits inside the source card's right-hand
        /// bar. <paramref name="to"/> is an input dot: the left-hand bar of the target card.
        /// </summary>
        private static void DrawCards(Vector2 from, Vector2 to)
        {
            DrawCardAt(from, true);
            DrawCardAt(to, false);
        }

        private static void DrawCardAt(Vector2 dot, bool trailing)
        {
            var size = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
            var topLeft = new Vector2(trailing ? dot.X - size.X + 0.012f : dot.X - 0.012f,
                                      dot.Y - 0.016f);
            DrawCard(topLeft, size);
        }

        private static void DrawCard(Vector2 topLeft, Vector2 size)
        {
            FillRect(ToPixels(topLeft), SizeToPixels(size), Card);

            var bar = new Vector2(size.X, 0.012f);
            FillRect(ToPixels(new Vector2(topLeft.X, topLeft.Y + size.Y)), SizeToPixels(bar),
                     PortBar);

            // Frame: one pixel, the same rule the editor's borders follow.
            var left = ToPixels(topLeft).X;
            var top = ToPixels(topLeft).Y;
            var right = ToPixels(topLeft + size).X;
            var bottom = ToPixels(topLeft + size).Y;
            for (int x = (int)left; x < (int)right; x++)
            {
                PutPixel(x, (int)top, CardBorder);
                PutPixel(x, (int)bottom, CardBorder);
            }
            for (int y = (int)top; y < (int)bottom; y++)
            {
                PutPixel((int)left, y, CardBorder);
                PutPixel((int)right, y, CardBorder);
            }
        }

        private static void Paint(List<Vector4> pieces, Color colour)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Vector4 piece = pieces[i];
                FillRect(new Vector2(piece.X, piece.Y),
                         new Vector2(piece.Z - piece.X, piece.W - piece.Y), colour);
            }
        }

        private static Vector2 ToPixels(Vector2 point)
        {
            return _transform.ToPixels(point);
        }

        /// <summary>
        /// A size in pixels. A size has no origin, so it takes the scale and none of the offset —
        /// the same distinction <c>VgsScreenDraw.ToApiSize</c> makes.
        /// </summary>
        private static Vector2 SizeToPixels(Vector2 size)
        {
            return new Vector2(size.X * _transform.Scale.X, size.Y * _transform.Scale.Y);
        }

        private static void FillRect(Vector2 position, Vector2 size, Color colour)
        {
            int left = (int)Math.Round(position.X);
            int top = (int)Math.Round(position.Y);
            int right = (int)Math.Round(position.X + size.X);
            int bottom = (int)Math.Round(position.Y + size.Y);

            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    PutPixel(x, y, colour);
                }
            }
        }

        private static void PutPixel(int x, int y, Color colour)
        {
            x += _paintOffsetX;
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            Put(_rgba, Width, x, y, colour);
        }

        private static void Put(byte[] rgba, int width, int x, int y, Color colour)
        {
            int at = (y * width + x) * 4;
            rgba[at] = colour.R;
            rgba[at + 1] = colour.G;
            rgba[at + 2] = colour.B;
            rgba[at + 3] = 255;
        }

        private static void Fill(Color colour)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                _rgba[i * 4] = colour.R;
                _rgba[i * 4 + 1] = colour.G;
                _rgba[i * 4 + 2] = colour.B;
                _rgba[i * 4 + 3] = 255;
            }
        }
    }
}
