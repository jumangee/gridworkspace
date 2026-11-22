using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Vgs.IconAtlasBuilder
{
    /// <summary>
    /// Builds the icon textures, their SBC registration and the generated C# table from the SVG
    /// sources next to it.
    ///
    /// Why a build tool and not code inside the mod: a Space Engineers mod is compiled by the
    /// game from <c>.cs</c> files and cannot ship a library, so anything the mod needs at run
    /// time has to be small and provably correct. Rasterising curves is neither, while doing it
    /// once on a build machine and shipping the result costs the mod nothing at all.
    ///
    /// <b>One texture per icon, not one atlas.</b> An atlas is the usual answer and it was the
    /// first one here, but it does not survive mipmapping: at the level the GPU samples for a
    /// 15-pixel icon, the transparent margin between two cells is one texel wide, and a box
    /// filter across that boundary mixes a neighbour's ink into the edge of the cell being drawn.
    /// Measured, with the numbers recorded in ADR-0019. With a texture per icon the sprite
    /// sampler clamps at the texture edge — verified in the game's IL, ADR-0019 §4a — where the
    /// margin is transparent by construction, so there is nothing to bleed into. The cost is
    /// three small files instead of one; the benefit is that the guarantee is structural rather
    /// than a matter of enough padding.
    ///
    /// The tool is self-contained: it references no game assembly and needs no Python, so the
    /// textures can be rebuilt on any machine with a .NET Framework compiler.
    /// </summary>
    public static class AtlasBuilder
    {
        /// <summary>Side of one icon texture, in pixels.</summary>
        private const int TextureSize = 256;

        /// <summary>
        /// Transparent margin between the ink and the edge of its texture, in atlas pixels.
        ///
        /// It exists so that the bilinear fetch at the edge of the drawn quad, and the coarse
        /// mips, land on transparency rather than on the icon itself. At the smallest size the
        /// editor draws (9.6 px from a 256 px texture) the GPU samples mip 4, where this margin
        /// is 0.5 texel — the ink still stops short of the edge, and beyond the edge the sampler
        /// clamps to a transparent texel.
        /// </summary>
        private const int Inset = 8;

        private const int SubSamples = 8;

        /// <summary>
        /// Side of one catalogue preview tile, in pixels. The preview exists because the texture
        /// itself is white ink on transparency: every Markdown viewer paints on white, so a table
        /// of the shipped PNGs would be a table of blank cells.
        /// </summary>
        private const int PreviewSize = 96;

        /// <summary>Margin of a preview tile, so the icon's ink is 64 of the 96 pixels.</summary>
        private const int PreviewInset = 16;

        /// <summary>Preview background: the editor canvas colour (VgsEditorScreen.ColCanvas).</summary>
        private static readonly byte[] PreviewBackground = { 18, 22, 29 };

        /// <summary>Preview ink: the card button's resting colour (ColPlusFill).</summary>
        private static readonly byte[] PreviewInk = { 244, 247, 252 };

        /// <summary>One line of <c>icons.txt</c>: the C# name, the SVG file, and what it means.</summary>
        private sealed class Entry
        {
            public string Id;
            public string File;
            public string Comment;
            public float TextureScale;
        }

        public static int Main(string[] args)
        {
            string repo = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

            // The output root is normally the repository itself. It is a parameter so that
            // `build-icon-atlas.ps1 -Verify` can rebuild into a scratch folder and compare, which
            // is the only way to notice that someone edited icons.txt and forgot to rebuild.
            string outRoot = args.Length > 1 && args[1].Length > 0 ? args[1] : repo;

            string toolDir = Path.Combine(repo, @"tools\icon-atlas");
            string iconDir = Path.Combine(toolDir, "icons");
            string manifest = Path.Combine(toolDir, "icons.txt");

            string textureDir = Path.Combine(outRoot, @"src\VGS.Mod\Textures\VGS\Icons");
            string sbcPath = Path.Combine(outRoot, @"src\VGS.Mod\Data\GuiTextures.sbc");
            string codePath = Path.Combine(outRoot, @"src\VGS.Mod\Data\Scripts\VGS\Ui\VgsIconAtlas.g.cs");
            string previewDir = Path.Combine(outRoot, @"docs\icons");

            if (!File.Exists(manifest))
            {
                Console.Error.WriteLine("manifest not found: " + manifest);
                return 2;
            }

            List<Entry> entries = ReadManifest(manifest);

            Directory.CreateDirectory(textureDir);
            Directory.CreateDirectory(Path.GetDirectoryName(sbcPath));
            Directory.CreateDirectory(Path.GetDirectoryName(codePath));
            Directory.CreateDirectory(previewDir);

            Console.WriteLine("icon textures: " + entries.Count + " icon(s), " +
                              TextureSize + "x" + TextureSize + " each, inset " + Inset + " px");

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                string svgPath = Path.Combine(iconDir, entry.File);
                if (!File.Exists(svgPath))
                {
                    Console.Error.WriteLine("missing icon source: " + svgPath);
                    return 2;
                }

                string svg = File.ReadAllText(svgPath);
                float viewWidth, viewHeight;
                ParseViewBox(ExtractAttribute(svg, "viewBox"), out viewWidth, out viewHeight);
                string data = ExtractAttribute(svg, "d");

                float tolerance = 0.05f;
                IconShape shape = SvgPath.Parse(data, viewWidth, viewHeight, tolerance);
                IconRaster.Cell cell = IconRaster.Render(shape, TextureSize, Inset, SubSamples);

                entry.TextureScale = cell.TextureScale;

                string path = Path.Combine(textureDir, entry.Id + ".png");
                PngWriter.Report report = PngWriter.Write(path, cell.Alpha, TextureSize, TextureSize);

                // The catalogue preview is a second, independent rasterisation at the size a
                // reader sees it — not a downscale of the texture. A preview that merely shrank
                // the 256 px texture would hide a rasteriser defect behind the resampler.
                WritePreview(Path.Combine(previewDir, entry.Id + ".png"), shape);

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,-26} contours {2,2}  points {3,4}  ink {4:0.0}x{5:0.0} " +
                    "texture x{6:0.000}  {7:0.0} KB",
                    entry.Id, entry.File, shape.Contours.Count, shape.PointCount,
                    cell.Ink.Width, cell.Ink.Height, entry.TextureScale, report.Bytes / 1024.0));
            }

            File.WriteAllText(sbcPath, BuildSbc(entries), new UTF8Encoding(false));
            Console.WriteLine("  wrote " + Path.GetFileName(sbcPath));

            File.WriteAllText(codePath, BuildCode(entries), new UTF8Encoding(false));
            Console.WriteLine("  wrote " + Path.GetFileName(codePath));

            Console.WriteLine("  wrote " + entries.Count + " catalogue preview(s) in docs\\icons");

            Console.WriteLine("ICONS OK");
            return 0;
        }

        /// <summary>
        /// Writes the catalogue preview of one icon: the same contours, rasterised at the size a
        /// reader sees, composited over the canvas colour so the light ink is visible on any
        /// background a Markdown viewer paints.
        /// </summary>
        private static void WritePreview(string path, IconShape shape)
        {
            IconRaster.Cell cell = IconRaster.Render(shape, PreviewSize, PreviewInset, SubSamples);

            var rgba = new byte[PreviewSize * PreviewSize * 4];
            for (int i = 0; i < cell.Alpha.Length; i++)
            {
                float alpha = cell.Alpha[i] / 255f;

                rgba[i * 4] = Composite(PreviewBackground[0], PreviewInk[0], alpha);
                rgba[i * 4 + 1] = Composite(PreviewBackground[1], PreviewInk[1], alpha);
                rgba[i * 4 + 2] = Composite(PreviewBackground[2], PreviewInk[2], alpha);
                rgba[i * 4 + 3] = 255;
            }

            PngWriter.WriteRgba(path, rgba, PreviewSize, PreviewSize);
        }

        private static byte Composite(byte background, byte ink, float alpha)
        {
            float value = background + (ink - background) * alpha;
            if (value < 0f) value = 0f;
            if (value > 255f) value = 255f;
            return (byte)(value + 0.5f);
        }

        private static List<Entry> ReadManifest(string path)
        {
            var entries = new List<Entry>();
            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    Console.Error.WriteLine("icons.txt line " + (i + 1) + ": expected '<id> <file> [comment]'");
                    Environment.Exit(2);
                }

                entries.Add(new Entry
                {
                    Id = parts[0],
                    File = parts[1],
                    Comment = parts.Length > 2 ? parts[2].Trim() : parts[0]
                });
            }

            if (entries.Count == 0) throw new InvalidOperationException("icons.txt lists no icons");

            // Duplicate ids would silently overwrite one texture with another and emit a switch
            // with two identical cases, which does not compile. Catch it here, where the message
            // can name the manifest line.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                int first;
                if (seen.TryGetValue(entries[i].Id, out first))
                {
                    throw new InvalidOperationException("icons.txt lists '" + entries[i].Id +
                        "' twice (entries " + (first + 1) + " and " + (i + 1) + ")");
                }

                seen[entries[i].Id] = i;
            }

            return entries;
        }

        private static void ParseViewBox(string viewBox, out float width, out float height)
        {
            string[] parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) throw new FormatException("unsupported viewBox: " + viewBox);
            width = float.Parse(parts[2], CultureInfo.InvariantCulture);
            height = float.Parse(parts[3], CultureInfo.InvariantCulture);
        }

        private static string ExtractAttribute(string xml, string name)
        {
            Match match = Regex.Match(xml, "\\s" + name + "=\"([^\"]+)\"");
            if (!match.Success) throw new FormatException("attribute not found: " + name);
            return match.Groups[1].Value;
        }

        /// <summary>
        /// The SBC entry. <c>SubtypeName</c> is what the sprite API resolves the material by, so
        /// each icon gets its own name and its own file — one texture per icon, not a cell in a
        /// shared atlas (see the class comment for why).
        /// </summary>
        private static string BuildSbc(List<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\r\n");
            sb.Append("<!--\r\n");
            sb.Append("  Generated by tools/build-icon-atlas.ps1. Do not edit by hand.\r\n");
            sb.Append("  One SubtypeName and one texture per icon: an atlas would bleed between\r\n");
            sb.Append("  cells once the GPU samples a mip level (ADR-0019).\r\n");
            sb.Append("-->\r\n");
            sb.Append("<Definitions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\r\n");
            sb.Append("  <Definition xsi:type=\"MyObjectBuilder_GuiTextureAtlasDefinition\">\r\n");
            sb.Append("    <Id Type=\"MyObjectBuilder_GuiTextureAtlasDefinition\" Subtype=\"VGS_Icons\" />\r\n");
            sb.Append("    <Textures>\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("      <Texture>\r\n");
                sb.Append("        <SubtypeName>VGS_").Append(entries[i].Id).Append("</SubtypeName>\r\n");
                sb.Append("        <Path>Textures\\VGS\\Icons\\").Append(entries[i].Id).Append(".png</Path>\r\n");
                sb.Append("      </Texture>\r\n");
            }

            sb.Append("    </Textures>\r\n");
            sb.Append("  </Definition>\r\n");
            sb.Append("</Definitions>\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// The generated C# table: the enum the editor codes against and the numbers the drawing
        /// code needs. It is checked in so the mod builds without this tool.
        /// </summary>
        private static string BuildCode(List<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\r\n");
            sb.Append("//   Generated by tools/build-icon-atlas.ps1 from tools/icon-atlas/icons.txt.\r\n");
            sb.Append("//   Do not edit: the next atlas build overwrites this file.\r\n");
            sb.Append("//   Source images: Font Awesome Free 7.3.1, CC BY 4.0.\r\n");
            sb.Append("// </auto-generated>\r\n");
            sb.Append("using VRageMath;\r\n");
            sb.Append("\r\n");
            sb.Append("namespace Vgs.Ui\r\n");
            sb.Append("{\r\n");
            sb.Append("    /// <summary>\r\n");
            sb.Append("    /// Every icon the build produces. The name is what the editor codes against;\r\n");
            sb.Append("    /// the file behind it is an implementation detail (ADR-0019).\r\n");
            sb.Append("    /// </summary>\r\n");
            sb.Append("    public enum VgsIcon\r\n");
            sb.Append("    {\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("        /// <summary>").Append(Escape(entries[i].Comment)).Append("</summary>\r\n");
                sb.Append("        ").Append(entries[i].Id).Append(" = ").Append(i).Append(",\r\n");
            }

            sb.Append("    }\r\n");
            sb.Append("\r\n");
            sb.Append("    /// <summary>\r\n");
            sb.Append("    /// Names and sizes of the icon textures, generated from tools/icon-atlas/icons.txt.\r\n");
            sb.Append("    ///\r\n");
            sb.Append("    /// One texture per icon rather than one atlas: a shared atlas mixes neighbouring\r\n");
            sb.Append("    /// icons together once the GPU samples a mip level, and the sizes this editor draws\r\n");
            sb.Append("    /// at are exactly where that happens. See ADR-0019 for the measurement.\r\n");
            sb.Append("    /// </summary>\r\n");
            sb.Append("    public static class VgsIconAtlas\r\n");
            sb.Append("    {\r\n");
            sb.Append("        /// <summary>\r\n");
            sb.Append("        /// Prefix of every SubtypeName registered in Data\\GuiTextures.sbc. The full name\r\n");
            sb.Append("        /// is what <c>MyStringId</c> must be computed from.\r\n");
            sb.Append("        /// </summary>\r\n");
            sb.Append("        public const string MaterialPrefix = \"VGS_\";\r\n");
            sb.Append("\r\n");
            sb.Append("        /// <summary>\r\n");
            sb.Append("        /// Side of every icon texture, in pixels. All of them are square and this size,\r\n");
            sb.Append("        /// so the source rectangle of an icon is always the whole texture.\r\n");
            sb.Append("        /// </summary>\r\n");
            sb.Append("        public const int TextureSize = ").Append(TextureSize).Append(";\r\n");
            sb.Append("\r\n");
            sb.Append("        /// <summary>Number of icons the build produced.</summary>\r\n");
            sb.Append("        public const int Count = ").Append(entries.Count).Append(";\r\n");
            sb.Append("\r\n");
            sb.Append("        /// <summary>\r\n");
            sb.Append("        /// SubtypeName of <paramref name=\"icon\"/> in Data\\GuiTextures.sbc, and therefore the\r\n");
            sb.Append("        /// material name the sprite API resolves. A string literal per icon, so the caller\r\n");
            sb.Append("        /// can cache the <c>MyStringId</c> once instead of building it every frame.\r\n");
            sb.Append("        /// </summary>\r\n");
            sb.Append("        public static string MaterialName(VgsIcon icon)\r\n");
            sb.Append("        {\r\n");
            sb.Append("            switch (icon)\r\n");
            sb.Append("            {\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                sb.Append("                case VgsIcon.").Append(e.Id).Append(": return \"VGS_")
                  .Append(e.Id).Append("\";\r\n");
            }

            sb.Append("                default: return \"\";\r\n");
            sb.Append("            }\r\n");
            sb.Append("        }\r\n");
            sb.Append("\r\n");
            sb.Append("        /// <summary>\r\n");
            sb.Append("        /// Source rectangle to draw <paramref name=\"icon\"/> from. Every icon is a whole\r\n");
            sb.Append("        /// texture, so this is the full square; it exists so that a future build could\r\n");
            sb.Append("        /// pack icons differently without every call site changing.\r\n");
            sb.Append("        /// </summary>\r\n");
            sb.Append("        public static bool TryGetSource(VgsIcon icon, out Rectangle source)\r\n");
            sb.Append("        {\r\n");
            sb.Append("            if ((int)icon < 0 || (int)icon >= Count)\r\n");
            sb.Append("            {\r\n");
            sb.Append("                source = new Rectangle(0, 0, 0, 0);\r\n");
            sb.Append("                return false;\r\n");
            sb.Append("            }\r\n");
            sb.Append("\r\n");
            sb.Append("            source = new Rectangle(0, 0, TextureSize, TextureSize);\r\n");
            sb.Append("            return true;\r\n");
            sb.Append("        }\r\n");
            sb.Append("\r\n");
            sb.Append("        /// <summary>\r\n");
            sb.Append("        /// Texture side divided by ink height, for <paramref name=\"icon\"/>. The build\r\n");
            sb.Append("        /// normalises every icon by its ink and leaves a transparent margin, so the\r\n");
            sb.Append("        /// square texture is a little larger than the ink it contains; a caller that\r\n");
            sb.Append("        /// wants an ink height multiplies by this to get the size to draw.\r\n");
            sb.Append("        /// </summary>\r\n");
            sb.Append("        public static float TextureScale(VgsIcon icon)\r\n");
            sb.Append("        {\r\n");
            sb.Append("            switch (icon)\r\n");
            sb.Append("            {\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                sb.Append("                case VgsIcon.").Append(e.Id).Append(": return ")
                  .Append(e.TextureScale.ToString("0.######", CultureInfo.InvariantCulture)).Append("f;\r\n");
            }

            sb.Append("                default: return 1f;\r\n");
            sb.Append("            }\r\n");
            sb.Append("        }\r\n");
            sb.Append("    }\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
