#pragma program Cover vertex=MainVertex pixel=CoverPixel

float4 MainVertex(float3 position : POSITION0) : SV_POSITION
{
    return float4(position.xy, 1.0, 1.0);
}

float4 CoverPixel() : SV_TARGET
{
    return float4(0.0, 0.0, 0.0, 1.0);
}
