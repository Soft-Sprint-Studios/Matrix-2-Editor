using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Sledge.Providers.Texture.ImageDecoders
{
    public static class TgaDecoder
    {
        public static Bitmap Decode(Stream stream)
        {
            using var br = new BinaryReader(stream);
            var idLength = br.ReadByte();
            var colorMapType = br.ReadByte();
            var imageType = br.ReadByte();
            br.ReadBytes(5); // Color map spec
            var xOrigin = br.ReadUInt16();
            var yOrigin = br.ReadUInt16();
            var width = br.ReadUInt16();
            var height = br.ReadUInt16();
            var bpp = br.ReadByte();
            var descriptor = br.ReadByte();

            if (idLength > 0) br.ReadBytes(idLength);

            bool isRle = (imageType == 10 || imageType == 11);
            bool isColor = (imageType == 2 || imageType == 10);
            if (!isColor || (bpp != 24 && bpp != 32)) return null;

            int bytesPerPixel = bpp / 8;
            int totalPixels = width * height;
            byte[] rawPixels = new byte[totalPixels * 4];

            if (!isRle)
            {
                byte[] src = br.ReadBytes(totalPixels * bytesPerPixel);
                int srcIdx = 0;
                for (int i = 0; i < totalPixels; i++)
                {
                    byte b = src[srcIdx++];
                    byte g = src[srcIdx++];
                    byte r = src[srcIdx++];
                    byte a = bytesPerPixel == 4 ? src[srcIdx++] : (byte)255;

                    rawPixels[i * 4 + 0] = b;
                    rawPixels[i * 4 + 1] = g;
                    rawPixels[i * 4 + 2] = r;
                    rawPixels[i * 4 + 3] = a;
                }
            }
            else
            {
                int pixelCount = 0;
                while (pixelCount < totalPixels)
                {
                    byte packetHeader = br.ReadByte();
                    int count = (packetHeader & 0x7F) + 1;
                    if ((packetHeader & 0x80) != 0) // RLE packet
                    {
                        byte b = br.ReadByte();
                        byte g = br.ReadByte();
                        byte r = br.ReadByte();
                        byte a = bytesPerPixel == 4 ? br.ReadByte() : (byte)255;

                        for (int i = 0; i < count && pixelCount < totalPixels; i++)
                        {
                            rawPixels[pixelCount * 4 + 0] = b;
                            rawPixels[pixelCount * 4 + 1] = g;
                            rawPixels[pixelCount * 4 + 2] = r;
                            rawPixels[pixelCount * 4 + 3] = a;
                            pixelCount++;
                        }
                    }
                    else // Raw packet
                    {
                        for (int i = 0; i < count && pixelCount < totalPixels; i++)
                        {
                            byte b = br.ReadByte();
                            byte g = br.ReadByte();
                            byte r = br.ReadByte();
                            byte a = bytesPerPixel == 4 ? br.ReadByte() : (byte)255;

                            rawPixels[pixelCount * 4 + 0] = b;
                            rawPixels[pixelCount * 4 + 1] = g;
                            rawPixels[pixelCount * 4 + 2] = r;
                            rawPixels[pixelCount * 4 + 3] = a;
                            pixelCount++;
                        }
                    }
                }
            }

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            bool flipY = (descriptor & 0x20) == 0;
            if (flipY)
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(rawPixels, y * stride, bmpData.Scan0 + (height - 1 - y) * bmpData.Stride, stride);
                }
            }
            else
            {
                Marshal.Copy(rawPixels, 0, bmpData.Scan0, rawPixels.Length);
            }

            bmp.UnlockBits(bmpData);
            return bmp;
        }
    }
}