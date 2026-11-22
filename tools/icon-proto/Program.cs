using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using VRageMath;

namespace Vgs.IconProto
{
    /// <summary>
    /// Offline harness: rasterises Font Awesome SVGs at the exact pixel sizes the VGS editor
    /// would use, and writes the alpha masks as PGM for visual inspection.
    ///
    /// It exists to answer one question with evidence instead of opinion: can a vector icon be
    /// turned into a screen-space sprite without going through a bitmap, and does it still look
    /// clean at the editor's real sizes (a port control is about 15 px at 1080p)?
    /// </summary>
    public static class Program
    {
        private sealed class IconSpec
        {
            public string Name;
            public string SvgPath;
            public string FontFile;
            public int Codepoint;
        }

        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : @"e:\Projects\!VGS";
            string outDir = args.Length > 1 ? args[1] : Path.Combine(root, @"tmp\icon-proto\out");

            string fa = Path.Combine(root, @"data\fontawesome-free-7.3.1-desktop");
            string svgDir = Path.Combine(fa, "svgs");

            var icons = new List<IconSpec>
            {
                new IconSpec { Name = "circle-dot-solid", SvgPath = Path.Combine(svgDir, @"solid\circle-dot.svg"), FontFile = Path.Combine(fa, @"otfs\Font Awesome 7 Free-Solid-900.otf"), Codepoint = 0xF192 },
                new IconSpec { Name = "circle-dot-regular", SvgPath = Path.Combine(svgDir, @"regular\circle-dot.svg"), FontFile = Path.Combine(fa, @"otfs\Font Awesome 7 Free-Regular-400.otf"), Codepoint = 0xF192 },
                new IconSpec { Name = "square-plus-regular", SvgPath = Path.Combine(svgDir, @"regular\square-plus.svg"), FontFile = Path.Combine(fa, @"otfs\Font Awesome 7 Free-Regular-400.otf"), Codepoint = 0xF0FE },
                new IconSpec { Name = "square-plus-solid", SvgPath = Path.Combine(svgDir, @"solid\square-plus.svg"), FontFile = Path.Combine(fa, @"otfs\Font Awesome 7 Free-Solid-900.otf"), Codepoint = 0xF0FE },
                new IconSpec { Name = "xmark-solid", SvgPath = Path.Combine(svgDir, @"solid\xmark.svg"), FontFile = Path.Combine(fa, @"otfs\Font Awesome 7 Free-Solid-900.otf"), Codepoint = 0xF00D }
            };

            // Every pixel size the editor can produce, not a sample: a port control's diameter is
            // 0.0135 * clientHeight * zoom, which at 1080p spans about 5 px at MinZoom to 44 px
            // at MaxZoom, and the bind button is twice that. Rasterising the whole range proves
            // the cost is bounded and lets the preview show the real ladder.
            var sizeList = new List<int>();
            for (int px = 4; px <= 96; px++) sizeList.Add(px);
            // 256 is the cell size a baked atlas would need to cover a 4K screen without
            // upscaling (the "+" button is 175 px there at maximum zoom). Rendering it lets the
            // atlas comparison in atlas_compare.py start from the same contour.
            sizeList.Add(128);
            sizeList.Add(256);
            int[] sizes = sizeList.ToArray();

            Directory.CreateDirectory(outDir);

            var report = new StringBuilder();
            report.AppendLine("icon,sizePx,contours,points,ms,inkPixels,inkRatio,areaExpected,areaActual,errorPct");

