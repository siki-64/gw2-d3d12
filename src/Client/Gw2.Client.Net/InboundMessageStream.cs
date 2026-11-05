using System;
using System.Collections.Generic;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.Client.Net;

public sealed class InboundMessageStream
{
    private readonly MessageSchemaSet _schemas;
    private readonly List<byte> _frameBytes = new List<byte>();
    private readonly List<byte> _messageBytes = new List<byte>();

    public InboundMessageStream(MessageSchemaSet schemas)
    {
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
    }

    public IReadOnlyList<DecodedMessage> Append(ReadOnlySpan<byte> decryptedFrames)
    {
        _frameBytes.AddRange(decryptedFrames.ToArray());
        _messageBytes.AddRange(TransportFrame.Deframe(_frameBytes.ToArray(), out int framesConsumed));
        _frameBytes.RemoveRange(0, framesConsumed);

        var messages = MessageStreamDecoder.TryDecode(_schemas, _messageBytes.ToArray(), out int messagesConsumed);
        _messageBytes.RemoveRange(0, messagesConsumed);
        return messages;
    }
}
