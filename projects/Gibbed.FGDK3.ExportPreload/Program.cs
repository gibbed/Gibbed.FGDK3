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
using System.Text;
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
                if (input == null)
                {
                    // no file
                    return;
                }

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
                case 0: return ExportText;
                case 1: return ExportTextures;
                case 3: return ExportShapes;
            }
            return null;
        }

        private static Action<PreloadFile.OverlayData, Stream, Endian, string> GetZooExporter(int assetType)
        {
            switch (assetType)
            {
                case 0: return ExportText;
                case 1: return ExportTextures;
                case 3: return ExportShapes;
            }
            return null;
        }

        private static void ExportText(PreloadFile.OverlayData data, Stream input, Endian endian, string outputBasePath)
        {
            var size = input.ReadValueS32(endian);
            var lines = new List<string>();
            using (var temp = input.ReadToMemoryStream(size))
            {
                while (temp.Position < temp.Length)
                {
                    var line = temp.ReadStringZ(Encoding.Default);
                    lines.Add(line);
                }
            }

            var outputPath = Path.Combine(outputBasePath, "text.txt");

            var outputParentPath = Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(outputParentPath);

            File.WriteAllLines(outputPath, lines, Encoding.UTF8);
        }

        private static void ExportTextures(PreloadFile.OverlayData data, Stream input, Endian endian, string outputBasePath)
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

        private static void ExportShapes(PreloadFile.OverlayData data, Stream input, Endian endian, string outputBasePath)
        {
            var resourcesHeader = ResourcesHeader.Read(input, endian);

            for (int i = 0; i < data.ElementCount; i++)
            {
                var shapeHeaderBytes = resourcesHeader.ResourceBytes[0][1 + (i * 2)];
                ShapeHeader shapeHeader;
                using (var temp = new MemoryStream(shapeHeaderBytes, false))
                {
                    shapeHeader = ReadShapeHeader(temp, endian);
                }
                ExportShape(shapeHeader, input, endian, Path.Combine(outputBasePath, $"shape_{i}"));
            }
        }

        private struct ShapeHeader
        {
            public float Unknown0;
            public float Unknown1;
            public float Unknown2;
            public float Unknown3;
            public uint Unknown4;
            public uint Unknown5;
            public uint Unknown6;
            public int LODCount;
            public int Unknown8Count;
            public uint Unknown9;
            public uint Unknown10;
            public uint Unknown11;
            public uint Unknown12;
            public uint Unknown13;
            public uint Unknown14;
            public uint Unknown15;
            public uint Unknown16;
        }


        private static ShapeHeader ReadShapeHeader(Stream input, Endian endian)
        {
            ShapeHeader instance;
            instance.Unknown0 = input.ReadValueF32(endian);
            instance.Unknown1 = input.ReadValueF32(endian);
            instance.Unknown2 = input.ReadValueF32(endian);
            instance.Unknown3 = input.ReadValueF32(endian);
            instance.Unknown4 = input.ReadValueU32(endian);
            instance.Unknown5 = input.ReadValueU32(endian);
            instance.Unknown6 = input.ReadValueU32(endian);
            instance.LODCount = input.ReadValueS32(endian);
            instance.Unknown8Count = input.ReadValueS32(endian);
            instance.Unknown9 = input.ReadValueU32(endian);
            instance.Unknown10 = input.ReadValueU32(endian);
            instance.Unknown11 = input.ReadValueU32(endian);
            instance.Unknown12 = input.ReadValueU32(endian);
            instance.Unknown13 = input.ReadValueU32(endian);
            instance.Unknown14 = input.ReadValueU32(endian);
            instance.Unknown15 = input.ReadValueU32(endian);
            instance.Unknown16 = input.ReadValueU32(endian);
            return instance;
        }

        private static void ExportShape(ShapeHeader header, Stream input, Endian endian, string outputBasePath)
        {
            var unknown2 = new uint[header.Unknown8Count];
            for (int i = 0; i < header.Unknown8Count; i++)
            {
                unknown2[i] = input.ReadValueU32(endian);
            }

            for (int i = 0; i < header.LODCount; i++)
            {
                using (var writer = new StringWriter())
                {
                    ExportShapeObject(input, endian, 0, writer);
                    File.WriteAllText($"{outputBasePath}_lod{i}.obj", writer.ToString(), Encoding.UTF8);
                }
            }
        }

        private static int ExportShapeObject(Stream input, Endian endian, int baseIndex, TextWriter writer)
        {
            var unknown1_0 = input.ReadValueS32(endian);
            var meshCount = input.ReadValueS32(endian);
            var unknown1_2 = input.ReadValueU32(endian);

            writer.WriteLine($"#unknown1_2={unknown1_2}");

            var unknown1_3 = new uint[unknown1_0];
            for (int i = 0; i < unknown1_0; i++)
            {
                unknown1_3[i] = input.ReadValueU32(endian);
                writer.WriteLine($"#unknown1_3[{i}]={unknown1_3[i]}");
            }

            for (int i = 0; i < meshCount; i++)
            {
                writer.WriteLine($"o mesh_{i}");
                baseIndex = ExportShapeMesh(input, endian, baseIndex, writer);
            }

            return baseIndex;
        }

        private static int ExportShapeMesh(Stream input, Endian endian, int baseVertexIndex, TextWriter writer)
        {
            var unknown1_4_0 = input.ReadValueS16(endian);
            var triangleStripLengthCount = input.ReadValueU16(endian);
            var indexCount = input.ReadValueU16(endian);
            var weightedVertexCount = input.ReadValueU16(endian);
            var unknown1_4_4 = input.ReadValueU32(endian); // format?

            var triangleStripLengths = new ushort[triangleStripLengthCount];
            for (int i = 0; i < triangleStripLengthCount; i++)
            {
                triangleStripLengths[i] = input.ReadValueU16(endian);
            }

            var indices = new ushort[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                indices[i] = input.ReadValueU16(endian);
            }

            if (((indexCount + triangleStripLengthCount) & 1) != 0)
            {
                // padding
                input.Position += 2;
            }

            var startVertexIndex = 1 + baseVertexIndex;

            writer.WriteLine($"#unknown1_4_0={unknown1_4_0}");

            for (int i = 0; i < weightedVertexCount; i++, baseVertexIndex++)
            {
                var vx = input.ReadValueF32(endian);
                var vy = input.ReadValueF32(endian);
                var vz = input.ReadValueF32(endian);
                writer.WriteLine($"v {vx} {vy} {vz}");

                if (unknown1_4_4 == 0x411C)
                {
                    var v8 = input.ReadValueU32(endian);
                    var v9 = input.ReadValueF32(endian);
                    var v10 = input.ReadValueF32(endian);
                    var v11 = input.ReadValueF32(endian);
                    var v12 = input.ReadValueF32(endian);

                    writer.WriteLine($"# {v8} {v9} {v10} {v11} {v12}");
                }

                // TODO(gibbed): need to fix float output, Blender doesn't like scientific notation
                var nx = input.ReadValueF32(endian);
                var ny = input.ReadValueF32(endian);
                var nz = input.ReadValueF32(endian);
                writer.WriteLine($"#vn {nx} {ny} {nz}");

                var uvx = input.ReadValueF32(endian);
                var uvy = input.ReadValueF32(endian);
                writer.WriteLine($"vt {uvx} {uvy}");
            }

            // decompose triangle strips
            var index = 0;
            foreach (var stripLength in triangleStripLengths)
            {
                for (int i = 0; i < stripLength - 2; i++)
                {
                    var a = startVertexIndex + indices[index + i + 0];
                    var b = startVertexIndex + indices[index + i + 1];
                    var c = startVertexIndex + indices[index + i + 2];

                    if ((i & 1) != 0)
                    {
                        var temp = b;
                        b = c;
                        c = temp;
                    }

                    writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
                }
                index += stripLength;
            }

            return baseVertexIndex;
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

            //throw new InvalidOperationException($"'{name}' does not exist in an expected location");
            return null;
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
