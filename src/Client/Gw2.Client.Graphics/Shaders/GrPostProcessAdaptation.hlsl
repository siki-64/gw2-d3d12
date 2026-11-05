#pragma program Downsample vertex=MainVertex pixel=DownsamplePixel
#pragma program LuminosityAverage vertex=MainVertex pixel=LuminosityAveragePixel
#pragma program ToneMap vertex=MainVertex pixel=ToneMapPixel
#pragma program DepthBlur vertex=MainVertex pixel=DepthBlurPixel
#pragma program LightRayMask vertex=MainVertex pixel=LightRayMaskPixel
#pragma program LightRays vertex=MainVertex pixel=LightRaysPixel

float4 ScreenDimsInv;
float4 DownsampleLevel;
float4 Dim;
float4 Bright;
float4 DarkMin;
float4 DarkMax;
float4 BrightMin;
float4 BrightMax;
float4 DarkCurveCoeff;
float4 DarkCurveExponent;
float4 DofParams;
float4 LightRayParams;
float4 LightRayScreenPos;
Texture2D Source;
SamplerState SourceSampler;
Texture2D Second;
SamplerState SecondSampler;

static const float3 Rec709Luma = float3(0.2126, 0.7152, 0.0722);
static const float3 RgbToX = float3(0.4124, 0.3576, 0.1805);
static const float3 RgbToZ = float3(0.0193, 0.1192, 0.9505);
static const float3 XyzToR = float3(3.2405, -1.5371, -0.4985);
static const float3 XyzToG = float3(-0.9693, 1.876, 0.0416);
static const float3 XyzToB = float3(0.0556, -0.204, 1.0572);

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

float4 DownsamplePixel(VertexOutput input) : SV_TARGET
{
    float2 texel = ScreenDimsInv.xy;
    float2 center = input.TextureCoordinate + 2.0 * texel;
    float4 average = 0.25 * Source.SampleLevel(SourceSampler, center + float2(texel.x, -texel.y), 0);
    average += 0.25 * Source.SampleLevel(SourceSampler, center - texel, 0);
    average += 0.25 * Source.SampleLevel(SourceSampler, center + float2(-texel.x, texel.y), 0);
    average += 0.25 * Source.SampleLevel(SourceSampler, center + texel, 0);
    return float4(saturate(average.rgb - DownsampleLevel.rgb), saturate(average.a));
}

float4 LuminosityAveragePixel(VertexOutput input) : SV_TARGET
{
    float3 sum = 0.0;
    for (int column = 0; column < 11; column++)
        for (int row = 0; row < 11; row++)
            sum += Source.SampleLevel(SourceSampler, float2(column, row) * 0.1, 0).rgb;
    float luminance = saturate(dot(Rec709Luma, sum * 0.008264));
    float previous = Second.SampleLevel(SecondSampler, float2(0.0, 0.0), 0).r;
    float adapted = previous < luminance ? min(luminance, previous + Bright.x) : max(luminance, previous - Dim.x);
    float brightLimit = max(BrightMax.x, 0.2);
    float darkness = saturate((DarkMax.x - adapted) / (DarkMax.x - DarkMin.x));
    float brightness = saturate((adapted - brightLimit) / (brightLimit - BrightMin.x));
    return float4(adapted, darkness, brightness, max(1.0 - (brightness + darkness), 0.0));
}

float Dither(float2 pixel)
{
    return frac(frac(dot(pixel + float2(1.0, 0.0), float2(0.067111, 0.005837))) * 52.982918) - 0.5;
}

