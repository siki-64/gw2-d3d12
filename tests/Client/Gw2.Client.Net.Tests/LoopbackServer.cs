using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Gw2.Protocol.Session;
using Gw2.Protocol.Transport;

namespace Gw2.Client.Net.Tests;

internal sealed class LoopbackServer
{
    private readonly byte[] _clientSharedSecret;
    private TransportCipherState? _towardsClient;
    private TransportCipherState? _fromClient;

    public LoopbackServer(HandshakeGroup group, ReadOnlySpan<byte> clientSeed) =>
        _clientSharedSecret = HandshakeKeyExchange.Derive(clientSeed, group).SharedSecretPrefix();

    public byte[]? Hello { get; private set; }

    public List<byte[]> ReceivedPlaintext { get; } = new List<byte[]>();

    public void OnClientBytes(byte[] bytes)
    {
        if (_fromClient is null)
        {
            if (Hello is not null) throw new InvalidOperationException("the client sent before the key frame");
            Hello = bytes;
            return;
        }
        ReceivedPlaintext.Add(TransportCipher.Crypt(_fromClient, bytes));
    }

    public byte[] KeyFrame()
    {
        var transportKey = RandomNumberGenerator.GetBytes(TransportCipher.KeyLength);
        _towardsClient = new TransportCipherState();
        TransportCipher.KeySchedule(transportKey, _towardsClient);
        _fromClient = TransportCipherState.FromCapturedState(_towardsClient.I, _towardsClient.J, _towardsClient.CopySBox());
        return HandshakeFrame.EncodeServerKey(HandshakeFrame.DeriveTransportKey(transportKey, _clientSharedSecret));
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext) => TransportCipher.Crypt(_towardsClient!, plaintext);
}
