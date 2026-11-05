using System;
using System.Collections.Generic;
using Gw2.Client.Net.Keys;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class TextKeyDeliveryTests
{
    private const int MessageIdFieldType = 0x01;
    private const int ByteCountedArrayFieldType = 0x11;
    private const int VarintFieldType = 0x04;
    private const int EightByteFieldType = 0x05;
    private const int MaximumKeysPerBatch = 64;

    [TestMethod]
    public void ExecutableDefinesTheKeyBatchAsTextIdAndKeyPairs()
    {
        var fields = DeliverSchema().Fields;

        Assert.AreEqual(MessageIdFieldType, fields[0].FieldType);
        Assert.AreEqual(ByteCountedArrayFieldType, fields[1].FieldType);
        Assert.AreEqual(MaximumKeysPerBatch, fields[1].Param);
        CollectionAssert.AreEqual(new[] { VarintFieldType, EightByteFieldType }, Array.ConvertAll(fields[1].Reference!, field => field.FieldType));
        Assert.IsTrue(RecvSchemas().TryGet(TextKeyDelivery.DoneMessageId, out _));
    }

    [TestMethod]
    public void StoresEveryDeliveredKeyAndClearsOnDone()
    {
        var stored = new Dictionary<uint, ulong>();
        var delivery = new TextKeyDelivery((textId, key) => stored[textId] = key, stored.Clear);

        Assert.IsTrue(delivery.Handle(RoundTrip(DeliverSchema(), Batch((31799, 0x000001BF00DDBF2AUL), (7, 0x1122334455667788UL)))));

        Assert.AreEqual(0x000001BF00DDBF2AUL, stored[31799]);
        Assert.AreEqual(0x1122334455667788UL, stored[7]);
        Assert.IsTrue(delivery.Handle(new DecodedMessage(TextKeyDelivery.DoneMessageId, 0, 0, [])));
        Assert.AreEqual(0, stored.Count);
        Assert.IsFalse(delivery.Handle(new DecodedMessage(TextKeyDelivery.DeliverMessageId - 2, 0, 0, [])));
    }

    [TestMethod]
    public void AppliesKeysFromAFramedStreamSplitAcrossChunks()
    {
        var deliver = MsgPackWriter.WriteMessage(DeliverSchema(), Batch((31799, 0x000001BF00DDBF2AUL)));
        Assert.IsTrue(RecvSchemas().TryGet(TextKeyDelivery.DoneMessageId, out var doneSchema));
        var done = MsgPackWriter.WriteMessage(doneSchema, [new DecodedField(MessageIdFieldType, true, (ulong)TextKeyDelivery.DoneMessageId)]);
        var frames = new List<byte>();
        frames.AddRange(UncompressedFrame(deliver));
        frames.AddRange(UncompressedFrame(done));
        var wire = frames.ToArray();
        var split = 4 + deliver.Length + 1;

        var stream = new InboundMessageStream(RecvSchemas());
        var stored = new Dictionary<uint, ulong>();
        var cleared = 0;
        var delivery = new TextKeyDelivery((textId, key) => stored[textId] = key, () => cleared++);

        foreach (var message in stream.Append(wire.AsSpan(0, split))) delivery.Handle(message);
        Assert.AreEqual(0x000001BF00DDBF2AUL, stored[31799]);
        Assert.AreEqual(0, cleared);
        foreach (var message in stream.Append(wire.AsSpan(split))) delivery.Handle(message);
        Assert.AreEqual(1, cleared);
    }

    private static byte[] UncompressedFrame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[2] = (byte)payload.Length;
        frame[3] = (byte)(payload.Length >> 8);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static DecodedField[] Batch(params (uint TextId, ulong Key)[] keys)
    {
        var entries = Array.ConvertAll(keys, entry => new[]
        {
            new DecodedField(VarintFieldType, true, (ulong)entry.TextId),
            new DecodedField(EightByteFieldType, true, BitConverter.GetBytes(entry.Key)),
        });
        return [new DecodedField(MessageIdFieldType, true, (ulong)TextKeyDelivery.DeliverMessageId), new DecodedField(ByteCountedArrayFieldType, true, entries)];
    }

    private static DecodedMessage RoundTrip(MessageSchema schema, DecodedField[] fields)
    {
        var wire = MsgPackWriter.WriteMessage(schema, fields);
        int offset = 0;
        var decoded = MsgPackReader.ReadFields(schema.Fields, wire, ref offset);
        Assert.AreEqual(wire.Length, offset);
        return new DecodedMessage(schema.MessageId, 0, wire.Length, decoded, wire);
    }

    private static MessageSchemaSet RecvSchemas() => RecordedSession.Schemas.Game.Recv;

    private static MessageSchema DeliverSchema()
    {
        Assert.IsTrue(RecvSchemas().TryGet(TextKeyDelivery.DeliverMessageId, out var schema));
        return schema;
    }
}
