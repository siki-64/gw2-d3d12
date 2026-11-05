#pragma program DownsampleIntensity vertex=MainVertex pixel=DownsampleIntensityPixel
#pragma program BlurHorizontal vertex=MainVertex pixel=BlurHorizontalPixel
#pragma program BlurVertical vertex=MainVertex pixel=BlurVerticalPixel

float4 ScreenDimsInv;
float4 DownsampleLevel;
float4 BlurParams;
Texture2D Source;
SamplerState SourceSampler;

static const float4 BlurWeights = float4(0.147914, 0.487427, 0.328026, 0.036633);
static const float4 BlurOffsets = float4(-2.247664, -0.444672, 1.339244, 3.0);

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

float4 SampleSource(float2 textureCoordinate)
{
    return Source.SampleLevel(SourceSampler, textureCoordinate, 0);
}

float4 DownsampleIntensityPixel(VertexOutput input) : SV_TARGET
{
    float2 texel = ScreenDimsInv.xy;
    float2 center = input.TextureCoordinate + 2.0 * texel;
    float4 average = 0.25 * SampleSource(center + float2(texel.x, -texel.y));
    average += 0.25 * SampleSource(center - texel);
    average += 0.25 * SampleSource(center + float2(-texel.x, texel.y));
    average += 0.25 * SampleSource(center + texel);
    float luma = dot(average.rgb, float3(0.2125, 0.7154, 0.0721));
    return float4(saturate(average.rgb * luma - DownsampleLevel.rgb), saturate(average.a));
}

float4 Blur(float2 textureCoordinate, float2 direction)
{
    float2 step = BlurParams.xy * BlurParams.z * direction;
    float4 sum = BlurWeights.x * SampleSource(textureCoordinate + BlurOffsets.x * step);
    sum += BlurWeights.y * SampleSource(textureCoordinate + BlurOffsets.y * step);
    sum += BlurWeights.z * SampleSource(textureCoordinate + BlurOffsets.z * step);
    sum += BlurWeights.w * SampleSource(textureCoordinate + BlurOffsets.w * step);
    return float4(sum.rgb * BlurParams.w, sum.a);
}

float4 BlurHorizontalPixel(VertexOutput input) : SV_TARGET
{
    return Blur(input.TextureCoordinate, float2(1.0, 0.0));
}

float4 BlurVerticalPixel(VertexOutput input) : SV_TARGET
{
    return Blur(input.TextureCoordinate, float2(0.0, 1.0));
}
