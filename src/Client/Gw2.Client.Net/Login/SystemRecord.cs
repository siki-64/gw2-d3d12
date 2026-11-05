using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Gw2.Client.Net.Login;

public sealed record SystemRecord(
    byte LogicalProcessors,
    ushort ProcessorFeatures,
    ushort ProcessorMegahertz,
    ushort ProcessorSignature,
    string ProcessorVendor,
    byte OsMajorVersion,
    byte OsMinorVersion,
    ushort MemoryMegabytes,
    uint StorageFlags,
    uint AdapterDeviceId,
    uint AdapterDriverBuild,
    uint AdapterVendorId,
    ushort VideoMemoryMegabytes,
    ushort DirectXVersion,
    byte OsBits,
    byte OsServicePackMajor,
    byte OsServicePackMinor)
{
    public const int Length = 96;
    public const byte RecordVersion = 3;
    public const uint StorageProbed = 2;
    public const uint OpticalDrivePresent = 4;
    public const uint OpticalProbeFailed = 8;
    public const ushort DirectX9 = 0x9020;
    private const int MegabyteRounding = 0x80000;
    private const int MegabyteShift = 20;

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        var span = bytes.AsSpan();
        span[0] = RecordVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], Length);
        span[4] = LogicalProcessors;
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], ProcessorFeatures);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], ProcessorMegahertz);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], ProcessorSignature);
        Encoding.ASCII.GetBytes(ProcessorVendor.PadRight(12, '\0')[..12], span[12..24]);
        span[24] = OsMajorVersion;
        span[25] = OsMinorVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], MemoryMegabytes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], StorageFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], AdapterDeviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], AdapterDriverBuild);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], AdapterVendorId);
        BinaryPrimitives.WriteUInt16LittleEndian(span[44..], VideoMemoryMegabytes);
        BinaryPrimitives.WriteUInt16LittleEndian(span[46..], DirectXVersion);
        span[92] = OsBits;
        span[93] = OsServicePackMajor;
        span[94] = OsServicePackMinor;
        return bytes;
    }

    public static SystemRecord Parse(ReadOnlySpan<byte> bytes) => new(
        bytes[4],
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
        Encoding.ASCII.GetString(bytes[12..24]).TrimEnd('\0'),
        bytes[24],
        bytes[25],
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[44..]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[46..]),
        bytes[92],
        bytes[93],
        bytes[94]);

    public static SystemRecord FromThisMachine()
    {
        var (vendor, signature, features) = ProcessorIdentity();
        var (deviceId, driverBuild, vendorId) = MachineQueries.PrimaryAdapter();
        var (osMajor, osMinor, servicePackMajor, servicePackMinor) = MachineQueries.OsVersion();
        uint storage = StorageProbed | (MachineQueries.ProbeOpticalDrive() switch
        {
            null => OpticalProbeFailed,
            true => OpticalDrivePresent,
            false => 0u,
        });
        return new SystemRecord(
            (byte)MachineQueries.LogicalProcessors(),
            features,
            MachineQueries.MeasureProcessorMegahertz(),
            signature,
            vendor,
            osMajor,
            osMinor,
            ToMegabytes(MachineQueries.TotalPhysicalMemory()),
            storage,
            deviceId,
            driverBuild,
            vendorId,
            ToMegabytes(MachineQueries.TotalVideoMemory()),
            File.Exists(Path.Combine(Environment.SystemDirectory, "d3d9.dll")) ? DirectX9 : (ushort)0,
            (byte)(Environment.Is64BitOperatingSystem ? 64 : 32),
            servicePackMajor,
            servicePackMinor);
    }

    private static ushort ToMegabytes(ulong bytes) => (ushort)Math.Min(ushort.MaxValue, (bytes + MegabyteRounding) >> MegabyteShift);

    private static (string Vendor, ushort Signature, ushort Features) ProcessorIdentity()
    {
        var (highestLeaf, vendorB, vendorC, vendorD) = X86Base.CpuId(0, 0);
        var vendorBytes = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(vendorBytes, vendorB);
        BinaryPrimitives.WriteInt32LittleEndian(vendorBytes.AsSpan(4), vendorD);
        BinaryPrimitives.WriteInt32LittleEndian(vendorBytes.AsSpan(8), vendorC);
        string vendor = Encoding.ASCII.GetString(vendorBytes);
        if (highestLeaf == 0) return (vendor, 0, 0);

        var (signature, _, featureC, featureD) = X86Base.CpuId(1, 0);
        int extendedD = 0;
        if ((uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax >= 0x80000001) extendedD = X86Base.CpuId(unchecked((int)0x80000001), 0).Edx;

        ushort features = 0;
        if (vendor == "AuthenticAMD" && extendedD < 0) features |= 0x1;
        if ((featureD & 0x8000) != 0) features |= 0x2;
        if ((featureC & 0x80) != 0) features |= 0x4;
        if ((featureD & 0x10000000) != 0) features |= 0x8;
        if ((featureD & 0x800000) != 0) features |= 0x10;
        if ((featureD & 0x40000) != 0) features |= 0x20;
        if ((featureD & 0x2000000) != 0) features |= 0x40;
        if ((featureD & 0x4000000) != 0) features |= 0x80;
        if ((featureC & 0x1) != 0) features |= 0x100;
        if ((featureD & 0x10) != 0) features |= 0x200;
        return (vendor, (ushort)(signature & 0xFFF), features);
    }
}

