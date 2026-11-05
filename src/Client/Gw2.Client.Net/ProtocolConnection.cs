using System;
using System.Collections.Generic;
using Gw2.Protocol;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Session;
using Gw2.Protocol.Transport;

namespace Gw2.Client.Net;

public sealed class ProtocolConnectionRefusedException : Exception
{
    public ProtocolConnectionRefusedException(byte[] frame)
        : base($"the server refused the handshake: {Convert.ToHexString(frame)}") => Frame = frame;

    public byte[] Frame { get; }
}

public sealed class ProtocolConnection
{
    private readonly ProtocolSchemaCorpus _corpus;
    private readonly Action<byte[]> _transportSend;
    private readonly byte[] _sharedSecret;
    private readonly SessionPhase _phase = new SessionPhase();
    private readonly List<byte> _handshakeBytes = new List<byte>();
    private readonly InboundMessageStream _inbound;
    private TransportCipherState? _inboundCipher;
    private SendStream? _sendStream;

    public ProtocolConnection(ProtocolSchemaCorpus corpus, HandshakeGroup group, ReadOnlySpan<byte> seed, Action<byte[]> transportSend)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
        _transportSend = transportSend ?? throw new ArgumentNullException(nameof(transportSend));
        _inbound = new InboundMessageStream(corpus.Recv);
        var keys = HandshakeKeyExchange.Derive(seed, group);
        _sharedSecret = keys.SharedSecretPrefix();
        _transportSend(HandshakeFrame.EncodeClientHello(keys.PublicValue));
    }

    public bool Established => _phase.CanSendEncrypted;

    public int BufferedOutbound => _sendStream?.Buffered ?? 0;

    public IReadOnlyList<DecodedMessage> Receive(ReadOnlySpan<byte> bytes)
    {
        if (!Established)
        {
            _handshakeBytes.AddRange(bytes.ToArray());
            if (!TryEstablish()) return Array.Empty<DecodedMessage>();
            bytes = _handshakeBytes.ToArray();
            _handshakeBytes.Clear();
        }
        if (bytes.IsEmpty) return Array.Empty<DecodedMessage>();

        return _inbound.Append(TransportCipher.Crypt(_inboundCipher!, bytes));
    }

    public void Send(int messageId, IReadOnlyList<DecodedField> fields)
    {
        if (_sendStream is null) throw new InvalidOperationException("the connection has no transport key yet");
        _sendStream.Append(messageId, fields);
    }

    public void SendValues(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0x01, true, (ulong)messageId);
        for (var index = 0; index < values.Length; index++)
            fields[index + 1] = new DecodedField(0, true, values[index] switch
            {
                uint number => (ulong)number,
                ushort number => (ulong)number,
                byte number => (ulong)number,
                var value => value,
            });
        Send(messageId, fields);
    }

    public void EndFrame() => _sendStream?.EndFrame();

    private bool TryEstablish()
    {
        if (_handshakeBytes.Count < 2) return false;
        var pending = _handshakeBytes.ToArray();
        if (!HandshakeFrame.TryClassify(pending, out HandshakeKind kind) || kind == HandshakeKind.ServerError)
            throw new ProtocolConnectionRefusedException(pending);
        if (pending.Length < HandshakeFrame.ServerKeyFrameLength) return false;
        if (!_phase.TryEstablish(pending, _sharedSecret, out byte[]? transportKey))
            throw new ProtocolConnectionRefusedException(pending);

        _inboundCipher = new TransportCipherState();
        TransportCipher.KeySchedule(transportKey, _inboundCipher);
        var outboundCipher = TransportCipherState.FromCapturedState(_inboundCipher.I, _inboundCipher.J, _inboundCipher.CopySBox());
        _sendStream = new SendStream(new OutboundProtocolCodec(_corpus, outboundCipher), _phase, _transportSend);
        _handshakeBytes.RemoveRange(0, HandshakeFrame.ServerKeyFrameLength);
        return true;
    }
}
