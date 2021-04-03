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
using Gibbed.IO;

namespace Gibbed.FGDK3.FileFormats
{
    public class ResourcesHeader
    {
        public Resource[] Resources { get; set; }
        public Dependency[] Dependencies { get; set; }
        public byte[][][] ResourceBytes { get; set; }

        public static ResourcesHeader Read(Stream input, Endian endian)
        {
            var resourceCount = 1 + input.ReadValueS32(endian);

            var resources = new Resource[resourceCount];
            for (int i = 0; i < resourceCount; i++)
            {
                resources[i] = ReadResource(input, endian);
            }

            var dependencyCount = input.ReadValueS32(endian);
            var dependencies = new Dependency[dependencyCount];
            for (int i = 0; i < dependencyCount; i++)
            {
                dependencies[i] = ReadDependency(input, endian);
            }

            var resourceBytes = new byte[resourceCount][][];
            for (int i = 0; i < resourceCount; i++)
            {
                var resource = resources[i];
                resourceBytes[i] = new byte[resource.Subresources.Length][];
                for (int j = 0; j < resource.Subresources.Length; j++)
                {
                    var subresource = resource.Subresources[j];
                    resourceBytes[i][j] = input.ReadBytes(subresource.DataSize);
                }
                resources[i] = resource;
            }

            var instance = new ResourcesHeader();
            instance.Resources = resources;
            instance.Dependencies = dependencies;
            instance.ResourceBytes = resourceBytes;
            return instance;
        }

        private static Resource ReadResource(Stream input, Endian endian)
        {
            var subresourceCount = input.ReadValueS32(endian);
            var subresources = new Subresource[subresourceCount];
            for (int i = 0; i < subresourceCount; i++)
            {
                subresources[i] = ReadSubresource(input, endian);
            }

            Resource instance;
            instance.Subresources = subresources;
            return instance;
        }

        private static Subresource ReadSubresource(Stream input, Endian endian)
        {
            var dataSize = input.ReadValueS32(endian);
            var unknownCount = input.ReadValueS32(endian);
            var unknowns = new Unknown[unknownCount];
            for (int i = 0; i < unknownCount; i++)
            {
                unknowns[i] = ReadUnknown(input, endian);
            }

            Subresource instance;
            instance.DataSize = dataSize;
            instance.Unknowns = unknowns;
            return instance;
        }

        private static Unknown ReadUnknown(Stream input, Endian endian)
        {
            Unknown instance;
            instance.Unknown0 = input.ReadValueU32(endian);
            instance.Unknown1 = input.ReadValueU32(endian);
            instance.Unknown2 = input.ReadValueU32(endian);
            return instance;
        }

        private static Dependency ReadDependency(Stream input, Endian endian)
        {
            Dependency instance;
            instance.Unknown0 = input.ReadValueU32(endian);
            instance.Unknown1 = input.ReadValueU32(endian);
            return instance;
        }

        public struct Resource
        {
            public Subresource[] Subresources;
        }

        public struct Unknown
        {
            public uint Unknown0;
            public uint Unknown1;
            public uint Unknown2;
        }

        public struct Subresource
        {
            public int DataSize;
            internal Unknown[] Unknowns;
        }

        public struct Dependency
        {
            public uint Unknown0;
            public uint Unknown1;
        }
    }
}
