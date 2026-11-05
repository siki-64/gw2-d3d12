using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class Token64Tests
{
    private const ulong GroundIdleMachineToken = 0x15844B4E0;
    private const ulong UnarmedGroundIdleToken = 0x15844B4F5;

    [TestMethod]
    public void FillsTheStanceLetterOfAMachineToken()
    {
        Assert.AreEqual(UnarmedGroundIdleToken, Token64.FillEmptyLetters(GroundIdleMachineToken, 'u', '.'));
        Assert.AreEqual("ugmidle", Token64.Decode(Token64.FillEmptyLetters(GroundIdleMachineToken, 'U', '.')));
    }

    [TestMethod]
    public void KeepsLettersThatAreAlreadySet()
    {
        Assert.AreEqual(UnarmedGroundIdleToken, Token64.FillEmptyLetters(UnarmedGroundIdleToken, 'x', 'y'));
        Assert.AreEqual("ab", Token64.Decode(Token64.FillEmptyLetters(0, 'a', 'b')));
    }
}
