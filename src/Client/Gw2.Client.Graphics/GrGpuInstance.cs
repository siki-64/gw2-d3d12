using System.Numerics;
using System.Runtime.InteropServices;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrGpuInstance
{
    public const int SizeInBytes = 112;
    public const uint NoGroup = uint.MaxValue;

    public GrVertexInstance Row;
    public Vector3 BoundsMin;
    public uint LodState;
    public Vector3 BoundsMax;
    public uint LodLevel;
    public uint DrawGroup;
    public uint CasterGroup;
    public uint Unused0;
    public uint Unused1;
}
