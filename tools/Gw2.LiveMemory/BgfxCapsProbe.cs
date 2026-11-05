using System;
using System.IO;
using Gw2.Formats;
using Gw2.Exe.Live;

namespace Gw2.LiveMemory
{
    internal static class BgfxCapsProbe
    {
        private const int CapsSize = 0x2C0;

        public static int Run(LiveProcess process, string? savePath)
        {
            var caps = new byte[CapsSize];
            if (!process.TryReadInto(process.ImageBase + BgfxLibrary.FromExecutable(process.ExecutablePath).CapsRva, caps))
            {
                Console.WriteLine("bgfx caps unreadable");
                return 1;
            }
            for (var offset = 0; offset < CapsSize; offset += 16)
                Console.WriteLine($"+0x{offset:X3}: {Convert.ToHexString(caps, offset, 16)}");
            if (savePath != null)
                File.WriteAllBytes(savePath, caps);
            return 0;
        }
    }
}
