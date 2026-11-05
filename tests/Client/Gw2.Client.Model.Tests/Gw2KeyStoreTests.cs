using System.IO;
using System.Text;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2KeyStoreTests
{
    [TestMethod]
    public void ParsesKeysByKindAndId()
    {
        const string json = "{\"build\":\"1\",\"keys\":{\"text\":{\"31799\":\"000001BF00DDBF2A\"}},\"conflicts\":[]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var store = Gw2KeyStore.Parse(stream);

        Assert.AreEqual("1", store.Build);
        Assert.AreEqual(1, store.Count(Gw2KeyStore.TextKind));
        Assert.AreEqual(0x000001BF00DDBF2AUL, store.FindTextKey(31799));
        Assert.IsNull(store.FindTextKey(1));
        Assert.IsNull(Gw2KeyStore.Empty().Find("content", 1));
    }

    [TestMethod]
    public void StoresServerDeliveredKeysAndClearsThem()
    {
        var store = Gw2KeyStore.Empty();

        store.Store(Gw2KeyStore.TextKind, 31799, 0x000001BF00DDBF2AUL);
        store.Store(Gw2KeyStore.TextKind, 0, 1);

        Assert.AreEqual(0x000001BF00DDBF2AUL, store.FindTextKey(31799));
        Assert.AreEqual(1, store.Count(Gw2KeyStore.TextKind));
        store.Clear(Gw2KeyStore.TextKind);
        Assert.IsNull(store.FindTextKey(31799));
    }
}
