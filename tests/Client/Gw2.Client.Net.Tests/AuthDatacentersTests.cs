using System;
using System.Linq;
using Gw2.Client.Net.Login;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AuthDatacentersTests
{
    [TestMethod]
    public void TheConnectedDatacenterIsTriedFirstAndTheOtherOneAfter()
    {
        CollectionAssert.AreEqual(new uint[] { 2, 1 }, AuthDatacenters.TryOrder(2, new Random(0)).ToArray());
    }

    [TestMethod]
    public void WithoutAConnectionBothDatacentersAreTriedOnceStartingAtRandom()
    {
        var firsts = Enumerable.Range(0, 64).Select(seed => AuthDatacenters.TryOrder(null, new Random(seed))).ToList();

        Assert.IsTrue(firsts.All(order => order.OrderBy(datacenter => datacenter).SequenceEqual(AuthDatacenters.All)));
        CollectionAssert.AreEquivalent(new uint[] { 1, 2 }, firsts.Select(order => order[0]).Distinct().ToArray());
    }

    [TestMethod]
    public void ADatacenterOutsideTheMaskIsNotReused()
    {
        Assert.IsTrue(AuthDatacenters.All.Contains(AuthDatacenters.TryOrder(3, new Random(0))[0]));
    }
}
