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

using System.IO;
using Gibbed.FGDK3.FileFormats;
using Gibbed.IO;

namespace Gibbed.FGDK3.ExportPreload
{
    internal static partial class Program
    {
        public delegate void AssetExportDelegate(
            PreloadFile.OverlayAssetGroup assetGroup,
            Stream input, Endian endian,
            string outputBasePath);

        public delegate (string, AssetExportDelegate) GetAssetExporterDelegate(int assetType);

        public static (string, AssetExportDelegate) GetDogsAssetExporter(int assetType)
        {
            switch (assetType)
            {
                case 0: return ("Text", AssetExporters.TextExporter.Export);
                case 1: return ("Texture", AssetExporters.TextureExporter.Export);
                case 2: return ("Font", null);
                case 3: return ("Shape", AssetExporters.ShapeExporter.Export);
                case 4: return ("Sound", null);
                case 5: return ("Creature", null);
                case 6: return ("DogsTaleLand", null);
                case 7: return ("Animation", null);
                case 8: return ("Script", null);
                case 9: return ("NavGraph", null);
                case 10: return ("Music", null);
            }
            return default;
        }

        public static (string, AssetExportDelegate) GetZooAssetExporter(int assetType)
        {
            switch (assetType)
            {
                case 0: return ("Text", AssetExporters.TextExporter.Export);
                case 1: return ("Texture", AssetExporters.TextureExporter.Export);
                case 2: return ("Shape", AssetExporters.ShapeExporter.Export);
            }
            return default;
        }
    }
}
