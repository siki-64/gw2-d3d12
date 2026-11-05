using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrMaterialConstantsComparerTests
{
    [TestMethod]
    public void SeparateDictionariesWithTheSameConstantsMergeLikeTheExeMemcmp()
    {
        var first = new Dictionary<uint, Vector4> { [1] = Vector4.One, [2] = Vector4.UnitX };
        var second = new Dictionary<uint, Vector4> { [2] = Vector4.UnitX, [1] = Vector4.One };
        var changed = new Dictionary<uint, Vector4> { [1] = Vector4.One, [2] = Vector4.UnitY };
        var comparer = GrMaterialConstantsComparer.Instance;
        Assert.IsTrue(comparer.Equals(first, second));
        Assert.AreEqual(comparer.GetHashCode(first), comparer.GetHashCode(second));
        Assert.IsFalse(comparer.Equals(first, changed));
    }
}