float4 ToneMapPixel(VertexOutput input) : SV_TARGET
{
    float4 source = Source.SampleLevel(SourceSampler, input.TextureCoordinate, 0);
    float3 color = source.rgb * source.rgb;
    float luminance = dot(Rec709Luma, color);
    float dark = exp2(log2(luminance) * max(DarkCurveExponent.x, 0.00001)) * DarkCurveCoeff.x;
    float bright = min(luminance, (luminance * 4.0) * (luminance * 4.0) * 0.166667);
    float3 weights = Second.SampleLevel(SecondSampler, float2(0.0, 0.0), 0).yzw;
    float mapped = saturate(luminance * weights.z + (dark * weights.x + bright * weights.y));
    float x = dot(RgbToX, color);
    float sum = dot(RgbToZ, color) + (luminance + x) + 0.00001;
    float chromaX = x / sum;
    float chromaY = luminance / sum;
    float scale = mapped / (chromaY + 0.00001);
    float3 xyz = float3(scale * chromaX, mapped, scale * (1.0 - chromaX - chromaY));
    float3 rgb = sqrt(float3(dot(XyzToR, xyz), dot(XyzToG, xyz), dot(XyzToB, xyz)));
    return float4(saturate(rgb + Dither(input.Position.xy) * 0.003922), saturate(source.a));
}

float4 DepthBlurPixel(VertexOutput input) : SV_TARGET
{
    float2 uv = input.Position.xy * ScreenDimsInv.xy;
    float depth = Second.SampleLevel(SecondSampler, uv, 0).r;
    float radius = ceil(0.9999 - depth) * saturate((depth * ScreenDimsInv.w - DofParams.x) / DofParams.y);
    float near = radius * 0.66;
    float2 diagonal = near * near * ScreenDimsInv.xy;
    float2 axis = near * ScreenDimsInv.xy;
    float4 last = Source.SampleLevel(SourceSampler, uv - diagonal, 0);
    float3 sum = Source.SampleLevel(SourceSampler, uv + float2(diagonal.x, -diagonal.y), 0).rgb + last.rgb;
    sum += Source.SampleLevel(SourceSampler, uv + float2(-diagonal.x, diagonal.y), 0).rgb;
    sum += Source.SampleLevel(SourceSampler, uv + diagonal, 0).rgb;
    sum += Source.SampleLevel(SourceSampler, uv + float2(-axis.x, 0.0), 0).rgb;
    sum += Source.SampleLevel(SourceSampler, uv + float2(axis.x, 0.0), 0).rgb;
    sum += Source.SampleLevel(SourceSampler, uv + float2(0.0, -axis.y), 0).rgb;
    sum += Source.SampleLevel(SourceSampler, uv + float2(0.0, axis.y), 0).rgb;
    return float4(sum * 0.125, last.a);
}

static const float LightRaySourceBit = 0.03137255 - 0.00001;
static const int LightRaySamples = 32;

float LightRaySource(float alpha)
{
    return frac(alpha / (2.0 * LightRaySourceBit)) * 2.0 * LightRaySourceBit > LightRaySourceBit ? 1.0 : 0.0;
}

float4 LightRayMaskPixel(VertexOutput input) : SV_TARGET
{
    float2 uv = input.TextureCoordinate;
    float2 texel = ScreenDimsInv.xy;
    float count = LightRaySource(Source.SampleLevel(SourceSampler, uv, 0).a);
    count += LightRaySource(Source.SampleLevel(SourceSampler, uv - texel, 0).a);
    count += LightRaySource(Source.SampleLevel(SourceSampler, uv + float2(texel.x, -texel.y), 0).a);
    count += LightRaySource(Source.SampleLevel(SourceSampler, uv + float2(-texel.x, texel.y), 0).a);
    count += LightRaySource(Source.SampleLevel(SourceSampler, uv + texel, 0).a);
    return (count / 5.0).xxxx;
}

float4 LightRaysPixel(VertexOutput input) : SV_TARGET
{
    float2 uv = input.TextureCoordinate;
    float2 step = (uv - LightRayScreenPos.xy) * (LightRayParams.x / LightRaySamples);
    float4 color = Source.SampleLevel(SourceSampler, uv, 0);
    float weight = 1.0;
    for (int index = 0; index < LightRaySamples; index++)
    {
        uv -= step;
        color += weight * Second.SampleLevel(SecondSampler, uv, 0).w * Source.SampleLevel(SourceSampler, uv, 0) * LightRayParams.z;
        weight *= LightRayParams.y;
    }
    return color * LightRayParams.w;
}
