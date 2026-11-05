using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentTransformationsTests
{
    [TestMethod]
    public void FollowsTheLiveBundlePickupAndDrop()
    {
        var transformations = new AgentTransformations();
        transformations.Handle(Message(AgentTransformations.Set, 6052u, 2728u, 5u, 0u));
        Assert.AreEqual(2728u, transformations.ActiveTransformationOf(6052));
        Assert.AreEqual(2728u, transformations.TransformationOf(6052, AgentTransformations.BundleType));
        transformations.Handle(Message(AgentTransformations.Set, 6052u, 0u, 5u, 0u));
        Assert.IsNull(transformations.ActiveTransformationOf(6052));
        transformations.Handle(Message(AgentTransformations.Set, 6052u, 2659u, 0u, 0u));
        Assert.AreEqual(2659u, transformations.ActiveTransformationOf(6052));
        Assert.IsNull(transformations.ActiveTransformationOf(4985));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
