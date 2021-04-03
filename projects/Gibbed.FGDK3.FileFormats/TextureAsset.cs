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
using Gibbed.IO;
using System.IO;

namespace Gibbed.FGDK3.FileFormats
{
    public struct TextureAsset
    {
        public int ColorCount;
        public uint[] Palette;
        public uint[] PaletteDark;
        public int Width;
        public int Height;
        public byte[][] Mips;

        public static TextureAsset Read(TextureFlags flags, Stream input, Endian endian)
        {
            var knownFlags =
                TextureFlags.Unknown0 | TextureFlags.Unknown1 |
                TextureFlags.Unknown2 | TextureFlags.HasMips;
            var unknownFlags = flags & ~knownFlags;
            if (unknownFlags != TextureFlags.None)
            {
                throw new FormatException("got unknown texture flags");
            }

            var colorCount = input.ReadValueS32(endian);

            var palette = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                palette[i] = input.ReadValueU32(endian);
            }

            var paletteDark = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                paletteDark[i] = input.ReadValueU32(endian);
            }

            var width = input.ReadValueS32(endian);
            var height = input.ReadValueS32(endian);

            var mipCount = (flags & TextureFlags.HasMips) != 0 ? 1 : 4;

            var mips = new byte[mipCount][];
            for (int i = 0; i < mipCount; i++)
            {
                var mipSize = input.ReadValueS32(endian);
                mips[i] = input.ReadBytes(mipSize);
            }

            TextureAsset instance;
            instance.ColorCount = colorCount;
            instance.Palette = palette;
            instance.PaletteDark = paletteDark;
            instance.Width = width;
            instance.Height = height;
            instance.Mips = mips;
            return instance;
        }
    }
}
