using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Transport;

namespace Gw2.Protocol
{
    public sealed class OutboundProtocolCodec
    {
        private readonly ProtocolSchemaCorpus _corpus;
        private readonly TransportCipherState _cipher;

        public OutboundProtocolCodec(ProtocolSchemaCorpus corpus, TransportCipherState cipherState)
        {
            _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
            _cipher = cipherState ?? throw new ArgumentNullException(nameof(cipherState));
        }

        public MessageSchemaSet SendSchemas => _corpus.Send;

        public byte[] Encode(int messageId, IReadOnlyList<DecodedField> fields) =>
            TransportCipher.Crypt(_cipher, EncodePlaintext(messageId, fields));

        public byte[] EncodePlaintext(int messageId, IReadOnlyList<DecodedField> fields)
        {
            if (!_corpus.Send.TryGet(messageId, out MessageSchema schema))
                throw new FormatException($"msgpack: unknown send message id 0x{messageId:x}");
            return MsgPackWriter.WriteMessage(schema, fields);
        }

        public byte[] Crypt(ReadOnlySpan<byte> plaintext) => TransportCipher.Crypt(_cipher, plaintext);
    }
}
