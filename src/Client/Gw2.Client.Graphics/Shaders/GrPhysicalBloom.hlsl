#pragma program Prefilter vertex=MainVertex pixel=PrefilterPixel
#pragma program Downsample vertex=MainVertex pixel=DownsamplePixel
#pragma program Upsample vertex=MainVertex pixel=UpsamplePixel
#pragma program Resolve vertex=MainVertex pixel=ResolvePixel

float4 SourceTexelSize;
float4 DownsampleLevel;
float4 BloomResolve;
Texture2D Source;
SamplerState SourceSampler;

static const float3 LumaWeights = float3(0.2125, 0.7154, 0.0721);
static const float CenterGroupWeight = 0.5;
static const float CornerGroupWeight = 0.125;

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

float3 Tap(float2 textureCoordinate, float2 texelOffset)
{
    return Source.SampleLevel(SourceSampler, textureCoordinate + texelOffset * SourceTexelSize.xy, 0).rgb;
}

void ThirteenTapGroups(float2 textureCoordinate, out float3 center, out float3 corners[4])
{
    float3 a = Tap(textureCoordinate, float2(-2.0, -2.0));
    float3 b = Tap(textureCoordinate, float2(0.0, -2.0));
    float3 c = Tap(textureCoordinate, float2(2.0, -2.0));
    float3 d = Tap(textureCoordinate, float2(-1.0, -1.0));
    float3 e = Tap(textureCoordinate, float2(1.0, -1.0));
    float3 f = Tap(textureCoordinate, float2(-2.0, 0.0));
    float3 g = Tap(textureCoordinate, float2(0.0, 0.0));
    float3 h = Tap(textureCoordinate, float2(2.0, 0.0));
    float3 i = Tap(textureCoordinate, float2(-1.0, 1.0));
    float3 j = Tap(textureCoordinate, float2(1.0, 1.0));
    float3 k = Tap(textureCoordinate, float2(-2.0, 2.0));
    float3 l = Tap(textureCoordinate, float2(0.0, 2.0));
    float3 m = Tap(textureCoordinate, float2(2.0, 2.0));
    center = (d + e + i + j) * 0.25;
    corners[0] = (a + b + f + g) * 0.25;
    corners[1] = (b + c + g + h) * 0.25;
    corners[2] = (f + g + k + l) * 0.25;
    corners[3] = (g + h + l + m) * 0.25;
}

float3 ExeIntensity(float3 color)
{
    return saturate(color * dot(color, LumaWeights) - DownsampleLevel.rgb);
}

float KarisWeight(float3 color)
{
    return 1.0 / (1.0 + dot(color, LumaWeights));
}

float4 PrefilterPixel(VertexOutput input) : SV_TARGET
{
    float3 center;
    float3 corners[4];
    ThirteenTapGroups(input.TextureCoordinate, center, corners);
    float3 intensity = ExeIntensity(center);
    float weight = CenterGroupWeight * KarisWeight(intensity);
    float3 sum = intensity * weight;
    float weightSum = weight;

    [unroll]
    for (int corner = 0; corner < 4; corner++)
    {
        intensity = ExeIntensity(corners[corner]);
        weight = CornerGroupWeight * KarisWeight(intensity);
        sum += intensity * weight;
        weightSum += weight;
    }

    return float4(sum / weightSum, 1.0);
}

float4 DownsamplePixel(VertexOutput input) : SV_TARGET
{
    float3 center;
    float3 corners[4];
    ThirteenTapGroups(input.TextureCoordinate, center, corners);
    return float4(center * CenterGroupWeight + (corners[0] + corners[1] + corners[2] + corners[3]) * CornerGroupWeight, 1.0);
}

float4 UpsamplePixel(VertexOutput input) : SV_TARGET
{
    float2 uv = input.TextureCoordinate;
    float3 sum = Tap(uv, float2(0.0, 0.0)) * 4.0;
    sum += (Tap(uv, float2(0.0, -1.0)) + Tap(uv, float2(-1.0, 0.0)) + Tap(uv, float2(1.0, 0.0)) + Tap(uv, float2(0.0, 1.0))) * 2.0;
    sum += Tap(uv, float2(-1.0, -1.0)) + Tap(uv, float2(1.0, -1.0)) + Tap(uv, float2(-1.0, 1.0)) + Tap(uv, float2(1.0, 1.0));
    return float4(sum / 16.0, 1.0);
}

float4 ResolvePixel(VertexOutput input) : SV_TARGET
{
    return float4(Tap(input.TextureCoordinate, float2(0.0, 0.0)) * BloomResolve.x, 1.0);
}
