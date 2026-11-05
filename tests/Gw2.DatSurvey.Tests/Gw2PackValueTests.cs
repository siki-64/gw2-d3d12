using Gw2.Formats;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2PackValueTests
{
    [TestMethod]
    public void RequiredFieldReturnsItsValue()
    {
        var owner = Owner(("count", 7u), ("scale", 1.5f), ("id", 9ul));
        Assert.AreEqual(7u, PackValue.RequireU32(owner, "count"));
        Assert.AreEqual(1.5f, PackValue.RequireF32(owner, "scale"));
        Assert.AreEqual(9ul, PackValue.RequireU64(owner, "id"));
    }

    [TestMethod]
    public void MissingRequiredFieldThrowsWithTypeAndName()
    {
        var error = Assert.ThrowsExactly<PackFieldException>(() => PackValue.RequireU32(Owner(), "count"));
        StringAssert.Contains(error.Message, "PackOwnerV0");
        StringAssert.Contains(error.Message, "count");
    }

    [TestMethod]
    public void MistypedFieldThrowsInsteadOfReturningZero()
    {
        var owner = Owner(("scale", 1.5f), ("count", 3u));
        Assert.ThrowsExactly<PackFieldException>(() => PackValue.RequireU32(owner, "scale"));
        Assert.ThrowsExactly<PackFieldException>(() => PackValue.RequireF32(owner, "count"));
        Assert.ThrowsExactly<PackFieldException>(() => PackValue.OptionalU64(owner, "scale"));
    }

    [TestMethod]
    public void OptionalFieldIsNullOnlyWhenAbsent()
    {
        var owner = Owner(("playScript", 0ul));
        Assert.AreEqual(0ul, PackValue.OptionalU64(owner, "playScript"));
        Assert.IsNull(PackValue.OptionalU64(owner, "updateScript"));
    }

    [TestMethod]
    public void BytesAcceptEveryArrayShape()
    {
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, PackValue.AsBytes(new byte[] { 1, 2 }));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, PackValue.AsBytes(new ReadOnlyMemory<byte>([1, 2])));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, PackValue.AsBytes(new object?[] { (byte)1, 2u }));
        Assert.AreEqual(0, PackValue.AsBytes(null).Length);
    }

    [TestMethod]
    public void ListsConvertArrayItemsAndSkipMismatchedOnes()
    {
        var numbers = new object?[] { (byte)1, (ushort)2, 3u, 4ul };
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 }, PackValue.AsU32List(numbers).ToArray());
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3, 4 }, PackValue.AsU16List(numbers).ToArray());
        CollectionAssert.AreEqual(new ulong[] { 0, 0, 3, 4 }, PackValue.AsU64List(numbers).ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, PackValue.AsStrings(new object?[] { "a", 1u, null }).ToArray());
        var points = new object?[] { new[] { 1f, 2f, 3f }, new[] { 4f }, null };
        CollectionAssert.AreEqual(new[] { new System.Numerics.Vector3(1, 2, 3) }, PackValue.AsVec3List(points).ToArray());
        CollectionAssert.AreEqual(new[] { new System.Numerics.Vector2(1, 2) }, PackValue.AsVec2List(points).ToArray());
        Assert.AreEqual(3, PackValue.ArrayLength(points));
        Assert.AreEqual(0, PackValue.AsU32List(null).Count);
    }

    [TestMethod]
    public void RequiredLookupCountsAsRead()
    {
        var owner = Owner(("count", 1u));
        var reads = PackFieldAccess.Record(() => PackValue.RequireU32(owner, "count"));
        Assert.IsTrue(reads.Reads.Contains(("PackOwnerV0", "count")));
    }

    [TestMethod]
    public void OptionalLookupOfAnAbsentFieldIsNotRecorded()
    {
        var owner = Owner(("playScript", 1ul));
        var reads = PackFieldAccess.Record(() =>
        {
            PackValue.OptionalU64(owner, "updateScript");
            owner.Optional("flags");
            PackValue.OptionalU64(owner, "playScript");
        });
        CollectionAssert.AreEquivalent(new[] { ("PackOwnerV0", "playScript") }, reads.Reads.ToArray());
    }

    private static PackObject Owner(params (string Name, object? Value)[] fields)
    {
        var owner = new PackObject("PackOwnerV0");
        foreach (var (name, value) in fields) owner.Add(name, value);
        return owner;
    }
}
