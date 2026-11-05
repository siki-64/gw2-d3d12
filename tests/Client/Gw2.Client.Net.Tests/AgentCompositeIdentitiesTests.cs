using System;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentCompositeIdentitiesTests
{
    private const uint CapturedAgentId = 2118;
    private const ulong NornMaleRaceToken = 0x2B02D749EE;
    private const ulong HuntsmanVariantToken = 0x705B3A3AA8;
    private const string CapturedNpcCharacterName = "7901C6100281964977AFD3A7222F00000200010000000000000000EE49D7022B000000CD8DD7000000001000";

    [TestMethod]
    public void TheCapturedCharacterNameCarriesTheNornMaleRaceToken()
    {
        var identities = new AgentCompositeIdentities();
        identities.Handle(MessageStreamDecoder.Decode(RecordedSession.Schemas.Game.Recv, Convert.FromHexString(CapturedNpcCharacterName)).Single());
        Assert.AreEqual(new AgentCompositeIdentity(NornMaleRaceToken, null), identities.IdentityOf(CapturedAgentId));

        identities.Handle(CharacterComposite(CapturedAgentId, HuntsmanVariantToken));
        Assert.AreEqual(new AgentCompositeIdentity(NornMaleRaceToken, HuntsmanVariantToken), identities.IdentityOf(CapturedAgentId));
    }

    private static DecodedMessage CharacterComposite(uint agentId, ulong variantToken)
    {
        DecodedField[] fields =
        [
            new(0, true, (ulong)AgentCompositeIdentities.CharacterComposite),
            new(0, true, (ulong)agentId),
            new(0, true, BitConverter.GetBytes(variantToken)),
        ];
        return new DecodedMessage(AgentCompositeIdentities.CharacterComposite, 0, 0, fields);
    }
}
