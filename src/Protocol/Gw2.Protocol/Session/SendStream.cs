using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Protocol.Session
{
    public sealed class SendStream
    {
        public const int Capacity = 0x94C;

        private readonly OutboundProtocolCodec _codec;
        private readonly SessionPhase _phase;
        private readonly Action<byte[]>? _transportSend;
        private readonly List<byte> _buffer = new List<byte>();

        public SendStream(OutboundProtocolCodec codec, SessionPhase phase, Action<byte[]>? transportSend = null)
        {
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _phase = phase ?? throw new ArgumentNullException(nameof(phase));
            _transportSend = transportSend;
        }

        public int Buffered => _buffer.Count;

        public void Append(int messageId, IReadOnlyList<DecodedField> fields)
        {
            if (!_phase.CanSendEncrypted)
                throw new InvalidOperationException(
                    $"the send encoder is gated on MSGCONN_MODE_ENCRYPTED (mode {_phase.Mode})");

            byte[] encoded = _codec.EncodePlaintext(messageId, fields);
            AppendBytes(encoded);
        }

        public void EndFrame()
        {
            if (_buffer.Count > 0) Flush();
        }

        public void Flush()
        {
            switch (_phase.Mode)
            {
                case MsgConnMode.Encrypted:
                    if (_buffer.Count == 0) return;
                    byte[] plaintext = _buffer.ToArray();
                    _buffer.Clear();
                    _transportSend?.Invoke(_codec.Crypt(plaintext));
                    return;
                case MsgConnMode.ClientStart:
                    _buffer.Clear();
                    return;
                default:
                    throw new InvalidOperationException(
                        $"outbound flush requires MSGCONN_MODE_ENCRYPTED, got {_phase.Mode} (MsgConn.cpp:0x291)");
            }
        }

        private void AppendBytes(byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                if (_buffer.Count == Capacity)
                {
                    Flush();
                    continue;
                }

                int space = Capacity - _buffer.Count;
                int count = Math.Min(space, bytes.Length - offset);
                for (int k = 0; k < count; k++) _buffer.Add(bytes[offset + k]);
                offset += count;
            }
        }
    }
}
