/* Copyright (c) 2021 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Gibbed.FGDK3.FileFormats;
using Gibbed.IO;

namespace Gibbed.FGDK3.ExportPreload.AssetExporters
{
    internal static class TextureExporter
    {
        public static void Export(PreloadFile.OverlayAssetGroup assetGroup, Stream input, Endian endian, string outputBasePath)
        {
            var textureCount = input.ReadValueS32(endian);
            var resourcesHeader = ResourcesHeader.Read(input, endian);

            for (int i = 0; i < textureCount; i++)
            {
                var textureFlags = resourcesHeader.ResourceBytes[2][i * 2][0];
                ExportTexture(textureFlags, input, endian, Path.Combine(outputBasePath, $"texture_{i}"));
            }

            // skip sprite data
            var spriteCount = input.ReadValueS32(endian);
            for (int i = 0; i < spriteCount; i++)
            {
                var spriteId = input.ReadValueS32(endian);
                var spriteNameLength = input.ReadValueS32(endian);
                input.Position += spriteNameLength;
            }
        }

        private static void ExportTexture(byte flags, Stream input, Endian endian, string outputBasePath)
        {
            var colorCount = input.ReadValueS32(endian);

            var paletteA = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                paletteA[i] = input.ReadValueU32(endian);
            }

            var paletteB = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                paletteB[i] = input.ReadValueU32(endian);
            }

            var width = input.ReadValueS32(endian);
            var height = input.ReadValueS32(endian);

            var mipCount = (flags & 0x40) != 0 ? 1 : 4;

            var mipSize = input.ReadValueS32(endian);
            var mipBytes = input.ReadBytes(mipSize);

            for (int i = 1; i < mipCount; i++)
            {
                var additionalMipSize = input.ReadValueS32(endian);
                input.Position += additionalMipSize;
            }

            var outputParentPath = Path.GetDirectoryName(outputBasePath);
            Directory.CreateDirectory(outputParentPath);

            using (var bitmap = MakeBitmapPalettized(width, height, mipBytes, paletteA))
            {
                bitmap.Save(outputBasePath + "_a.png", ImageFormat.Png);
            }

            using (var bitmap = MakeBitmapPalettized(width, height, mipBytes, paletteB))
            {
                bitmap.Save(outputBasePath + "_b.png", ImageFormat.Png);
            }
        }

        private static Bitmap MakeBitmapPalettized(int width, int height, byte[] buffer, uint[] palette)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            var bitmapPalette = bitmap.Palette;
            for (int i = 0; i < palette.Length; i++)
            {
                var abgr = palette[i];
                uint b = (abgr & 0x00FF0000) >> 16;
                uint g = (abgr & 0x0000FF00);
                uint r = (abgr & 0x000000FF) << 16;
                uint a = (abgr & 0xFF000000);
                // alpha 128 -> 255, might be wrong
                a = (uint)(byte)((a >> 24) / 128.0 * 255) << 24;
                var argb = (int)(a | r | g | b);
                bitmapPalette.Entries[i] = Color.FromArgb(argb);
            }
            bitmap.Palette = bitmapPalette;
            var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(area, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            var scan = data.Scan0 + (data.Stride * (height - 1));
            // texture data is upside down
            for (int y = 0, o = 0; y < height; y++, o += width, scan -= data.Stride)
            {
                Marshal.Copy(buffer, o, scan, width);
            }
            bitmap.UnlockBits(data);
            return bitmap;
        }
    }
}
