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
                case 2: return ExportShapes;
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

        private struct ShapeVertex
        {
            public float X, Y, Z;
            public byte BoneA, BoneB, BoneC, BoneD;
            public float BoneAWeight, BoneBWeight, BoneCWeight, BoneDWeight;
            public float NormalX, NormalY, NormalZ;
            public float UVX, UVY;
        }

        private struct ShapeFace
        {
            public int A, B, C;
            public bool Weighted;
        }

        private class ShapeMesh
        {
            public ShapeBuildContext Parent;
            public List<string> Comments = new List<string>();
            public List<ShapeVertex> Vertices = new List<ShapeVertex>();
            public List<ShapeFace> Faces = new List<ShapeFace>();
        }

        private class ShapeJoint
        {
            public string Name = "Root";
            public List<ShapeJoint> Children = new List<ShapeJoint>();
        }

        private class ShapeBuildContext
        {
            public List<string> Comments = new List<string>();
            public List<ShapeMesh> Meshes = new List<ShapeMesh>();
            public Dictionary<byte, ShapeJoint> BoneIDToJoint = new Dictionary<byte, ShapeJoint>();
            public ShapeJoint Root = new ShapeJoint();
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
                    var sbc = new ShapeBuildContext();
                    sbc.BoneIDToJoint[0] = sbc.Root;
                    ExportShapeObject(input, endian, sbc);

                    writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                    writer.WriteLine("<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
                    writer.WriteLine("  <asset><unit name=\"meter\" meter=\"1\"/><up_axis>Y_UP</up_axis></asset>");
                    foreach (var comment in sbc.Comments)
                        writer.WriteLine($"  <!-- {comment} -->");
                    writer.WriteLine("  <library_geometries>");
                    // NOTE ABOUT THE NAMES:
                    //  "Couldn't find a mesh UID by controller's UID."
                    // Something is fishy here. Try not to deviate too much from Blender-standard. Just in case.
                    for (int meshId = 0; meshId < sbc.Meshes.Count; meshId++)
                    {
                        var mesh = sbc.Meshes[meshId];
                        writer.WriteLine($"    <geometry id=\"m{meshId}-mesh\" name=\"m{meshId}\"><mesh>");
                        writer.WriteLine($"      <source id=\"mesh-{meshId}-positions\">");
                        writer.WriteLine($"        <float_array id=\"mesh-{meshId}-array-p\" count=\"{mesh.Vertices.Count * 3}\">");
                        // NOTE: X is inverted here to fix stuff
                        foreach (var vtx in mesh.Vertices)
                            writer.WriteLine($"{ConvF32S(-vtx.X)} {ConvF32S(vtx.Y)} {ConvF32S(vtx.Z)}");
                        writer.WriteLine($"        </float_array>");
                        writer.WriteLine($"        <technique_common><accessor source=\"#mesh-{meshId}-array-p\" count=\"{mesh.Vertices.Count}\" stride=\"3\">");
                        writer.WriteLine($"          <param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/>");
                        writer.WriteLine($"        </accessor></technique_common>");
                        writer.WriteLine($"      </source>");
                        writer.WriteLine($"      <source id=\"mesh-{meshId}-normals\">");
                        writer.WriteLine($"        <float_array id=\"mesh-{meshId}-array-n\" count=\"{mesh.Vertices.Count * 3}\">");
                        // NOTE: X is inverted here too
                        foreach (var vtx in mesh.Vertices)
                            writer.WriteLine($"{ConvF32S(-vtx.NormalX)} {ConvF32S(vtx.NormalY)} {ConvF32S(vtx.NormalZ)}");
                        writer.WriteLine($"        </float_array>");
                        writer.WriteLine($"        <technique_common><accessor source=\"#mesh-{meshId}-array-n\" count=\"{mesh.Vertices.Count}\" stride=\"3\">");
                        writer.WriteLine($"          <param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/>");
                        writer.WriteLine($"        </accessor></technique_common>");
                        writer.WriteLine($"      </source>");
                        writer.WriteLine($"      <source id=\"mesh-{meshId}-uvs\">");
                        writer.WriteLine($"        <float_array id=\"mesh-{meshId}-array-u\" count=\"{mesh.Vertices.Count * 2}\">");
                        foreach (var vtx in mesh.Vertices)
                            writer.WriteLine($"{ConvF32S(vtx.UVX)} {ConvF32S(vtx.UVY)}");
                        writer.WriteLine($"        </float_array>");
                        writer.WriteLine($"        <technique_common><accessor source=\"#mesh-{meshId}-array-u\" count=\"{mesh.Vertices.Count}\" stride=\"2\">");
                        writer.WriteLine($"          <param name=\"S\" type=\"float\"/><param name=\"T\" type=\"float\"/>");
                        writer.WriteLine($"        </accessor></technique_common>");
                        writer.WriteLine($"      </source>");
                        // Continuing...
                        writer.WriteLine($"      <vertices id=\"mesh-{meshId}-vertices\"><input semantic=\"POSITION\" source=\"#mesh-{meshId}-positions\"/></vertices>");
                        writer.WriteLine($"      <triangles count=\"{mesh.Faces.Count}\">");
                        writer.WriteLine($"        <input semantic=\"VERTEX\" source=\"#mesh-{meshId}-vertices\" offset=\"0\"/>");
                        writer.WriteLine($"        <input semantic=\"NORMAL\" source=\"#mesh-{meshId}-normals\" offset=\"1\"/>");
                        writer.WriteLine($"        <input semantic=\"TEXCOORD\" source=\"#mesh-{meshId}-uvs\" offset=\"2\" set=\"0\"/>");
                        writer.WriteLine($"        <p>");
                        foreach (var face in mesh.Faces)
                            writer.WriteLine($"          {face.A} {face.A} {face.A} {face.B} {face.B} {face.B} {face.C} {face.C} {face.C}");
                        writer.WriteLine($"        </p>");
                        writer.WriteLine($"      </triangles>");
                        writer.WriteLine($"    </mesh></geometry>");
                    }
                    writer.WriteLine("  </library_geometries>");

                    // setup bone mapping
                    var boneIDToJointIDs = new Dictionary<byte, int>();
                    var jointNames = new List<string>();
                    foreach (var bid in sbc.BoneIDToJoint.Keys)
                    {
                        boneIDToJointIDs[bid] = jointNames.Count;
                        jointNames.Add(sbc.BoneIDToJoint[bid].Name);
                    }

                    // continue forward!
                    writer.WriteLine("  <library_controllers>");
                    for (int meshId = 0; meshId < sbc.Meshes.Count; meshId++)
                    {
                        var mesh = sbc.Meshes[meshId];
                        writer.WriteLine($"    <controller id=\"Armature_m{meshId}-skin\" name=\"Armature\">");
                        writer.WriteLine($"      <skin source=\"#m{meshId}-mesh\">");
                        writer.WriteLine($"        <bind_shape_matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</bind_shape_matrix>");
                        writer.WriteLine($"        <source id=\"Armature_m{meshId}-skin-joints\">");
                        writer.WriteLine($"          <Name_array id=\"Armature_m{meshId}-skin-joints-array\" count=\"{jointNames.Count}\">");
                        foreach (var name in jointNames)
                            writer.WriteLine(name);
                        writer.WriteLine($"          </Name_array>");
                        writer.WriteLine($"          <technique_common><accessor source=\"#Armature_m{meshId}-skin-joints-array\" count=\"{jointNames.Count}\" stride=\"1\"><param name=\"JOINT\" type=\"name\"/></accessor></technique_common>");
                        writer.WriteLine($"        </source>");
                        writer.WriteLine($"        <source id=\"Armature_m{meshId}-skin-bind_poses\">");
                        writer.WriteLine($"          <float_array id=\"Armature_m{meshId}-skin-bind_poses-array\" count=\"{jointNames.Count * 16}\">");
                        foreach (var name in jointNames)
                            writer.WriteLine("          1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1");
                        writer.WriteLine($"          </float_array>");
                        writer.WriteLine($"          <technique_common><accessor source=\"#Armature_m{meshId}-skin-bind_poses-array\" count=\"{jointNames.Count}\" stride=\"16\"><param name=\"TRANSFORM\" type=\"float4x4\"/></accessor></technique_common>");
                        writer.WriteLine($"        </source>");
                        var weightsToWeightIDs = new Dictionary<float, int>();
                        var weights = new List<float>();
                        void _ensureWeight(float f)
                        {
                            if (!weightsToWeightIDs.ContainsKey(f))
                            {
                                weightsToWeightIDs[f] = weights.Count;
                                weights.Add(f);
                            }
                        }
                        foreach (var vtx in mesh.Vertices)
                        {
                            _ensureWeight(vtx.BoneAWeight);
                            _ensureWeight(vtx.BoneBWeight);
                            _ensureWeight(vtx.BoneCWeight);
                            _ensureWeight(vtx.BoneDWeight);
                        }
                        writer.WriteLine($"        <source id=\"Armature_m{meshId}-skin-weights\">");
                        writer.WriteLine($"          <float_array id=\"Armature_m{meshId}-skin-weights-array\" count=\"{weights.Count}\">");
                        foreach (var f in weights)
                            writer.WriteLine($"{ConvF32S(f)}");
                        writer.WriteLine($"          </float_array>");
                        writer.WriteLine($"          <technique_common><accessor source=\"#Armature_m{meshId}-skin-weights-array\" count=\"{weights.Count}\" stride=\"1\"><param name=\"WEIGHT\" type=\"float\"/></accessor></technique_common>");
                        writer.WriteLine($"        </source>");
                        writer.WriteLine($"        <joints>");
                        writer.WriteLine($"          <input semantic=\"JOINT\" source=\"#Armature_m{meshId}-skin-joints\"/>");
                        writer.WriteLine($"          <input semantic=\"INV_BIND_MATRIX\" source=\"#Armature_m{meshId}-skin-bind_poses\"/>");
                        writer.WriteLine($"        </joints>");
                        writer.WriteLine($"        <vertex_weights count=\"{mesh.Vertices.Count}\">");
                        writer.WriteLine($"          <input semantic=\"JOINT\" source=\"#Armature_m{meshId}-skin-joints\" offset=\"0\"/>");
                        writer.WriteLine($"          <input semantic=\"WEIGHT\" source=\"#Armature_m{meshId}-skin-weights\" offset=\"1\"/>");
                        writer.WriteLine($"          <vcount>");
                        foreach (var vtx in mesh.Vertices)
                        {
                            var count = 0;
                            if (vtx.BoneAWeight != 0)
                                count++;
                            if (vtx.BoneBWeight != 0)
                                count++;
                            if (vtx.BoneCWeight != 0)
                                count++;
                            if (vtx.BoneDWeight != 0)
                                count++;
                            writer.WriteLine($"{count}");
                        }
                        writer.WriteLine($"          </vcount>");
                        writer.WriteLine($"          <v>");
                        foreach (var vtx in mesh.Vertices)
                        {
                            if (vtx.BoneAWeight != 0)
                                writer.WriteLine($"{boneIDToJointIDs[vtx.BoneA]} {weightsToWeightIDs[vtx.BoneAWeight]}");
                            if (vtx.BoneBWeight != 0)
                                writer.WriteLine($"{boneIDToJointIDs[vtx.BoneB]} {weightsToWeightIDs[vtx.BoneBWeight]}");
                            if (vtx.BoneCWeight != 0)
                                writer.WriteLine($"{boneIDToJointIDs[vtx.BoneC]} {weightsToWeightIDs[vtx.BoneCWeight]}");
                            if (vtx.BoneDWeight != 0)
                                writer.WriteLine($"{boneIDToJointIDs[vtx.BoneD]} {weightsToWeightIDs[vtx.BoneDWeight]}");
                        }
                        writer.WriteLine($"          </v>");
                        writer.WriteLine($"        </vertex_weights>");
                        writer.WriteLine($"      </skin>");
                        writer.WriteLine($"    </controller>");
                    }
                    writer.WriteLine("  </library_controllers>");
                    writer.WriteLine("  <library_visual_scenes><visual_scene id=\"Scene\" name=\"Scene\">");
                    writer.WriteLine("    <node id=\"Armature\" name=\"Armature\" type=\"NODE\">");
                    void writeJoint(ShapeJoint j)
                    {
                        writer.WriteLine($"      <node id=\"Armature_{j.Name}\" name=\"{j.Name}\" sid=\"{j.Name}\" type=\"JOINT\">");
                        foreach (var j2 in j.Children)
                            writeJoint(j2);
                        writer.WriteLine($"      </node>");
                    }
                    writeJoint(sbc.Root);
                    for (int meshId = 0; meshId < sbc.Meshes.Count; meshId++)
                    {
                        writer.WriteLine($"      <node id=\"m{meshId}\" name=\"m{meshId}\" type=\"NODE\">");
                        writer.WriteLine($"        <instance_controller url=\"#Armature_m{meshId}-skin\"><skeleton>#Armature_Root</skeleton></instance_controller>");
                        writer.WriteLine($"      </node>");
                    }
                    writer.WriteLine("    </node>");
                    writer.WriteLine("  </visual_scene></library_visual_scenes><scene><instance_visual_scene url=\"#Scene\"/></scene>");
                    writer.WriteLine("</COLLADA>");

                    var outputPath = $"{outputBasePath}_lod{i}.dae";

                    var outputParentPath = Path.GetDirectoryName(outputPath);
                    Directory.CreateDirectory(outputParentPath);

                    File.WriteAllText(outputPath, writer.ToString(), Encoding.UTF8);
                }
            }
        }

        private static string ConvF32S(float f)
        {
            // sending this to N50, which is roughly the maximum precision, causes the points to disappear entirely for reasons
            return f.ToString("N10");
        }

        private static void ExportShapeObject(Stream input, Endian endian, ShapeBuildContext context)
        {
            var unknown1_0 = input.ReadValueS32(endian);
            var meshCount = input.ReadValueS32(endian);
            var unknown1_2 = input.ReadValueU32(endian);

            context.Comments.Add($"unknown1_2={unknown1_2}");

            var unknown1_3 = new uint[unknown1_0];
            for (int i = 0; i < unknown1_0; i++)
            {
                unknown1_3[i] = input.ReadValueU32(endian);
                context.Comments.Add($"unknown1_3[{i}]={unknown1_3[i]}");
            }

            for (int i = 0; i < meshCount; i++)
            {
                var lContext = new ShapeMesh();
                lContext.Parent = context;
                ExportShapeMesh(input, endian, lContext);
                context.Meshes.Add(lContext);
            }
        }

        private static void ExportShapeMesh(Stream input, Endian endian, ShapeMesh context)
        {
            var unknown1_4_0 = input.ReadValueS16(endian);
            context.Comments.Add($"unknown1_4_0={unknown1_4_0}");

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

            var weighted = unknown1_4_4 == 0x411C;

            for (int i = 0; i < weightedVertexCount; i++)
            {
                ShapeVertex sv = new ShapeVertex();
                sv.X = input.ReadValueF32(endian);
                sv.Y = input.ReadValueF32(endian);
                sv.Z = input.ReadValueF32(endian);

                if (weighted)
                {
                    void _ensureBone(byte b)
                    {
                        if (!context.Parent.BoneIDToJoint.ContainsKey(b))
                        {
                            var j = new ShapeJoint();
                            j.Name = "AutoExporterJoint" + b;
                            context.Parent.Root.Children.Add(j);
                            context.Parent.BoneIDToJoint[b] = j;
                        }
                    }
                    // Bone indices.
                    // These may be endian-dependent, uncertain until tested.
                    // However, results suggest format would be 0xDDCCBBAA if an int, which would be unusual.
                    sv.BoneA = input.ReadValueU8();
                    _ensureBone(sv.BoneA);
                    sv.BoneB = input.ReadValueU8();
                    _ensureBone(sv.BoneB);
                    sv.BoneC = input.ReadValueU8();
                    _ensureBone(sv.BoneC);
                    sv.BoneD = input.ReadValueU8();
                    _ensureBone(sv.BoneD);
                    // Bone weights
                    sv.BoneAWeight = input.ReadValueF32(endian);
                    sv.BoneBWeight = input.ReadValueF32(endian);
                    sv.BoneCWeight = input.ReadValueF32(endian);
                    sv.BoneDWeight = input.ReadValueF32(endian);
                }

                sv.NormalX = input.ReadValueF32(endian);
                sv.NormalY = input.ReadValueF32(endian);
                sv.NormalZ = input.ReadValueF32(endian);

                sv.UVX = input.ReadValueF32(endian);
                sv.UVY = input.ReadValueF32(endian);

                context.Vertices.Add(sv);
            }

            // decompose triangle strips
            var index = 0;
            foreach (var stripLength in triangleStripLengths)
            {
                for (int i = 0; i < stripLength - 2; i++)
                {
                    ShapeFace sf = new ShapeFace();
                    sf.A = indices[index + i + 0];
                    sf.B = indices[index + i + 1];
                    sf.C = indices[index + i + 2];
                    sf.Weighted = weighted;

                    if ((i & 1) != 0)
                    {
                        var temp = sf.B;
                        sf.B = sf.C;
                        sf.C = temp;
                    }

                    context.Faces.Add(sf);
                }
                index += stripLength;
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
