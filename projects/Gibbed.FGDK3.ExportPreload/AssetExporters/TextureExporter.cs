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

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

            var textures = new TextureAsset[textureCount];
            for (int i = 0; i < textureCount; i++)
            {
                // TODO(gibbed): actually read the header, this forces LE
                var textureFlags = (TextureFlags)BitConverter.ToUInt32(resourcesHeader.ResourceBytes[2][i * 2], 0);
                textures[i] = TextureAsset.Read(textureFlags, input, endian);
            }

            var spriteCount = input.ReadValueS32(endian);
            var sprites = new (int id, string name)[spriteCount];
            for (int i = 0; i < spriteCount; i++)
            {
                var spriteId = input.ReadValueS32(endian);
                var spriteNameLength = input.ReadValueS32(endian);
                var spriteName = input.ReadString(spriteNameLength, true, Encoding.ASCII);
                sprites[i] = (spriteId, spriteName);
            }

            for (int i = 0; i < textureCount; i++)
            {
                var texture = textures[i];

                var outputPath = Path.Combine(outputBasePath, $"texture_{i}");

                var outputParentPath = Path.GetDirectoryName(outputPath);
                Directory.CreateDirectory(outputParentPath);

                using (var bitmap = MakeBitmapPalettized(texture.Width, texture.Height, texture.Mips[0], texture.Palette))
                {
                    bitmap.Save(outputPath + ".png", ImageFormat.Png);
                }

                using (var bitmap = MakeBitmapPalettized(texture.Width, texture.Height, texture.Mips[0], texture.PaletteDark))
                {
                    bitmap.Save(outputPath + "_dark.png", ImageFormat.Png);
                }
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
