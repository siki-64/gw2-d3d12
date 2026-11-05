#nullable enable
using System;
using Gw2.Formats;

namespace Gw2.Protocol.Session
{
    public sealed class HandshakeGroup
    {
        public const int Generator = 4;
        private const int DescriptorVersion = 1;
        private const int DescriptorHeaderSize = 8;
        private const int DescriptorSize = DescriptorHeaderSize + 2 * HandshakeKeyExchange.ValueLength;

        private static readonly byte?[] DescriptorGetter =
        {
            0xC7, 0x01, DescriptorSize, 0x00, 0x00, 0x00,
            0x48, 0x8D, 0x05, null, null, null, null,
            0xC3,
        };

        private static HandshakeGroup? _current;

        private HandshakeGroup(byte[] modulus, byte[] serverPublicValue)
        {
            Modulus = modulus;
            ServerPublicValue = serverPublicValue;
        }

        public static HandshakeGroup Current => _current ??= Extract(ExecutableImage.Current);

        public byte[] Modulus { get; }

        public byte[] ServerPublicValue { get; }

        public HandshakeGroup WithServerPublicValue(byte[] serverPublicValue)
        {
            if (serverPublicValue.Length != HandshakeKeyExchange.ValueLength)
                throw new ArgumentException($"The server public value must be {HandshakeKeyExchange.ValueLength} bytes.", nameof(serverPublicValue));
            return new HandshakeGroup(Modulus, serverPublicValue);
        }

        public static HandshakeGroup FromExecutable(string path) => Extract(ExecutableImage.Open(path));

        internal static HandshakeGroup Extract(ExecutableImage executable)
        {
            var (codeStart, _) = executable.Section(".text");
            var code = executable.SectionBytes(".text");
            HandshakeGroup? group = null;
            for (int index = 0; index + DescriptorGetter.Length <= code.Length; index++)
            {
                if (!Matches(code.Slice(index), DescriptorGetter)) continue;
                ulong lea = codeStart + (ulong)index + 6;
                ulong descriptor = lea + 7 + (ulong)(long)(int)executable.Dword(lea + 3);
                var bytes = executable.Bytes(descriptor, DescriptorSize);
                if (bytes.Length != DescriptorSize || BitConverter.ToInt32(bytes) != DescriptorVersion || BitConverter.ToInt32(bytes.Slice(4)) != Generator) continue;
                if (group != null) throw new InvalidOperationException("The executable has more than one handshake descriptor.");
                group = new HandshakeGroup(bytes.Slice(DescriptorHeaderSize, HandshakeKeyExchange.ValueLength).ToArray(),
                    bytes.Slice(DescriptorHeaderSize + HandshakeKeyExchange.ValueLength, HandshakeKeyExchange.ValueLength).ToArray());
            }
            return group ?? throw new InvalidOperationException("The executable has no handshake descriptor.");
        }

        private static bool Matches(ReadOnlySpan<byte> bytes, byte?[] pattern)
        {
            for (int index = 0; index < pattern.Length; index++)
                if (pattern[index] is byte expected && bytes[index] != expected) return false;
            return true;
        }
    }
}
