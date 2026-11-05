using System;
using System.Diagnostics.CodeAnalysis;

namespace Gw2.Protocol.Session
{
    public enum HandshakeKind : byte
    {
        ClientHello = 0,

        ServerKey = 1,

        ServerError = 2,
    }

    public static class HandshakeFrame
    {
        public const int MaxClientHelloValue = 0x40;

        public const int ServerKeyFrameLength = 0x16;

        public const int KeyMaterialLength = 0x14;

        public static byte[] EncodeClientHello(ReadOnlySpan<byte> publicValue)
        {
            if (publicValue.Length > MaxClientHelloValue)
                throw new ArgumentException(
                    $"client public value must be <= 0x{MaxClientHelloValue:x} bytes", nameof(publicValue));

            var frame = new byte[2 + publicValue.Length];
            frame[0] = (byte)HandshakeKind.ClientHello;
            frame[1] = (byte)(publicValue.Length + 2);
            publicValue.CopyTo(frame.AsSpan(2));
            return frame;
        }

        public static bool TryDecodeClientHello(ReadOnlySpan<byte> frame, [MaybeNullWhen(false)] out byte[] publicValue)
        {
            publicValue = null;
            if (frame.Length < 2) return false;
            if (frame[0] != (byte)HandshakeKind.ClientHello) return false;
            int length = frame[1] - 2;
            if (length < 0 || length > MaxClientHelloValue || frame.Length < 2 + length) return false;
            publicValue = frame.Slice(2, length).ToArray();
            return true;
        }

        public static byte[] EncodeServerKey(ReadOnlySpan<byte> serverValue)
        {
            if (serverValue.Length != KeyMaterialLength)
                throw new ArgumentException("server value must be 20 bytes", nameof(serverValue));

            var frame = new byte[ServerKeyFrameLength];
            frame[0] = (byte)HandshakeKind.ServerKey;
            frame[1] = ServerKeyFrameLength;
            serverValue.CopyTo(frame.AsSpan(2));
            return frame;
        }

        public static bool TryClassify(ReadOnlySpan<byte> frame, out HandshakeKind kind)
        {
            kind = default;
            if (frame.Length < 2) return false;
            if (frame[0] > (byte)HandshakeKind.ServerError) return false;
            kind = (HandshakeKind)frame[0];
            return true;
        }

        public static bool TryDecodeServerKey(ReadOnlySpan<byte> frame, [MaybeNullWhen(false)] out byte[] serverValue)
        {
            serverValue = null;
            if (frame.Length < ServerKeyFrameLength) return false;
            if (frame[0] != (byte)HandshakeKind.ServerKey) return false;
            if (frame[1] != ServerKeyFrameLength) return false;
            serverValue = frame.Slice(2, KeyMaterialLength).ToArray();
            return true;
        }

        public static byte[] DeriveTransportKey(ReadOnlySpan<byte> serverValue, ReadOnlySpan<byte> sharedSecret)
        {
            if (serverValue.Length < KeyMaterialLength)
                throw new ArgumentException("server value must be at least 20 bytes", nameof(serverValue));
            if (sharedSecret.Length < KeyMaterialLength)
                throw new ArgumentException("shared secret must be at least 20 bytes", nameof(sharedSecret));

            var key = new byte[KeyMaterialLength];
            for (int i = 0; i < KeyMaterialLength; i++) key[i] = (byte)(serverValue[i] ^ sharedSecret[i]);
            return key;
        }
    }
}
