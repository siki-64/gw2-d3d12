#pragma program GpuCull compute=main

struct GpuInstance
{
    float4 RowX;
    float4 RowY;
    float4 RowZ;
    float4 Color;
    float3 BoundsMin;
    uint LodState;
    float3 BoundsMax;
    uint LodLevel;
    uint DrawGroup;
    uint CasterGroup;
    uint2 Unused;
};

cbuffer Cull : register(b0)
{
    float4 OutwardPlanes[6];
    uint PlaneCount;
    uint InstanceCount;
    uint CullsCasters;
    uint TestsOcclusion;
    row_major float4x4 OcclusionViewProjection;
    uint2 OcclusionDepthSize;
    uint2 UnusedOcclusion;
};

StructuredBuffer<GpuInstance> Instances : register(t0);
StructuredBuffer<uint> LodLevels : register(t1);
Texture2D<float> HiZ : register(t2);
RWByteAddressBuffer Rows : register(u0);
RWByteAddressBuffer Arguments : register(u1);

static const uint NoGroup = 0xFFFFFFFF;
static const uint ArgumentStride = 20;
static const uint InstanceCountOffset = 4;
static const uint StartInstanceOffset = 16;
static const uint RowStride = 64;

bool OutsidePlane(float4 plane, float3 boundsMin, float3 boundsMax)
{
    float3 nearestCorner = float3(
        plane.x >= 0 ? boundsMin.x : boundsMax.x,
        plane.y >= 0 ? boundsMin.y : boundsMax.y,
        plane.z >= 0 ? boundsMin.z : boundsMax.z);
    return dot(plane.xyz, nearestCorner) + plane.w > 0;
}

uint2 HiZTexel(uint2 depthPixel, uint level, uint2 levelSize)
{
    return min(depthPixel >> (level + 1), levelSize - 1);
}

bool Occluded(float3 boundsMin, float3 boundsMax)
{
    float2 screenMin = float2(1, 1);
    float2 screenMax = float2(0, 0);
    float nearestDepth = 1;
    for (uint corner = 0; corner < 8; corner++)
    {
        float3 position = float3(
            (corner & 1) != 0 ? boundsMax.x : boundsMin.x,
            (corner & 2) != 0 ? boundsMax.y : boundsMin.y,
            (corner & 4) != 0 ? boundsMax.z : boundsMin.z);
        float4 clip = mul(float4(position, 1), OcclusionViewProjection);
        if (clip.w <= 0 || clip.z < 0)
            return false;
        float3 ndc = clip.xyz / clip.w;
        float2 screen = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        screenMin = min(screenMin, screen);
        screenMax = max(screenMax, screen);
        nearestDepth = min(nearestDepth, ndc.z);
    }
    int2 lastPixel = int2(OcclusionDepthSize) - 1;
    uint2 firstPixel = uint2(clamp(int2(floor(saturate(screenMin) * OcclusionDepthSize)), 0, lastPixel));
    uint2 finalPixel = uint2(clamp(int2(floor(saturate(screenMax) * OcclusionDepthSize)), 0, lastPixel));
    uint levelWidth;
    uint levelHeight;
    uint levelCount;
    HiZ.GetDimensions(0, levelWidth, levelHeight, levelCount);
    uint level = 0;
    uint2 first = HiZTexel(firstPixel, 0, uint2(levelWidth, levelHeight));
    uint2 last = HiZTexel(finalPixel, 0, uint2(levelWidth, levelHeight));
    while (level + 1 < levelCount && (last.x - first.x > 1 || last.y - first.y > 1))
    {
        level++;
        HiZ.GetDimensions(level, levelWidth, levelHeight, levelCount);
        first = HiZTexel(firstPixel, level, uint2(levelWidth, levelHeight));
        last = HiZTexel(finalPixel, level, uint2(levelWidth, levelHeight));
    }
    float farthestDepth = max(
        max(HiZ.Load(int3(first, level)), HiZ.Load(int3(last.x, first.y, level))),
        max(HiZ.Load(int3(first.x, last.y, level)), HiZ.Load(int3(last, level))));
    return nearestDepth > farthestDepth;
}

[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= InstanceCount)
        return;
    GpuInstance instance = Instances[id.x];
    uint group = CullsCasters != 0 ? instance.CasterGroup : instance.DrawGroup;
    if (group == NoGroup || LodLevels[instance.LodState] != instance.LodLevel)
        return;
    for (uint plane = 0; plane < PlaneCount; plane++)
        if (OutsidePlane(OutwardPlanes[plane], instance.BoundsMin, instance.BoundsMax))
            return;
    if (TestsOcclusion != 0 && Occluded(instance.BoundsMin, instance.BoundsMax))
        return;
    uint argument = group * ArgumentStride;
    uint slot;
    Arguments.InterlockedAdd(argument + InstanceCountOffset, 1, slot);
    uint row = (Arguments.Load(argument + StartInstanceOffset) + slot) * RowStride;
    Rows.Store4(row, asuint(instance.RowX));
    Rows.Store4(row + 16, asuint(instance.RowY));
    Rows.Store4(row + 32, asuint(instance.RowZ));
    Rows.Store4(row + 48, asuint(instance.Color));
}
