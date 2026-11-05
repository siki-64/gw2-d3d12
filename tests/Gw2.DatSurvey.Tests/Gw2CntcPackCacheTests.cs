using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests
{
    [TestClass]
    public class Gw2CntcPackCacheTests
    {
        private static readonly Gw2CntcPackStamp[] Stamps =
        {
            new(0x13930E, 100, 7, 0xAAAA),
            new(0x13930F, 200, 8, 0xBBBB),
        };

        private static readonly byte[][] Packs = { new byte[] { 1, 2, 3 }, Array.Empty<byte>() };

        [TestMethod]
        public void Cache_ReturnsTheStoredPacksWhenEveryStampMatches()
        {
            var path = Path.GetTempFileName();
            try
            {
                Gw2CntcPackCache.Write(path, Stamps, Packs);

                Assert.IsTrue(Gw2CntcPackCache.TryRead(path, Stamps, out var read));
                Assert.AreEqual(2, read.Count);
                CollectionAssert.AreEqual(Packs[0], read[0]);
                Assert.AreEqual(0, read[1].Length);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Cache_IsRefusedWhenAStampDiffersTheCountDiffersOrTheFileIsForeignOrMissing()
        {
            var path = Path.GetTempFileName();
            try
            {
                Gw2CntcPackCache.Write(path, Stamps, Packs);

                var changedCrc = new[] { Stamps[0], Stamps[1] with { Crc = 1 } };
                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, changedCrc, out _));
                var changedSize = new[] { Stamps[0] with { Size = 1 }, Stamps[1] };
                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, changedSize, out _));
                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, Stamps[..1], out _));

                File.WriteAllBytes(path, new byte[64]);
                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, Stamps, out _));
                File.Delete(path);
                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, Stamps, out _));
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Cache_IsRefusedWhenTheFileIsTruncated()
        {
            var path = Path.GetTempFileName();
            try
            {
                Gw2CntcPackCache.Write(path, Stamps, Packs);
                var bytes = File.ReadAllBytes(path);
                File.WriteAllBytes(path, bytes[..^2]);

                Assert.IsFalse(Gw2CntcPackCache.TryRead(path, Stamps, out _));
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void PathFor_PutsTheCacheNextToTheArchive()
        {
            Assert.AreEqual(Path.Combine("research", "Gw2-208784.packs.bin"), Gw2CntcPackCache.PathFor(Path.Combine("research", "Gw2-208784.dat")));
        }

        [TestMethod]
        public void RealArchive_LoadsTheSamePacksFromTheCacheAsFromTheArchive()
        {
            var dat = Gw2Installation.DatPath();
            if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
            var cachePath = Gw2CntcPackCache.PathFor(dat);
            var cacheExisted = File.Exists(cachePath);
            try
            {
                using (var writer = Gw2ContentReader.Open(dat, false))
                {
                    if (!cacheExisted) Assert.IsFalse(writer.LoadedContentStoreFromPackCache);
                    writer.WritePackCache();
                }

                using var cached = Gw2ContentReader.Open(dat, false);
                using var direct = Gw2ContentReader.Open(dat, false, usePackCache: false);

                Assert.AreEqual(direct.ContentStore.Count, cached.ContentStore.Count);
                Assert.IsTrue(cached.LoadedContentStoreFromPackCache);
                Assert.IsFalse(direct.LoadedContentStoreFromPackCache);
                for (var i = 0; i < direct.ContentStore.Count; i++)
                    CollectionAssert.AreEqual(direct.ContentStore[i].Data.ToArray(), cached.ContentStore[i].Data.ToArray());
            }
            finally { if (!cacheExisted) File.Delete(cachePath); }
        }
    }
}