            foreach (IconSpec icon in icons)
            {
                if (!File.Exists(icon.SvgPath))
                {
                    Console.WriteLine("missing: " + icon.SvgPath);
                    continue;
                }

                string svg = File.ReadAllText(icon.SvgPath);
                string viewBox = ExtractAttribute(svg, "viewBox");
                string data = ExtractAttribute(svg, "d");

                float vbW, vbH;
                ParseViewBox(viewBox, out vbW, out vbH);

                Console.WriteLine();
                Console.WriteLine("== " + icon.Name + " == viewBox " + viewBox + ", path " + data.Length + " chars");

                foreach (int size in sizes)
                {
                    float tolerance = 0.1f * vbH / size;
                    var watch = Stopwatch.StartNew();

                    IconShape shape = SvgPath.Parse(data, vbW, vbH, tolerance);
                    byte[] alpha = IconRaster.RenderAlpha(shape, size, 8);

                    watch.Stop();

                    int ink = 0;
                    long alphaSum = 0;
                    for (int i = 0; i < alpha.Length; i++)
                    {
                        if (alpha[i] > 0) ink++;
                        alphaSum += alpha[i];
                    }

                    // Signed contour area, in viewBox units: with the non-zero rule the holes
                    // come out negative, so the sum is the filled area the rasteriser must
                    // reproduce. Comparing the two catches a wrong arc sweep or a lost hole.
                    double signedArea = SignedArea(shape);
                    double scale = Math.Min(size / vbW, size / vbH);
                    double expected = Math.Abs(signedArea) * scale * scale;
                    double actual = alphaSum / 255.0;
                    double error = expected > 0 ? (actual - expected) / expected : 0.0;

                    string file = Path.Combine(outDir, icon.Name + "-" + size + ".pgm");
                    WritePgm(file, alpha, size, size);

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4:0.00},{5},{6:0.000},{7:0.0},{8:0.0},{9:+0.0;-0.0}",
                        icon.Name, size, shape.Contours.Count, shape.PointCount,
                        watch.Elapsed.TotalMilliseconds, ink, ink / (double)(size * size),
                        expected, actual, 100.0 * error));

                    if (size == 15 || size == 29)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  {0,3} px: {1,4} points, {2,6:0.00} ms, ink {3:0.0}%, area {4:0.0} vs {5:0.0} ({6:+0.0;-0.0}%)",
                            size, shape.PointCount, watch.Elapsed.TotalMilliseconds,
                            100.0 * ink / (size * size), expected, actual, 100.0 * error));
                    }
                }
            }

            string reportPath = Path.Combine(outDir, "report.csv");
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine();
            Console.WriteLine("wrote " + reportPath);
            return 0;
        }

        /// <summary>
        /// Shoelace area over every contour, with the sign kept: outer contours come out one
        /// way and holes the other, so their sum is the net filled area the non-zero rule
        /// produces. Used as an independent check on the rasteriser — the two agree only if
        /// the arcs sweep the right way and the holes are actually holes.
        /// </summary>
        private static double SignedArea(IconShape shape)
        {
            double sum = 0.0;
            for (int c = 0; c < shape.Contours.Count; c++)
            {
                List<Vector2> contour = shape.Contours[c];
                for (int i = 0; i < contour.Count; i++)
                {
                    Vector2 a = contour[i];
                    Vector2 b = contour[(i + 1) % contour.Count];
                    sum += (double)a.X * b.Y - (double)b.X * a.Y;
                }
            }
            return sum * 0.5;
        }

        private static void ParseViewBox(string viewBox, out float width, out float height)
        {
            string[] parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            width = float.Parse(parts[2], CultureInfo.InvariantCulture);
            height = float.Parse(parts[3], CultureInfo.InvariantCulture);
        }

        private static string ExtractAttribute(string xml, string name)
        {
            int at = xml.IndexOf(name + "=\"", StringComparison.Ordinal);
            if (at < 0) throw new FormatException("attribute not found: " + name);
            at += name.Length + 2;
            int end = xml.IndexOf('"', at);
            return xml.Substring(at, end - at);
        }

        private static void WritePgm(string path, byte[] pixels, int width, int height)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] header = Encoding.ASCII.GetBytes("P5\n" + width + " " + height + "\n255\n");
                stream.Write(header, 0, header.Length);
                stream.Write(pixels, 0, pixels.Length);
            }
        }
    }
}