internal static unsafe class MachineQueries
{
    private const int MegahertzSamples = 5;
    private const uint SampleMilliseconds = 100;
    private const int RelationProcessorCore = 0;
    private const int ProcessorInformationLength = 32;
    private const int AdapterIdentifierLength = 1100;
    private const int AdapterDriverVersionOffset = 1056;
    private const int AdapterVendorIdOffset = 1064;
    private const int AdapterDeviceIdOffset = 1068;
    private const uint Direct3DSdkVersion = 32;
    private const uint VideoMemoryCaps = 0x10004000;
    private const uint MediaTypesControlCode = 0x2D0C04;
    private const uint DvdDeviceType = 0x33;
    private const uint DiskDeviceType = 0x2;
    private const uint PresentInterfaces = 0x12;
    private const int InsufficientBuffer = 122;
    private static readonly Guid CdromInterface = new("53f56308-b6bf-11d0-94f2-00a0c91efb8b");
    private static readonly Guid DirectDraw7Interface = new("15e65ec0-3b9c-11d2-b92f-00609797ea5b");
    private static readonly byte[] ReadTimestampCode = { 0x0F, 0x31, 0x48, 0xC1, 0xE2, 0x20, 0x48, 0x09, 0xD0, 0xC3 };

    public static int LogicalProcessors()
    {
        uint length = 0;
        GetLogicalProcessorInformation(null, ref length);
        if (length == 0) return Environment.ProcessorCount;
        var buffer = new byte[length];
        fixed (byte* information = buffer)
            if (!GetLogicalProcessorInformation(information, ref length)) return Environment.ProcessorCount;
        int logical = 0;
        for (int offset = 0; offset + ProcessorInformationLength <= length; offset += ProcessorInformationLength)
            if (BitConverter.ToInt32(buffer, offset + 8) == RelationProcessorCore) logical += BitOperations.PopCount(BitConverter.ToUInt64(buffer, offset));
        return logical == 0 ? Environment.ProcessorCount : logical;
    }

    public static ushort MeasureProcessorMegahertz()
    {
        byte* code = (byte*)VirtualAlloc(null, (nuint)ReadTimestampCode.Length, 0x3000, 0x40);
        if (code == null) return 0;
        try
        {
            ReadTimestampCode.CopyTo(new Span<byte>(code, ReadTimestampCode.Length));
            var readTimestamp = (delegate* unmanaged<ulong>)code;
            ulong total = 0;
            for (int sample = 0; sample < MegahertzSamples; sample++)
            {
                uint edge = timeGetTime();
                uint start;
                while ((start = timeGetTime()) == edge) { }
                ulong first = readTimestamp();
                uint elapsed;
                while ((elapsed = timeGetTime() - start) < SampleMilliseconds) { }
                ulong second = readTimestamp();
                total += (second + 100UL * elapsed - first) / (1000UL * elapsed);
            }
            return (ushort)(total / MegahertzSamples);
        }
        finally
        {
            VirtualFree(code, 0, 0x8000);
        }
    }

    public static (byte Major, byte Minor, byte ServicePackMajor, byte ServicePackMinor) OsVersion()
    {
        var information = new byte[284];
        BitConverter.TryWriteBytes(information, information.Length);
        fixed (byte* version = information)
            if (RtlGetVersion(version) != 0) return (0, 0, 0, 0);
        return (information[4], information[8], information[0x114], information[0x116]);
    }

    public static ulong TotalPhysicalMemory()
    {
        var status = new byte[64];
        BitConverter.TryWriteBytes(status, status.Length);
        fixed (byte* memory = status)
            if (!GlobalMemoryStatusEx(memory)) return 0;
        return BitConverter.ToUInt64(status, 8);
    }

