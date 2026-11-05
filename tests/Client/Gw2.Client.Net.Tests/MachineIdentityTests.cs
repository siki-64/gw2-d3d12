using System;
using System.IO;
using System.Linq;
using Gw2.Client.Net.Login;
using Gw2.Protocol.Capture;
using Gw2.Protocol.Login;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class MachineIdentityTests
{
    private const double MegahertzTolerance = 0.03;

    [TestMethod]
    public void TheInstallIdIsTheOneTheRecordedLoginSent()
    {
        var recorded = RecordedSetup(LoginMessageIds.ClientBuild);
        var installId = InstallIdentity.Current();
        Console.WriteLine($"install id {installId}");
        CollectionAssert.AreEqual((byte[])recorded.Fields[2].Value!, installId.ToByteArray());
    }

    [TestMethod]
    public void TheSystemRecordOfThisMachineIsTheOneTheRecordedLoginSent()
    {
        var recorded = SystemRecord.Parse((byte[])RecordedSetup(LoginMessageIds.SystemRecord).Fields[1].Value!);
        var built = SystemRecord.FromThisMachine();
        Console.WriteLine($"recorded {recorded}");
        Console.WriteLine($"built    {built}");

        Assert.AreEqual(recorded.ProcessorMegahertz, built.ProcessorMegahertz, recorded.ProcessorMegahertz * MegahertzTolerance);
        Assert.AreEqual(recorded with { ProcessorMegahertz = 0 }, built with { ProcessorMegahertz = 0 });
        CollectionAssert.AreEqual(recorded.ToBytes()[..8], built.ToBytes()[..8]);
    }

    [TestMethod]
    public void TheSystemRecordWritesBackTheBytesItWasParsedFrom()
    {
        var bytes = (byte[])RecordedSetup(LoginMessageIds.SystemRecord).Fields[1].Value!;
        CollectionAssert.AreEqual(bytes, SystemRecord.Parse(bytes).ToBytes());
    }

    [TestMethod]
    public void AnInstallRecordForAnotherComputerIsReplacedAndKept()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var clientRecord = Path.Combine(folder, "Install.bin");
            File.WriteAllBytes(clientRecord, new InstallRecord(Guid.NewGuid(), 1, InstallRecord.HashComputerName("other")).ToBytes());

            var replaced = InstallIdentity.Resolve(Path.Combine(folder, "missing.dat"), clientRecord, "this");
            var kept = InstallIdentity.Resolve(Path.Combine(folder, "missing.dat"), clientRecord, "this");

            Assert.AreEqual(replaced, kept);
            Assert.AreEqual(2u, InstallRecord.Parse(File.ReadAllBytes(clientRecord))!.UseCount);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static DecodedMessage RecordedSetup(int messageId)
    {
        var capture = RecordedSession.Capture();
        var login = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas)
            .SingleOrDefault(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (login is null) Assert.Inconclusive("the capture holds no login connection");
        return login.Messages.First(message => message.MessageId == messageId);
    }
}
