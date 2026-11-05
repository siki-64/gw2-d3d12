#pragma program Combine vertex=MainVertex pixel=CombinePixel

Texture2D DynamicShadow;
Texture2D BakedShadow;
float2 BakedRingOffset;
float4 TapOffsets[5];
float TapWeights[5];
float WeightSum;
SamplerState DynamicShadowSampler;
SamplerState BakedShadowSampler;

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
};

VertexOutput MainVertex(float3 position : POSITION0)
{
    VertexOutput output;
    output.Position = float4(position, 1.0);
    output.TextureCoordinate = position.xy * float2(0.5, -0.5) + 0.5;
    return output;
}

float4 CombinePixel(VertexOutput input) : SV_TARGET
{
    float2 moments = 0;
    [unroll]
    for (int tap = 0; tap < 5; tap++)
    {
        float2 coordinate = input.TextureCoordinate + TapOffsets[tap].xy;
        float dynamicDepth = DynamicShadow.SampleLevel(DynamicShadowSampler, coordinate, 0).x;
        float bakedDepth = BakedShadow.SampleLevel(BakedShadowSampler, coordinate + BakedRingOffset, 0).x;
        float depth = min(dynamicDepth, bakedDepth);
        moments += TapWeights[tap] * float2(depth, depth * depth);
    }
    return float4(moments / WeightSum, 0, 0);
}
