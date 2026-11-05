using System;
using System.Collections.Generic;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.CaptureDecoder
{
    public sealed class CapturedMessage
    {
        public CapturedMessage(DecodedMessage decoded, byte[] wireBytes)
        {
            Decoded = decoded;
            WireBytes = wireBytes;
        }

        public DecodedMessage Decoded { get; }
        public byte[] WireBytes { get; }
    }

    public sealed class CapturedStream
    {
        private const int FaultPreviewBytes = 48;

        private readonly MessageSchemaSet _schemas;
        private readonly bool _isFramed;
        private readonly List<byte> _framedBytes = new List<byte>();
        private readonly List<byte> _messageBytes = new List<byte>();

        public CapturedStream(MessageSchemaSet schemas, bool isFramed)
        {
            _schemas = schemas;
            _isFramed = isFramed;
        }

        public string? Fault { get; private set; }
        public int PendingBytes => _framedBytes.Count + _messageBytes.Count;

        public IReadOnlyList<CapturedMessage> Append(byte[] plaintext)
        {
            if (Fault != null) return Array.Empty<CapturedMessage>();
            if (_isFramed)
            {
                _framedBytes.AddRange(plaintext);
                byte[] deframed = TransportFrame.Deframe(_framedBytes.ToArray(), out int consumedFrameBytes);
                _framedBytes.RemoveRange(0, consumedFrameBytes);
                _messageBytes.AddRange(deframed);
            }
            else
            {
                _messageBytes.AddRange(plaintext);
            }

            byte[] messageStream = _messageBytes.ToArray();
            IReadOnlyList<DecodedMessage> decoded;
            int consumed;
            try
            {
                decoded = MessageStreamDecoder.TryDecode(_schemas, messageStream, out consumed);
            }
            catch (FormatException exception)
            {
                Fault = $"{exception.Message}; next bytes {Convert.ToHexString(messageStream, 0, Math.Min(messageStream.Length, FaultPreviewBytes))}";
                return Array.Empty<CapturedMessage>();
            }
            _messageBytes.RemoveRange(0, consumed);

            var messages = new List<CapturedMessage>(decoded.Count);
            foreach (DecodedMessage message in decoded)
                messages.Add(new CapturedMessage(message, messageStream.AsSpan(message.WireOffset, message.WireLength).ToArray()));
            return messages;
        }
    }
}
