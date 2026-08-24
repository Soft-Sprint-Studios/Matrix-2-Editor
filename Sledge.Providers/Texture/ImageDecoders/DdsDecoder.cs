using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Sledge.Providers.Texture.ImageDecoders
{
    public static class DdsDecoder
    {
        private const uint DDS_MAGIC = 0x20534444; // "DDS "
        private const uint FOURCC_DXT1 = 0x31545844;
        private const uint FOURCC_DXT3 = 0x33545844;
        private const uint FOURCC_DXT5 = 0x35545844;

        public static Bitmap Decode(Stream stream)
        {
            using var br = new BinaryReader(stream);
            if (br.ReadUInt32() != DDS_MAGIC) return null;

            var size = br.ReadUInt32();
            var flags = br.ReadUInt32();
            var height = (int)br.ReadUInt32();
            var width = (int)br.ReadUInt32();
            var pitchOrLinearSize = br.ReadUInt32();
            var depth = br.ReadUInt32();
            var mipmapCount = br.ReadUInt32();
            br.ReadBytes(44); // Reserved1

            // PixelFormat
            var pfSize = br.ReadUInt32();
            var pfFlags = br.ReadUInt32();
            var fourCC = br.ReadUInt32();
            var rgbBitCount = br.ReadUInt32();
            var rBitMask = br.ReadUInt32();
            var gBitMask = br.ReadUInt32();
            var bBitMask = br.ReadUInt32();
            var aBitMask = br.ReadUInt32();

            br.ReadBytes(20); // Caps, Caps2, Reserved2

            byte[] pixelData = new byte[width * height * 4];

            if (fourCC == FOURCC_DXT1)
            {
                DecodeDXT1(br, width, height, pixelData);
            }
            else if (fourCC == FOURCC_DXT5)
            {
                DecodeDXT5(br, width, height, pixelData);
            }
            else if (fourCC == 0 && (rgbBitCount == 32 || rgbBitCount == 24))
            {
                int bpp = (int)rgbBitCount / 8;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte b = br.ReadByte();
                        byte g = br.ReadByte();
                        byte r = br.ReadByte();
                        byte a = bpp == 4 ? br.ReadByte() : (byte)255;
                        int idx = (y * width + x) * 4;
                        pixelData[idx + 0] = b;
                        pixelData[idx + 1] = g;
                        pixelData[idx + 2] = r;
                        pixelData[idx + 3] = a;
                    }
                }
            }
            else
            {
                return null; // Unsupported DDS format
            }

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixelData, 0, bmpData.Scan0, pixelData.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        private static void DecodeDXT1(BinaryReader br, int width, int height, byte[] output)
        {
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            var colors = new Color[4];

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    ushort c0 = br.ReadUInt16();
                    ushort c1 = br.ReadUInt16();

                    colors[0] = DecodeRGB565(c0);
                    colors[1] = DecodeRGB565(c1);

                    if (c0 > c1)
                    {
                        colors[2] = Color.FromArgb(255, (2 * colors[0].R + colors[1].R) / 3, (2 * colors[0].G + colors[1].G) / 3, (2 * colors[0].B + colors[1].B) / 3);
                        colors[3] = Color.FromArgb(255, (colors[0].R + 2 * colors[1].R) / 3, (colors[0].G + 2 * colors[1].G) / 3, (colors[0].B + 2 * colors[1].B) / 3);
                    }
                    else
                    {
                        colors[2] = Color.FromArgb(255, (colors[0].R + colors[1].R) / 2, (colors[0].G + colors[1].G) / 2, (colors[0].B + colors[1].B) / 2);
                        colors[3] = Color.FromArgb(0, 0, 0, 0);
                    }

                    uint lookupTable = br.ReadUInt32();

                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int px = bx * 4 + x;
                            int py = by * 4 + y;
                            if (px < width && py < height)
                            {
                                int idx = (int)(lookupTable & 0x03);
                                Color col = colors[idx];
                                int outIdx = (py * width + px) * 4;
                                output[outIdx + 0] = col.B;
                                output[outIdx + 1] = col.G;
                                output[outIdx + 2] = col.R;
                                output[outIdx + 3] = col.A;
                            }
                            lookupTable >>= 2;
                        }
                    }
                }
            }
        }

        private static void DecodeDXT5(BinaryReader br, int width, int height, byte[] output)
        {
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            var alphas = new byte[8];
            var colors = new Color[4];

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    alphas[0] = br.ReadByte();
                    alphas[1] = br.ReadByte();

                    if (alphas[0] > alphas[1])
                    {
                        for (int i = 1; i < 7; i++)
                            alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
                    }
                    else
                    {
                        for (int i = 1; i < 5; i++)
                            alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
                        alphas[6] = 0;
                        alphas[7] = 255;
                    }

                    ulong alphaMask = 0;
                    for (int i = 0; i < 6; i++)
                        alphaMask |= ((ulong)br.ReadByte()) << (i * 8);

                    ushort c0 = br.ReadUInt16();
                    ushort c1 = br.ReadUInt16();
                    colors[0] = DecodeRGB565(c0);
                    colors[1] = DecodeRGB565(c1);
                    colors[2] = Color.FromArgb(255, (2 * colors[0].R + colors[1].R) / 3, (2 * colors[0].G + colors[1].G) / 3, (2 * colors[0].B + colors[1].B) / 3);
                    colors[3] = Color.FromArgb(255, (colors[0].R + 2 * colors[1].R) / 3, (colors[0].G + 2 * colors[1].G) / 3, (colors[0].B + 2 * colors[1].B) / 3);

                    uint lookupTable = br.ReadUInt32();

                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int px = bx * 4 + x;
                            int py = by * 4 + y;
                            if (px < width && py < height)
                            {
                                int colorIdx = (int)(lookupTable & 0x03);
                                int alphaIdx = (int)(alphaMask & 0x07);
                                Color col = colors[colorIdx];
                                byte a = alphas[alphaIdx];

                                int outIdx = (py * width + px) * 4;
                                output[outIdx + 0] = col.B;
                                output[outIdx + 1] = col.G;
                                output[outIdx + 2] = col.R;
                                output[outIdx + 3] = a;
                            }
                            lookupTable >>= 2;
                            alphaMask >>= 3;
                        }
                    }
                }
            }
        }

        private static Color DecodeRGB565(ushort val)
        {
            int r = ((val >> 11) & 0x1F) * 255 / 31;
            int g = ((val >> 5) & 0x3F) * 255 / 63;
            int b = (val & 0x1F) * 255 / 31;
            return Color.FromArgb(255, r, g, b);
        }
    }
}