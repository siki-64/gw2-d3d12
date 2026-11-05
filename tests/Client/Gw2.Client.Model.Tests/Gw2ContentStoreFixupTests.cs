using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ContentStoreFixupTests
{
    [TestMethod]
    public void SchemaFixupTablesMatchRawContentPackWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var fileId = Gw2ContentReader.ContentStoreFirstFileId;
        var store = Gw2ContentStore.TryParse(reader.ReadDocument(fileId))!;
        var pack = reader.ContentStore[0];
        var objects = pack.Objects;

        CollectionAssert.AreEqual(
            objects.SelectMany(pack.EnumerateLocalFixups).Select(fixup => (int)fixup.RelocationOffset).Distinct().Order().ToArray(),
            store.LocalOffsetFixups.Order().ToArray());
        CollectionAssert.AreEqual(
            objects.SelectMany(pack.EnumerateExternalFixups)
                .Select(fixup => new Gw2ContentExternalOffsetFixup((int)fixup.RelocationOffset, (int)fixup.TargetFileIndex))
                .Distinct().OrderBy(fixup => fixup.RelocOffset).ToArray(),
            store.ExternalOffsetFixups.OrderBy(fixup => fixup.RelocOffset).ToArray());
        CollectionAssert.AreEqual(
            objects.SelectMany(pack.EnumerateFileIndexFixups).Select(offset => (int)offset).Distinct().Order().ToArray(),
            store.FileIndexFixups.Order().ToArray());
        CollectionAssert.AreEqual(
            objects.SelectMany(pack.EnumerateStringIndexFixups).Select(offset => (int)offset).Distinct().Order().ToArray(),
            store.StringIndexFixups.Order().ToArray());
        CollectionAssert.AreEqual(
            objects.SelectMany(pack.EnumerateTrackedReferences)
                .Select(reference => new Gw2ContentTrackedReference(
                    (int)reference.SourceOffset, (int)reference.TargetFileIndex, (int)reference.TargetOffset))
                .Distinct().OrderBy(reference => (reference.SourceOffset, reference.TargetFileIndex, reference.TargetOffset)).ToArray(),
            store.TrackedReferences.OrderBy(reference => (reference.SourceOffset, reference.TargetFileIndex, reference.TargetOffset)).ToArray());
        Assert.IsTrue(store.LocalOffsetFixups.Count > 0);
        Assert.IsTrue(store.FileIds.Count > 0);
        Assert.IsTrue(store.FileIds.All(id => id != 0));
    }
}
