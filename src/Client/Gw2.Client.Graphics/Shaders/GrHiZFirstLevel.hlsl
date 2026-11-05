#pragma program HiZFirstLevel compute=main

cbuffer Reduce : register(b0)
{
    uint2 SourceSize;
    uint2 TargetSize;
};

Texture2D<float> Depth : register(t0);
RWTexture2D<float> Target : register(u0);

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= TargetSize.x || id.y >= TargetSize.y)
        return;
    uint2 first = id.xy * 2;
    uint2 last = uint2(
        id.x == TargetSize.x - 1 ? SourceSize.x - 1 : first.x + 1,
        id.y == TargetSize.y - 1 ? SourceSize.y - 1 : first.y + 1);
    float farthest = 0;
    for (uint y = first.y; y <= last.y; y++)
        for (uint x = first.x; x <= last.x; x++)
            farthest = max(farthest, Depth.Load(int3(x, y, 0)));
    Target[id.xy] = farthest;
}