    public static (uint DeviceId, uint DriverBuild, uint VendorId) PrimaryAdapter()
    {
        if (!NativeLibrary.TryLoad("d3d9.dll", out var library)) return (0, 0, 0);
        try
        {
            var create = (delegate* unmanaged<uint, nint**>)NativeLibrary.GetExport(library, "Direct3DCreate9");
            nint** direct3D = create(Direct3DSdkVersion);
            if (direct3D == null) return (0, 0, 0);
            try
            {
                var identifier = new byte[AdapterIdentifierLength];
                fixed (byte* adapter = identifier)
                {
                    var getAdapterIdentifier = (delegate* unmanaged<nint**, uint, uint, byte*, int>)(*direct3D)[5];
                    if (getAdapterIdentifier(direct3D, 0, 0, adapter) < 0) return (0, 0, 0);
                }
                return (BitConverter.ToUInt32(identifier, AdapterDeviceIdOffset), BitConverter.ToUInt16(identifier, AdapterDriverVersionOffset),
                    BitConverter.ToUInt32(identifier, AdapterVendorIdOffset));
            }
            finally
            {
                Release(direct3D);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public static ulong TotalVideoMemory()
    {
        if (!NativeLibrary.TryLoad("ddraw.dll", out var library)) return 0;
        nint** directDraw = null;
        nint** directDraw7 = null;
        try
        {
            var create = (delegate* unmanaged<Guid*, nint***, nint, int>)NativeLibrary.GetExport(library, "DirectDrawCreate");
            if (create(null, &directDraw, 0) < 0) return 0;
            Guid interfaceId = DirectDraw7Interface;
            var queryInterface = (delegate* unmanaged<nint**, Guid*, nint***, int>)(*directDraw)[0];
            if (queryInterface(directDraw, &interfaceId, &directDraw7) < 0) return 0;
            uint* caps = stackalloc uint[4];
            caps[0] = VideoMemoryCaps;
            caps[1] = caps[2] = caps[3] = 0;
            uint totalMemory = 0;
            uint freeMemory = 0;
            var getAvailableVideoMemory = (delegate* unmanaged<nint**, uint*, uint*, uint*, int>)(*directDraw7)[23];
            return getAvailableVideoMemory(directDraw7, caps, &totalMemory, &freeMemory) < 0 ? 0 : totalMemory;
        }
        finally
        {
            if (directDraw7 != null) Release(directDraw7);
            if (directDraw != null) Release(directDraw);
            NativeLibrary.Free(library);
        }
    }

    public static bool? ProbeOpticalDrive()
    {
        Guid cdrom = CdromInterface;
        nint devices = SetupDiGetClassDevsW(&cdrom, null, 0, PresentInterfaces);
        if (devices == -1) return null;
        bool probed = false;
        bool dvd = false;
        try
        {
            byte* interfaceData = stackalloc byte[32];
            for (uint index = 0; ; index++)
            {
                new Span<byte>(interfaceData, 32).Clear();
                *(uint*)interfaceData = 32;
                if (!SetupDiEnumDeviceInterfaces(devices, null, &cdrom, index, interfaceData)) break;
                uint required = 0;
                SetupDiGetDeviceInterfaceDetailW(devices, interfaceData, null, 0, &required, null);
                if (Marshal.GetLastPInvokeError() != InsufficientBuffer) break;
                var detail = new byte[required];
                string path;
                fixed (byte* detailBytes = detail)
                {
                    *(uint*)detailBytes = 8;
                    if (!SetupDiGetDeviceInterfaceDetailW(devices, interfaceData, detailBytes, required, null, null)) break;
                    path = new string((char*)(detailBytes + 4));
                }
                if (path.Contains("dvd", StringComparison.OrdinalIgnoreCase)) dvd = probed = true;
                nint drive = CreateFileW(path, 0xC0000000, 3, 0, 3, 0, 0);
                if (drive == -1) continue;
                var media = new byte[0x800];
                fixed (byte* mediaTypes = media)
                {
                    uint returned;
                    if (DeviceIoControl(drive, MediaTypesControlCode, null, 0, mediaTypes, (uint)media.Length, &returned, 0))
                    {
                        uint deviceType = *(uint*)mediaTypes;
                        if (deviceType == DvdDeviceType) dvd = true;
                        else if (deviceType == DiskDeviceType) dvd = false;
                        probed = true;
                    }
                }
                CloseHandle(drive);
                if (dvd) break;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devices);
        }
        return probed ? dvd : null;
    }

    private static void Release(nint** comObject) => ((delegate* unmanaged<nint**, uint>)(*comObject)[2])(comObject);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(byte* buffer, ref uint length);

    [DllImport("kernel32")]
    private static extern void* VirtualAlloc(void* address, nuint size, uint allocationType, uint protection);

    [DllImport("kernel32")]
    private static extern bool VirtualFree(void* address, nuint size, uint freeType);

    [DllImport("kernel32")]
    private static extern bool GlobalMemoryStatusEx(byte* status);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32")]
    private static extern bool DeviceIoControl(nint device, uint controlCode, void* input, uint inputLength, void* output, uint outputLength, uint* returned, nint overlapped);

    [DllImport("kernel32")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("ntdll")]
    private static extern int RtlGetVersion(byte* versionInformation);

    [DllImport("winmm")]
    private static extern uint timeGetTime();

    [DllImport("setupapi", SetLastError = true)]
    private static extern nint SetupDiGetClassDevsW(Guid* classGuid, char* enumerator, nint parent, uint flags);

    [DllImport("setupapi", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint devices, void* deviceInfo, Guid* interfaceClass, uint index, void* interfaceData);

    [DllImport("setupapi", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(nint devices, void* interfaceData, void* detail, uint detailLength, uint* required, void* deviceInfo);

    [DllImport("setupapi")]
    private static extern bool SetupDiDestroyDeviceInfoList(nint devices);
}
