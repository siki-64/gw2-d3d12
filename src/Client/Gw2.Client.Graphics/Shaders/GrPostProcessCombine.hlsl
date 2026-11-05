#pragma program PostProcessCombine vertex=MainVertex pixel=MainPixel
#pragma program DistortedPostProcessCombine vertex=DistortedGridVertex pixel=MainPixel

float4 SaturationConstant;
float4 TintTargetConstant;
float4 TintConstant;
float4 BloomAmountConstant;
float4 DistortionConstant;
float UseColorLookup;
Texture2D Scene;
SamplerState SceneSampler;
Texture2D Bloom;
SamplerState BloomSampler;
Texture2D ColorLookup;
SamplerState ColorLookupSampler;

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

static const float TwoPi = 6.283185;
static const float DistortionEdgeFade = 10.0;
static const float DistortionFadeCutoff = 0.001;

VertexOutput DistortedGridVertex(float3 position : POSITION0)
{
    float2 grid = position.xy;
    float2 wave = sin((grid.yx * 2.0 + DistortionConstant.wz - 1.0) * TwoPi + DistortionConstant.y) * DistortionConstant.x;
    float2 edge = min(min(grid, 1.0 - grid) * DistortionEdgeFade, 1.0);
    float fade = edge.x * edge.y;
    float2 warped = grid + (fade < DistortionFadeCutoff ? 0.0 : wave * fade);
    VertexOutput output;
    output.Position = float4(warped.x * 2.0 - 1.0, 1.0 - warped.y * 2.0, 0.0, 1.0);
    output.TextureCoordinate = grid;
    return output;
}

float4 MainPixel(VertexOutput input) : SV_TARGET
{
    float3 color = Scene.SampleLevel(SceneSampler, input.TextureCoordinate, 0).rgb;
    float3 chroma = color / dot(color, 1.0 / 3.0);
    float closeness = 1.0 - min(dot(abs(chroma - TintTargetConstant.rgb), 1.0 / 3.0), 1.0);
    float tintWeight = pow(closeness * 0.99999 + 0.00001, TintTargetConstant.w) * TintConstant.w;
    float luma = dot(color, float3(0.2125, 0.7154, 0.0721));
    float3 saturated = lerp(luma, color, SaturationConstant.w);
    float3 graded = lerp(saturated, TintConstant.rgb * luma, tintWeight);
    float3 bloom = Bloom.SampleLevel(BloomSampler, input.TextureCoordinate, 0).rgb * BloomAmountConstant.rgb;
    if (UseColorLookup == 0.0) return float4(bloom * 2.0 + graded, 0.0);
    float3 screened = saturate(1.0 - (1.0 - graded) * (1.0 - 2.0 * bloom));
    float sliceCoordinate = screened.b * 15.0;
    float slice = floor(sliceCoordinate);
    float2 lower = float2(screened.r * 0.05859375 + 0.001953125 + slice * 0.0625, screened.g * 0.9375 + 0.03125);
    float3 lowerColor = ColorLookup.SampleLevel(ColorLookupSampler, lower, 0).rgb;
    float3 upperColor = ColorLookup.SampleLevel(ColorLookupSampler, lower + float2(0.0625, 0.0), 0).rgb;
    return float4(lerp(lowerColor, upperColor, sliceCoordinate - slice), 0.0);
}
