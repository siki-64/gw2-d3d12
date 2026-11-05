using System;
using Gw2.Client.Net.Login;

namespace Gw2.Client.Net.Game;

public enum Storefront : uint
{
    None = 0,
    Steam = 1,
    Other = 2,
}

public sealed record DeviceReport(SystemRecord System, GameAdapter Adapter, Storefront Storefront, byte[] SessionId)
{
    public const byte ReportKind = 0x40;
    public const uint FullCapabilityMask = 0x0FFEFFD7;
    private const uint UnwrittenField = 0;
    private const uint UnsetMetric = 0;
    private const byte SystemRecordReservedByte = 0;
    private const string EmptyDebugText = "";
    private const int MegabyteShift = 20;

    public uint VideoMemoryMegabytes => (Adapter.VideoMemoryBudget < 0 ? uint.MaxValue : (uint)Adapter.VideoMemoryBudget) >> MegabyteShift;

    public object[] Values() =>
    [
        ReportKind,
        System.LogicalProcessors,
        UnwrittenField,
        System.ProcessorMegahertz,
        System.ProcessorVendor,
        FullCapabilityMask,
        Adapter.Name,
        (ushort)Adapter.DeviceId,
        UnsetMetric,
        UnsetMetric,
        VideoMemoryMegabytes,
        (ushort)Adapter.VendorId,
        (uint)System.MemoryMegabytes,
        System.OsBits,
        System.OsMajorVersion,
        System.OsMinorVersion,
        System.OsServicePackMajor,
        System.OsServicePackMinor,
        SystemRecordReservedByte,
        (uint)Storefront,
        SessionId,
        EmptyDebugText,
    ];
}
