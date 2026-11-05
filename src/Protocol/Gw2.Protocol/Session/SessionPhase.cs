using System;
using System.Diagnostics.CodeAnalysis;

namespace Gw2.Protocol.Session
{
    public enum MsgConnMode
    {
        ClientStart = 1,

        Initial = 2,

        Encrypted = 3,
    }

    public sealed class SessionPhase
    {
        public MsgConnMode Mode { get; private set; }

        public SessionPhase()
        {
            Mode = MsgConnMode.Initial;
        }

        public void ApplySetMode(bool flag)
        {
            Mode = flag ? MsgConnMode.ClientStart : MsgConnMode.Encrypted;
        }

        public bool CanSendEncrypted => Mode == MsgConnMode.Encrypted;

        public bool BuffersHandshake => Mode == MsgConnMode.ClientStart;

        public bool TryEstablish(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> sharedSecret, [MaybeNullWhen(false)] out byte[] transportKey)
        {
            transportKey = null;
            if (Mode != MsgConnMode.Initial) return false;
            if (!HandshakeFrame.TryDecodeServerKey(frame, out byte[]? serverValue)) return false;

            transportKey = HandshakeFrame.DeriveTransportKey(serverValue, sharedSecret);
            Mode = MsgConnMode.Encrypted;
            return true;
        }
    }
}
