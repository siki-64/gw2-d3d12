using Gw2.Formats;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2SplitArchiveTests
{
    private const int BlockSize = SplitStore.BlockSize;

    [TestMethod]
    public void ArchiveOpensFromAnySeekableStream()
    {
        var dat = SyntheticArchive.Dat((0x100, SyntheticArchive.Attex("DXT1", 4, 4, 0, new byte[8])));
        using var archive = FileArchive.Open(new MemoryStream(dat), verifyChecksums: false);

        Assert.AreEqual(0x100u, archive.FileIndex.Single().Key);
    }

    [TestMethod]
    public void ArchiveRejectsUnseekableStream()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FileArchive.Open(new ForwardOnlyStream(), verifyChecksums: false));
    }

    [TestMethod]
    public void SplitArchiveFolderOpensLikeTheSingleFile()
    {
        var root = NewDirectory();
        try
        {
            var dat = SyntheticArchive.Dat((0x100, SyntheticArchive.Attex("DXT1", 4, 4, 0, new byte[8])));
            WriteBlocks(root, dat);
            using var fromFolder = FileArchive.Open(root, verifyChecksums: false);
            using var fromBytes = FileArchive.Open(new MemoryStream(dat), verifyChecksums: false);

            CollectionAssert.AreEquivalent(fromBytes.FileIndex.ToArray(), fromFolder.FileIndex.ToArray());
            CollectionAssert.AreEqual(fromBytes.ReadRawEntry(fromBytes.FileIndex[0x100]), fromFolder.ReadRawEntry(fromFolder.FileIndex[0x100]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void BlocksSpanDirectoriesAndShortBlocksReadAsZeros()
    {
        var root = NewDirectory();
        try
        {
            var lastBlock = SplitStore.BlocksPerDirectory + 1;
            var logical = new byte[(long)lastBlock * BlockSize + 300];
            for (var i = 0; i < logical.Length; i++) logical[i] = (byte)(i * 7 + 1);
            WriteBlocks(root, logical);

            var shortBlock = 20;
            var missingBytes = 140;
            var shortPath = SplitStore.BlockPath(root, shortBlock);
            using (var file = new FileStream(shortPath, FileMode.Open, FileAccess.Write))
                file.SetLength(BlockSize - missingBytes);
            Array.Clear(logical, (shortBlock + 1) * BlockSize - missingBytes, missingBytes);

            using var stream = SplitStore.Open(root);
            Assert.AreEqual(logical.LongLength, stream.Length);

            var read = new byte[logical.Length];
            stream.Seek(0, SeekOrigin.Begin);
            var total = 0;
            while (total < read.Length)
            {
                var count = stream.Read(read, total, Math.Min(read.Length - total, 3 * BlockSize / 2));
                Assert.AreNotEqual(0, count);
                total += count;
            }

            CollectionAssert.AreEqual(logical, read);
            Assert.IsTrue(Directory.Exists(SplitStore.DirectoryPath(root, 1)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FolderWithoutBlockDirectoriesIsNotASplitArchive()
    {
        var root = NewDirectory();
        try
        {
            Assert.IsFalse(SplitStore.IsSplitArchive(root));
            Assert.ThrowsExactly<DirectoryNotFoundException>(() => SplitStore.Open(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteBlocks(string root, byte[] logical)
    {
        File.WriteAllBytes(Path.Combine(root, "Lock.dat"), []);
        for (long block = 0; block * BlockSize < logical.Length; block++)
        {
            Directory.CreateDirectory(SplitStore.DirectoryPath(root, block / SplitStore.BlocksPerDirectory));
            var start = (int)(block * BlockSize);
            File.WriteAllBytes(
                SplitStore.BlockPath(root, block),
                logical.AsSpan(start, Math.Min(BlockSize, logical.Length - start)).ToArray());
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gw2-split-archive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ForwardOnlyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
