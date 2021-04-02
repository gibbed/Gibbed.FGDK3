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
using System.IO;
using Gibbed.IO;

namespace Gibbed.FGDK3.FileFormats
{
    public class PreloadFile
    {
        public byte Unknown0 { get; set; }
        public byte Unknown1 { get; set; }
        public ushort[] TotalAssetCounts { get; set; }
        public byte Unknown3 { get; set; }
        public UnknownType2 Root { get; set; }

        public static PreloadFile Read(int assetTypeCount, Stream input, Endian endian)
        {
            var unknown0 = input.ReadValueU8();
            var unknown1 = input.ReadValueU8();
            var totalAssetCounts = new ushort[assetTypeCount];
            for (int i = 0; i < assetTypeCount; i++)
            {
                totalAssetCounts[i] = input.ReadValueU16(endian);
            }
            var unknown3 = input.ReadValueU8();
            var root = ReadUnknownType2(assetTypeCount, input, endian);

            var instance = new PreloadFile();
            instance.Unknown0 = unknown0;
            instance.Unknown1 = unknown1;
            instance.TotalAssetCounts = totalAssetCounts;
            instance.Unknown3 = unknown3;
            instance.Root = root;
            return instance;
        }

        private static object ReadObject(int assetTypeCount, Stream input, Endian endian)
        {
            var objectType = (ObjectType)input.ReadValueU8();
            switch (objectType)
            {
                case ObjectType.Overlay:
                {
                    return ReadOverlay(assetTypeCount, input, endian);
                }
                case ObjectType.Unknown1:
                {
                    return ReadUnknownType1(assetTypeCount, input, endian);
                }
                case ObjectType.Unknown2:
                {
                    return ReadUnknownType2(assetTypeCount, input, endian);
                }
            }
            throw new NotSupportedException();
        }

        private static Overlay ReadOverlay(int assetTypeCount, Stream input, Endian endian)
        {
            var id = input.ReadValueU8();
            var segment0 = ReadOverlayDataArray(assetTypeCount, input, endian);
            var segment1 = ReadOverlayDataArray(assetTypeCount, input, endian);
            var segment2 = ReadOverlayDataArray(assetTypeCount, input, endian);
            var segment3 = ReadOverlayDataArray(assetTypeCount, input, endian);

            Overlay instance;
            instance.Id = id;
            instance.AssetGroup0 = segment0;
            instance.AssetGroup1 = segment1;
            instance.AssetGroup2 = segment2;
            instance.AssetGroup3 = segment3;
            return instance;
        }

        private static OverlayAssetGroup[] ReadOverlayDataArray(int assetTypeCount, Stream input, Endian endian)
        {
            var instance = new OverlayAssetGroup[assetTypeCount];
            for (int i = 0; i < assetTypeCount; i++)
            {
                instance[i] = ReadOverlayData(input, endian);
            }
            return instance;
        }

        private static OverlayAssetGroup ReadOverlayData(Stream input, Endian endian)
        {
            var elementCount = input.ReadValueU16(endian);
            var unknownCount = input.ReadValueU16(endian);
            var unknowns = new OverlayUnknown[unknownCount];
            for (int i = 0; i < unknownCount; i++)
            {
                unknowns[i] = ReadOverlayUnknown(input, endian);
            }

            OverlayAssetGroup instance;
            instance.AssetCount = elementCount;
            instance.Unknowns = unknowns;
            return instance;
        }

        private static OverlayUnknown ReadOverlayUnknown(Stream input, Endian endian)
        {
            var unknown0 = input.ReadValueU16(endian);
            var unknown1 = input.ReadValueU16(endian);
            OverlayUnknown instance;
            instance.Unknown0 = unknown0;
            instance.Unknown1 = unknown1;
            return instance;
        }

        private static UnknownType1 ReadUnknownType1(int assetTypeCount, Stream input, Endian endian)
        {
            var objectCount = input.ReadValueU8();
            var objects = new object[objectCount];
            for (int i = 0; i < objectCount; i++)
            {
                objects[i] = ReadObject(assetTypeCount, input, endian);
            }
            UnknownType1 instance;
            instance.Objects = objects;
            return instance;
        }

        private static UnknownType2 ReadUnknownType2(int assetTypeCount, Stream input, Endian endian)
        {
            var objectCount = input.ReadValueU8();
            var objects = new object[objectCount];
            for (int i = 0; i < objectCount; i++)
            {
                objects[i] = ReadObject(assetTypeCount, input, endian);
            }
            UnknownType2 instance;
            instance.Objects = objects;
            return instance;
        }

        public enum ObjectType : byte
        {
            Overlay = 0,
            Unknown1 = 1,
            Unknown2 = 2,
        }

        public struct Overlay
        {
            public byte Id;
            public OverlayAssetGroup[] AssetGroup0;
            public OverlayAssetGroup[] AssetGroup1;
            public OverlayAssetGroup[] AssetGroup2;
            public OverlayAssetGroup[] AssetGroup3;
        }

        public struct OverlayAssetGroup
        {
            public ushort AssetCount;
            public OverlayUnknown[] Unknowns;

        }

        public struct OverlayUnknown
        {
            public ushort Unknown0;
            public ushort Unknown1;
        }

        public struct UnknownType1
        {
            public object[] Objects;
        }

        public struct UnknownType2
        {
            public object[] Objects;
        }
    }
}
