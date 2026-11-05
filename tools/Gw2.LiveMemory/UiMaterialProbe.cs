using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Exe.Live;

namespace Gw2.LiveMemory
{
    internal static class UiMaterialProbe
    {

        public static int Run(LiveProcess process)
        {
            var cache = process.ImageBase + process.Addresses.MaterialCacheRva;
            var fields = new byte[0x60];
            process.TryReadInto(cache, fields);
            Console.WriteLine($"cache {Convert.ToHexString(fields)}");
            process.TryRead(cache + 0x18, out ulong entry);
            var count = 0;
            var shaders = new SortedDictionary<int, int>();
            while (entry != 0 && (entry & 1) == 0 && count < 2000)
            {
                process.TryRead(entry + 180, out int fileCount);
                process.TryRead(entry + 184, out int shader);
                process.TryRead(entry + 192, out ulong material);
                if (count < 10) Console.WriteLine($"0x{entry:X} shader {shader} files {fileCount} material 0x{material:X}");
                shaders[shader] = shaders.GetValueOrDefault(shader) + 1;
                count++;
                if (!process.TryRead(entry + 216, out entry)) break;
            }
            Console.WriteLine($"{count} entries, shaders {string.Join(" ", shaders.Select(pair => $"{pair.Key}x{pair.Value}"))}");
            process.TryRead(cache + 0x68, out ulong solid);
            var solidCount = 0;
            while (solid != 0 && (solid & 1) == 0 && solidCount < 2000)
            {
                process.TryRead(solid, out uint color);
                process.TryRead(solid + 4, out int shader);
                process.TryRead(solid + 16, out ulong material);
                if (solidCount < 20) Console.WriteLine($"solid 0x{solid:X} colour {color:X8} shader {shader} material 0x{material:X}");
                solidCount++;
                if (!process.TryRead(solid + 56, out solid)) break;
            }
            Console.WriteLine($"{solidCount} solid entries");
            return 0;
        }
    }
}
