using System;
using System.Collections.Generic;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Transport;

namespace Gw2.Protocol
{
    public sealed class ProtocolCodec
    {
        private readonly ProtocolSchemaCorpus _corpus;
        private readonly TransportCipherState _cipher;
        private readonly List<byte> _transport = new List<byte>();
        private readonly List<byte> _stream = new List<byte>();

        public ProtocolCodec(ProtocolSchemaCorpus corpus, TransportCipherState cipherState)
        {
            _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
            _cipher = cipherState ?? throw new ArgumentNullException(nameof(cipherState));
        }

        public MessageSchemaSet RecvSchemas => _corpus.Recv;

        public IReadOnlyList<DecodedMessage> DecodeInbound(ReadOnlySpan<byte> ciphertext)
        {
            _transport.AddRange(TransportCipher.Crypt(_cipher, ciphertext));

            int off = 0;
            while (off + 4 <= _transport.Count)
            {
                int compLen = _transport[off] | (_transport[off + 1] << 8);
                int decodedLen = _transport[off + 2] | (_transport[off + 3] << 8);
                int size = compLen != 0 ? compLen : decodedLen;
                if (off + 4 + size > _transport.Count) break;

                if (compLen == 0)
                {
                    for (int k = 0; k < decodedLen; k++) _stream.Add(_transport[off + 4 + k]);
                }
                else
                {
                    byte[] payload = _transport.GetRange(off + 4, size).ToArray();
                    _stream.AddRange(Lz4Block.Decompress(payload, decodedLen));
                }
                off += 4 + size;
            }
            if (off > 0) _transport.RemoveRange(0, off);

            byte[] buffer = _stream.ToArray();
            IReadOnlyList<DecodedMessage> messages =
                MessageStreamDecoder.TryDecode(_corpus.Recv, buffer, out int consumed);
            if (consumed > 0) _stream.RemoveRange(0, consumed);
            return messages;
        }
    }
}
