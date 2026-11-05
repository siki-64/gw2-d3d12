namespace Gw2.Formats;

public sealed class SplitStore : Stream
{
    public const int BlockSize = 0x100000;
    public const int BlocksPerDirectory = 100;

    private readonly string _root;
    private readonly Dictionary<long, FileStream?> _openBlocks = [];
    private long _position;
    private bool _disposed;

    private SplitStore(string root, long length)
    {
        _root = root;
        Length = length;
    }

    public static bool IsSplitArchive(string path) =>
        Directory.Exists(path) && Directory.Exists(DirectoryPath(path, 0));

    public static SplitStore Open(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!IsSplitArchive(root))
            throw new DirectoryNotFoundException($"'{root}' is not a split GW2 archive");
        return new SplitStore(root, MeasureLength(root));
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        ThrowIfDisposed();
        if (_position >= Length || destination.IsEmpty)
            return 0;

        var block = _position / BlockSize;
        var offsetInBlock = (int)(_position % BlockSize);
        var count = (int)Math.Min(destination.Length, Math.Min(BlockSize - offsetInBlock, Length - _position));
        var target = destination[..count];

        var file = OpenBlock(block);
        var filled = 0;
        if (file is not null && offsetInBlock < file.Length)
        {
            file.Seek(offsetInBlock, SeekOrigin.Begin);
            while (filled < count)
            {
                var read = file.Read(target[filled..]);
                if (read == 0) break;
                filled += read;
            }
        }

        target[filled..].Clear();
        _position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0)
            throw new IOException("seek before the start of the split archive");
        _position = position;
        return position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            foreach (var file in _openBlocks.Values)
                file?.Dispose();
            _openBlocks.Clear();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    public static string DirectoryPath(string root, long directory) =>
        Path.Combine(root, $"{directory}-{BlockSize >> 20}-{BlocksPerDirectory}");

    public static string BlockPath(string root, long block) =>
        Path.Combine(DirectoryPath(root, block / BlocksPerDirectory), $"{block}.dat");

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SplitStore));
    }

    private FileStream? OpenBlock(long block)
    {
        if (_openBlocks.TryGetValue(block, out var file))
            return file;

        var path = BlockPath(_root, block);
        file = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess)
            : null;
        _openBlocks[block] = file;
        return file;
    }

    private static long MeasureLength(string root)
    {
        long lastDirectory = 0;
        while (Directory.Exists(DirectoryPath(root, lastDirectory + 1)))
            lastDirectory++;

        var firstBlock = lastDirectory * BlocksPerDirectory;
        for (var block = firstBlock + BlocksPerDirectory - 1; block >= firstBlock; block--)
        {
            var path = BlockPath(root, block);
            if (File.Exists(path))
                return block * BlockSize + new FileInfo(path).Length;
        }

        return firstBlock * BlockSize;
    }
}
