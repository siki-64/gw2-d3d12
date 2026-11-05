#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gw2.Protocol.Capture
{
    public enum CaptureRecordKind : byte
    {
        Inbound = 1,
        Outbound = 2,
        HandshakeOutbound = 3,
        Milliseconds = 4,
        SystemFileTime = 5,
        Entropy = 6,
        SendCallStack = 7,
        EventTickMilliseconds = 8,
        EventTickFileTime = 9,
        TransportKeySchedule = 10,
    }

    public sealed class CapturedChunk
    {
        public CapturedChunk(CaptureRecordKind kind, uint protocol, ulong connection, long timestamp, byte[] plaintext)
        {
            Kind = kind;
            Protocol = protocol;
            Connection = connection;
            Timestamp = timestamp;
            Plaintext = plaintext ?? throw new ArgumentNullException(nameof(plaintext));
        }

        public CaptureRecordKind Kind { get; }
        public uint Protocol { get; }
        public ulong Connection { get; }
        public long Timestamp { get; }
        public byte[] Plaintext { get; }
    }

    public sealed class NetworkCapture
    {
        public const int MaximumChunkBytes = 1 << 24;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GW2NCAP1");

        public NetworkCapture(int build, long timestampFrequency, IReadOnlyList<CapturedChunk> chunks)
        {
            Build = build;
            TimestampFrequency = timestampFrequency;
            Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        }

        public int Build { get; }
        public long TimestampFrequency { get; }
        public IReadOnlyList<CapturedChunk> Chunks { get; }

        public static void WriteHeader(BinaryWriter writer, int build, long timestampFrequency)
        {
            writer.Write(Magic);
            writer.Write(build);
            writer.Write(timestampFrequency);
        }

        public static void WriteChunk(BinaryWriter writer, CaptureRecordKind direction, uint protocol, ulong connection, long timestamp, ReadOnlySpan<byte> plaintext)
        {
            writer.Write((byte)direction);
            writer.Write(protocol);
            writer.Write(connection);
            writer.Write(timestamp);
            writer.Write(plaintext.Length);
            writer.Write(plaintext);
        }

        public static NetworkCapture Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var magic = reader.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic))
                throw new FormatException("network capture: missing GW2NCAP1 header");
            int build = reader.ReadInt32();
            long frequency = reader.ReadInt64();
            var chunks = new List<CapturedChunk>();
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 25) break;
                var direction = (CaptureRecordKind)reader.ReadByte();
                if (direction < CaptureRecordKind.Inbound || direction > CaptureRecordKind.TransportKeySchedule)
                    throw new FormatException($"network capture: unknown direction {(byte)direction} at {stream.Position - 1}");
                uint protocol = reader.ReadUInt32();
                ulong connection = reader.ReadUInt64();
                long timestamp = reader.ReadInt64();
                int length = reader.ReadInt32();
                if (length < 0 || length > MaximumChunkBytes)
                    throw new FormatException($"network capture: chunk length {length} out of range");
                var plaintext = reader.ReadBytes(length);
                if (plaintext.Length != length) break;
                chunks.Add(new CapturedChunk(direction, protocol, connection, timestamp, plaintext));
            }
            return new NetworkCapture(build, frequency, chunks);
        }

        public static bool IsStream(CaptureRecordKind kind) =>
            kind == CaptureRecordKind.Inbound || kind == CaptureRecordKind.Outbound || kind == CaptureRecordKind.HandshakeOutbound;

        public IReadOnlyList<ClockReading> ClockReadings()
        {
            var readings = new List<ClockReading>();
            foreach (var chunk in Chunks)
                if ((chunk.Kind == CaptureRecordKind.Milliseconds || chunk.Kind == CaptureRecordKind.SystemFileTime) && chunk.Plaintext.Length == sizeof(ulong))
                    readings.Add(new ClockReading(chunk.Kind, (uint)chunk.Connection, chunk.Timestamp, BitConverter.ToUInt64(chunk.Plaintext, 0)));
            return readings;
        }

        public IReadOnlyList<EventTick> EventTicks()
        {
            var ticks = new List<EventTick>();
            ulong? milliseconds = null;
            foreach (var chunk in Chunks)
            {
                if (chunk.Plaintext.Length != sizeof(ulong)) continue;
                if (chunk.Kind == CaptureRecordKind.EventTickMilliseconds) milliseconds = BitConverter.ToUInt64(chunk.Plaintext, 0);
                else if (chunk.Kind == CaptureRecordKind.EventTickFileTime && milliseconds is ulong tickMilliseconds)
                {
                    ticks.Add(new EventTick((uint)chunk.Connection, chunk.Timestamp, (uint)tickMilliseconds, BitConverter.ToUInt64(chunk.Plaintext, 0)));
                    milliseconds = null;
                }
            }
            return ticks;
        }

        public IReadOnlyList<CapturedChunk> EntropyOutputs()
        {
            var outputs = new List<CapturedChunk>();
            foreach (var chunk in Chunks)
                if (chunk.Kind == CaptureRecordKind.Entropy) outputs.Add(chunk);
            return outputs;
        }

        public IReadOnlyList<SendCallStack> SendCallStacks()
        {
            var stacks = new List<SendCallStack>();
            foreach (var chunk in Chunks)
            {
                if (chunk.Kind != CaptureRecordKind.SendCallStack) continue;
                var returnAddresses = new uint[chunk.Plaintext.Length / sizeof(uint)];
                for (int frame = 0; frame < returnAddresses.Length; frame++) returnAddresses[frame] = BitConverter.ToUInt32(chunk.Plaintext, frame * sizeof(uint));
                stacks.Add(new SendCallStack((int)chunk.Protocol, chunk.Timestamp, returnAddresses));
            }
            return stacks;
        }

        public static NetworkCapture Read(string path)
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }

        public IReadOnlyList<CapturedStream> Streams()
        {
            var streams = new List<CapturedStream>();
            var byKey = new Dictionary<(ulong Connection, CaptureRecordKind Direction), CapturedStream>();
            var connectionOrder = new Dictionary<ulong, int>();
            foreach (var chunk in Chunks)
            {
                if (!IsStream(chunk.Kind)) continue;
                if (!connectionOrder.TryGetValue(chunk.Connection, out int order))
                    connectionOrder[chunk.Connection] = order = connectionOrder.Count;
                if (!byKey.TryGetValue((chunk.Connection, chunk.Kind), out var captured))
                {
                    captured = new CapturedStream(order, chunk.Protocol, chunk.Kind);
                    byKey[(chunk.Connection, chunk.Kind)] = captured;
                    streams.Add(captured);
                }
                captured.Append(chunk);
            }
            return streams;
        }
    }

    public sealed class EventTick
    {
        public EventTick(uint thread, long timestamp, uint milliseconds, ulong fileTime)
        {
            Thread = thread;
            Timestamp = timestamp;
            Milliseconds = milliseconds;
            FileTime = fileTime;
        }

        public uint Thread { get; }
        public long Timestamp { get; }
        public uint Milliseconds { get; }
        public ulong FileTime { get; }
    }

    public sealed class SendCallStack
    {
        public SendCallStack(int messageId, long timestamp, IReadOnlyList<uint> returnAddressRvas)
        {
            MessageId = messageId;
            Timestamp = timestamp;
            ReturnAddressRvas = returnAddressRvas;
        }

        public int MessageId { get; }
        public long Timestamp { get; }
        public IReadOnlyList<uint> ReturnAddressRvas { get; }
    }

    public readonly struct ClockReading
    {
        public ClockReading(CaptureRecordKind clock, uint thread, long timestamp, ulong value)
        {
            Clock = clock;
            Thread = thread;
            Timestamp = timestamp;
            Value = value;
        }

        public CaptureRecordKind Clock { get; }
        public uint Thread { get; }
        public long Timestamp { get; }
        public ulong Value { get; }
    }

    public sealed class CapturedStream
    {
        private readonly MemoryStream _bytes = new MemoryStream();
        private readonly List<(long Offset, long Timestamp)> _chunkStarts = new List<(long, long)>();

        public CapturedStream(int connectionOrder, uint protocol, CaptureRecordKind direction)
        {
            ConnectionOrder = connectionOrder;
            Protocol = protocol;
            Direction = direction;
        }

        public int ConnectionOrder { get; }
        public uint Protocol { get; private set; }
        public CaptureRecordKind Direction { get; }
        public ReadOnlySpan<byte> Bytes => _bytes.GetBuffer().AsSpan(0, (int)_bytes.Length);
        public int ChunkCount => _chunkStarts.Count;

        public void Append(CapturedChunk chunk)
        {
            if (Protocol == 0) Protocol = chunk.Protocol;
            _chunkStarts.Add((_bytes.Length, chunk.Timestamp));
            _bytes.Write(chunk.Plaintext, 0, chunk.Plaintext.Length);
        }

        public int ChunkAt(long offset)
        {
            int low = 0, high = _chunkStarts.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_chunkStarts[middle].Offset <= offset) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        public long TimestampOfChunk(int chunk) => _chunkStarts[chunk].Timestamp;

        public List<int> ChunkEnds()
        {
            var ends = new List<int>(_chunkStarts.Count);
            for (int chunk = 1; chunk < _chunkStarts.Count; chunk++) ends.Add((int)_chunkStarts[chunk].Offset);
            if (_chunkStarts.Count > 0) ends.Add((int)_bytes.Length);
            return ends;
        }
    }
}
