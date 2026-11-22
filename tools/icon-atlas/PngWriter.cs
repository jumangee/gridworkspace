using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Vgs.IconAtlasBuilder
{
    /// <summary>
    /// Writes an icon as a PNG the game loads as a GUI texture.
    ///
    /// <b>Why PNG and not DDS.</b> The first implementation shipped DXT5 DDS files, copied from
    /// the shape of the game's own <c>IconCross.dds</c>. They were correct on disk — Pillow and
    /// the project's own decoder both read clean alpha at every mip level — and they still drew
    /// as fully opaque squares in the editor's sprite path, with the icon's shape faintly visible
    /// on top. Rebuilding the same icons as PNG, with no other change, made them render correctly.
    /// So the DDS route is broken for <c>MyScreenSpaceSprite</c> in a way that is invisible from
    /// the file: the header, the alpha and the mip chain can all be right and the sprite still
    /// comes out opaque.
    ///
    /// PNG is also the safer format on its own terms: lossless, so the alpha the rasteriser
    /// produces is the alpha the GPU receives, with no block quantisation to review and no mip
    /// chain to bleed.
    ///
    /// The encoder is deliberately minimal — 8-bit RGBA, one <c>IDAT</c>, no interlacing, filter
    /// type 0 on every row. Anything more would be untested code standing between the rasteriser
    /// and the file.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Result of a write, reported so the build can be checked instead of trusted.</summary>
        public struct Report
        {
            /// <summary>Size of the written file in bytes.</summary>
            public int Bytes;
        }

        /// <summary>
        /// Writes <paramref name="alpha"/> (row-major, <paramref name="width"/>×<paramref name="height"/>)
        /// as white pixels with that alpha, straight (not premultiplied).
        /// </summary>
        public static Report Write(string path, byte[] alpha, int width, int height)
        {
            if (alpha == null) throw new ArgumentNullException("alpha");
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");
            if (alpha.Length != width * height) throw new ArgumentException("alpha length mismatch");

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.Write(Signature, 0, Signature.Length);

                WriteChunk(stream, "IHDR", BuildHeader(width, height));
                WriteChunk(stream, "IDAT", BuildImageData(alpha, width, height));
                WriteChunk(stream, "IEND", new byte[0]);

                stream.Flush();
                return new Report { Bytes = (int)stream.Length };
            }
        }

        private static byte[] BuildHeader(int width, int height)
        {
            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;   // bit depth
            header[9] = 6;   // colour type: truecolour with alpha
            header[10] = 0;  // compression: deflate
            header[11] = 0;  // filter method: adaptive
            header[12] = 0;  // interlace: none
            return header;
        }

        /// <summary>
        /// Scanlines, each prefixed with filter type 0, deflated into a zlib stream.
        ///
        /// White RGB with straight alpha is what the sprite shader wants: it multiplies the sample
        /// by its own alpha, so premultiplied bytes would darken every edge twice.
        /// </summary>
        private static byte[] BuildImageData(byte[] alpha, int width, int height)
        {
            int stride = width * 4;
            var raw = new byte[height * (stride + 1)];

            int at = 0;
            for (int y = 0; y < height; y++)
            {
                raw[at++] = 0; // filter: none

                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    raw[at++] = 255;
                    raw[at++] = 255;
                    raw[at++] = 255;
                    raw[at++] = alpha[row + x];
                }
            }

            byte[] deflated;
            using (var buffer = new MemoryStream())
            {
                using (var deflate = new DeflateStream(buffer, CompressionMode.Compress, true))
                {
                    deflate.Write(raw, 0, raw.Length);
                }

                deflated = buffer.ToArray();
            }

            // zlib wrapper: two header bytes, the deflate stream, and an Adler-32 of the input.
            var zlib = new byte[2 + deflated.Length + 4];
            zlib[0] = 0x78; // deflate, 32K window
            zlib[1] = 0x01; // no preset dictionary, fastest level
            Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
            WriteBigEndian(zlib, 2 + deflated.Length, Adler32(raw));

            return zlib;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var header = new byte[8];
            WriteBigEndian(header, 0, (uint)data.Length);

            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            Buffer.BlockCopy(typeBytes, 0, header, 4, 4);

            stream.Write(header, 0, 8);
            if (data.Length > 0) stream.Write(data, 0, data.Length);

            uint crc = Crc32(0xFFFFFFFFu, typeBytes, 0, 4);
            crc = Crc32(crc, data, 0, data.Length) ^ 0xFFFFFFFFu;

            var trailer = new byte[4];
            WriteBigEndian(trailer, 0, crc);
            stream.Write(trailer, 0, 4);
        }

        private static uint[] _crcTable;

        private static uint[] CrcTable()
        {
            if (_crcTable != null) return _crcTable;

            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            _crcTable = table;
            return table;
        }

        private static uint Crc32(uint crc, byte[] data, int offset, int count)
        {
            uint[] table = CrcTable();
            for (int i = 0; i < count; i++)
            {
                crc = table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint Adler32(byte[] data)
        {
            const uint Modulus = 65521;
            uint a = 1, b = 0;

            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % Modulus;
                b = (b + a) % Modulus;
            }

            return (b << 16) | a;
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
