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
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Gibbed.FGDK3.FileFormats;
using Gibbed.IO;
using ICSharpCode.SharpZipLib.Zip;
using NDesk.Options;

namespace Gibbed.FGDK3.ExportPreload
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
        }

        private enum Target
        {
            Unknown = 0,
            Dogs,
            Zoo,
        }

        private static Target ParseTarget(string value)
        {
            if (Enum.TryParse<Target>(value, true, out var result) == false)
            {
                throw new FormatException("invalid target");
            }
            return result;
        }

        public static void Main(string[] args)
        {
            bool verbose = false;
            bool showHelp = false;
            Target target = Target.Unknown;

            var options = new OptionSet()
            {
                { "t|target=", "set target (dogs/zoo)", v => target = ParseTarget(v) },
                { "v|verbose", "be verbose", v => verbose = v != null },
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("{0}: ", GetExecutableName());
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `{0} --help' for more information.", GetExecutableName());
                return;
            }

            if (extra.Count < 1 || showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ <PRELOAD.DAT> [output_path]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var preloadPath = extra[0];
            var preloadParentDirectory = Path.GetDirectoryName(preloadPath);
            var overlayBasePath = Path.Combine(preloadParentDirectory, "OVERLAY");
            var overlayZipPath = Path.Combine(preloadParentDirectory, "OVERLAY.ZIP");
            var outputBasePath = extra.Count > 1 ? extra[1] : Path.Combine(Path.GetDirectoryName(preloadPath), "OVERLAY_unpack");

            if (target == Target.Unknown)
            {
                Console.WriteLine("Unknown target, attempting to auto-detect...");

                if (File.Exists(Path.Combine(preloadParentDirectory, "DOGS.DGF")) == true)
                {
                    Console.WriteLine("Detected Dog's Life.");
                    target = Target.Dogs;
                }
                else if (File.Exists(Path.Combine(preloadParentDirectory, "ZOO.DGF")) == true)
                {
                    Console.WriteLine("Detected Wallace & Gromit in Project Zoo.");
                    target = Target.Zoo;
                }
                else
                {
                    Console.WriteLine("Could not detect target. Please specify target.");
                    return;
                }
            }

            Func<int, Action<PreloadFile.OverlayData, Stream, Endian, string>> getExporter;
            int assetTypeCount, localizationCount;
            switch (target)
            {
                case Target.Dogs:
                {
                    assetTypeCount = 11;
                    localizationCount = 11;
                    getExporter = GetDogsExporter;
                    break;
                }

                case Target.Zoo:
                {
                    assetTypeCount = 10;
                    localizationCount = 5;
                    getExporter = GetZooExporter;
                    break;
                }

                default:
                {
                    Console.WriteLine("Unsupported target.");
                    return;
                }
            }

            const Endian endian = Endian.Little;

            PreloadFile preloadFile;
            using (var input = File.OpenRead(preloadPath))
            {
                preloadFile = PreloadFile.Read(assetTypeCount, input, endian);
            }

            var overlays = GetOverlays(preloadFile.Root);
            foreach (var overlay in overlays)
            {
                ExportOverlayData($"{overlay.Id}", assetTypeCount, getExporter, overlay.Segment0, overlayBasePath, overlayZipPath, outputBasePath, endian);
                ExportOverlayData($"{overlay.Id}d0", assetTypeCount, getExporter, overlay.Segment1, overlayBasePath, overlayZipPath, outputBasePath, endian);
                for (int i = 0; i < localizationCount; i++)
                {
                    ExportOverlayData($"{overlay.Id}l{i}", assetTypeCount, getExporter, overlay.Segment2, overlayBasePath, overlayZipPath, outputBasePath, endian);
                }
                for (int i = 0; i < localizationCount; i++)
                {
                    ExportOverlayData($"{overlay.Id}d0l{i}", assetTypeCount, getExporter, overlay.Segment3, overlayBasePath, overlayZipPath, outputBasePath, endian);
                }
            }
        }

        private static void ExportOverlayData(
            string name,
            int assetTypeCount, Func<int, Action<PreloadFile.OverlayData, Stream, Endian, string>> getExporter,
            PreloadFile.OverlayData[] datas,
            string basePath, string zipPath,
            string outputBasePath,
            Endian endian)
        {
            outputBasePath = Path.Combine(outputBasePath, name);

            name = $"{name}.ovl";
            using (var input = LoadOverlayFile(name, basePath, zipPath))
            {
                for (int assetType = 0; assetType < assetTypeCount; assetType++)
                {
                    var data = datas[assetType];
                    if (data.ElementCount <= 0)
                    {
                        continue;
                    }

                    var export = getExporter(assetType);
                    if (export == null)
                    {
                        Console.WriteLine($"Exporter for type#{assetType} unavailable, aborting export for '{name}'.");
                        return;
                    }
                    Console.WriteLine($"Exporting type#{assetType} assets from '{name}'...");
                    export(data, input, endian, outputBasePath);
                }
            }
        }

        private static Action<PreloadFile.OverlayData, Stream, Endian, string> GetDogsExporter(int assetType)
        {
            switch (assetType)
            {
                case 1: return ExportTextures;
            }
            return null;
        }

        private static Action<PreloadFile.OverlayData, Stream, Endian, string> GetZooExporter(int assetType)
        {
            switch (assetType)
            {
                case 1: return ExportTextures;
            }
            return null;
        }

        private static void ExportTextures(PreloadFile.OverlayData data, Stream input, Endian endian, string outputBasePath)
        {
            var textureCount = input.ReadValueS32(endian);
            var resourcesHeader = ResourcesHeader.Read(input, endian);

            for (int i = 0; i < textureCount; i++)
            {
                ExportTexture(resourcesHeader.ResourceBytes[2][i * 2][0], input, endian, Path.Combine(outputBasePath, $"texture_{i}"));
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

        private static Stream LoadOverlayFile(string name, string basePath, string zipPath)
        {
            var path = Path.Combine(basePath, name);
            if (File.Exists(path) == true)
            {
                return new MemoryStream(File.ReadAllBytes(path), false);
            }

            if (File.Exists(zipPath) == true)
            {
                using (var input = File.OpenRead(zipPath))
                using (var zip = new ZipFile(input))
                {
                    var entry = zip.GetEntry(name);
                    if (entry != null)
                    {
                        return zip.GetInputStream(entry).ReadToMemoryStream((int)entry.Size);
                    }
                }
            }

            throw new InvalidOperationException($"'{name}' does not exist in an expected location");
        }

        private static List<PreloadFile.Overlay> GetOverlays(PreloadFile.UnknownType2 root)
        {
            var queue = new Queue<object>();
            queue.Enqueue(root);
            var overlays = new List<PreloadFile.Overlay>();
            while (queue.Count > 0)
            {
                var obj = queue.Dequeue();
                if (obj is PreloadFile.Overlay overlay)
                {
                    overlays.Add(overlay);
                }
                else if (obj is PreloadFile.UnknownType1 unknown1)
                {
                    foreach (var child in unknown1.Objects)
                    {
                        queue.Enqueue(child);
                    }
                }
                else if (obj is PreloadFile.UnknownType2 unknown2)
                {
                    foreach (var child in unknown2.Objects)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            return overlays;
        }

        private static Bitmap MakeBitmapPalettized(int width, int height, byte[] buffer, uint[] palette)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            var bitmapPalette = bitmap.Palette;
            for (int i = 0; i < palette.Length; i++)
            {
                var abgr = palette[i];
                uint b = (abgr & 0x00FF0000) >> 16;
                uint r = (abgr & 0x000000FF) << 16;
                var argb = (int)((abgr & 0xFF00FF00) | b | r);
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
