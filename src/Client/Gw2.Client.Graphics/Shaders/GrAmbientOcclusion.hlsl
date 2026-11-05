#pragma program Downsample vertex=MainVertex pixel=DownsamplePixel
#pragma program Occlusion vertex=MainVertex pixel=OcclusionPixel
#pragma program BlurHorizontal vertex=MainVertex pixel=BlurHorizontalPixel
#pragma program BlurVertical vertex=MainVertex pixel=BlurVerticalPixel
#pragma program Upsample vertex=MainVertex pixel=UpsamplePixel

#define TAP_COUNT 12

float4 TexelOffset;
float4 ScreenDimsInv;
float4 SsaoParams;
float4 SsaoWorldToViewA;
float4 SsaoWorldToViewB;
float4 SsaoWorldToViewC;

Texture2D ssSource : register(t0);
Texture2D ssDepthTex : register(t1);
Texture2D ssNormalTex : register(t2);
Texture2D ssOcclusion : register(t3);
SamplerState ssSourceSampler : register(s0);
SamplerState ssDepthTexSampler : register(s1);
SamplerState ssNormalTexSampler : register(s2);
SamplerState ssOcclusionSampler : register(s3);

static const float2 TapDirections[TAP_COUNT] =
{
    float2(-0.865361, 0.501149),
    float2(0.4977, -0.867349),
    float2(0.003981, 0.999992),
    float2(-0.50459, -0.863359),
    float2(0.869324, 0.494243),
    float2(-0.999968, 0.007962),
    float2(0.861343, -0.508023),
    float2(-0.490776, 0.871286),
    float2(-0.011945, -0.999929),
    float2(0.51145, 0.859313),
    float2(-0.873234, -0.487302),
    float2(0.999873, -0.015928),
};

static const float MaximumTapRadius = 0.03;
static const float NormalBias = 0.4;
static const float NearFadeStart = 20.0;
static const float NearFadeRate = 0.025;
static const float BlurNearOffset = 1.458429;
static const float BlurFarOffset = 3.404;
static const float BlurCenterWeight = 0.15317;
static const float BlurNearWeight = 0.267542;
static const float BlurFarWeight = 0.155872;
static const float UpsampleOffset = 0.9;

struct VertexOutput
{
    float4 Position : SV_POSITION;
};

VertexOutput MainVertex(float4 position : POSITION0)
{
    VertexOutput output;
    output.Position = position;
    return output;
}

float2 CoordinateOf(VertexOutput input)
{
    return input.Position.xy * ScreenDimsInv.xy;
}

float DepthSimilarity(float difference, float tolerance)
{
    return 1.0 - smoothstep(0.0, 1.0, saturate(abs(difference) * tolerance));
}

float4 DownsamplePixel(VertexOutput input) : SV_TARGET0
{
    return ssSource.Sample(ssSourceSampler, CoordinateOf(input));
}

float3 ViewPositionOf(float2 coordinate, float viewDepth)
{
    float2 centered = 1.0 - 2.0 * coordinate;
    float aspect = ScreenDimsInv.y / ScreenDimsInv.x;
    return viewDepth * float3(aspect * centered.x, centered.y * TexelOffset.z, 1.0);
}

float4 OcclusionPixel(VertexOutput input) : SV_TARGET0
{
    float2 coordinate = CoordinateOf(input);
    float3 normal = ssNormalTex.Sample(ssNormalTexSampler, coordinate).xyz * 2.0 - 1.0;
    float3 viewNormal = normalize(float3(-dot(normal, SsaoWorldToViewA.xyz), dot(normal, SsaoWorldToViewB.xyz), dot(normal, SsaoWorldToViewC.xyz)));
    float centerDepth = ssDepthTex.Sample(ssDepthTexSampler, coordinate).x;
    float viewDepth = centerDepth * ScreenDimsInv.w;
    float3 center = ViewPositionOf(coordinate, viewDepth);
    float2 radiusOffset = ViewPositionOf(coordinate + float2(0.0, MaximumTapRadius), viewDepth).xy - center.xy;
    float inverseRadiusSquared = 1.0 / dot(radiusOffset, radiusOffset);
    float texel = ScreenDimsInv.y;
    float radiusStep = (MaximumTapRadius - texel) / TAP_COUNT;
    float2 sums = 0.0;

    [unroll]
    for (int tap = 0; tap < TAP_COUNT; tap++)
    {
        float2 tapCoordinate = coordinate + TapDirections[tap] * (texel + tap * radiusStep);
        float tapDepth = ssDepthTex.Sample(ssDepthTexSampler, tapCoordinate).x * ScreenDimsInv.w;
        float3 difference = ViewPositionOf(tapCoordinate, tapDepth) - center;
        float distanceSquared = dot(difference, difference);
        float radiusShare = distanceSquared * inverseRadiusSquared;
        float rampIn = smoothstep(0.0, 1.0, min(radiusShare * 5.0, 1.0));
        float occlusion = max(dot(difference * rsqrt(distanceSquared), viewNormal) - NormalBias, 0.0) * max(1.0 - radiusShare, 0.0) * rampIn * rampIn;
        float3 worldDifference = SsaoWorldToViewA.xyz * difference.x + SsaoWorldToViewB.xyz * difference.y + SsaoWorldToViewC.xyz * difference.z;
        float fromAbove = smoothstep(0.0, 1.0, saturate((-worldDifference.z * rsqrt(dot(worldDifference, worldDifference)) + 1.0) * 0.5));
        sums += float2(occlusion, occlusion * fromAbove);
    }

    float nearFade = smoothstep(0.0, 1.0, saturate((viewDepth - NearFadeStart) * NearFadeRate));
    float2 visibility = 1.0 - pow(nearFade * sums / 3.0, SsaoParams.w);
    float facing = saturate(viewNormal.z);
    return float4(lerp(visibility.y, visibility.x, facing * facing * 0.75 + 0.25), centerDepth, 1.0, 1.0);
}

float4 Blur(float2 coordinate, float2 axis)
{
    float2 center = ssSource.Sample(ssSourceSampler, coordinate).xy;
    float tolerance = 1.0 / (center.y * 0.0099 + 0.0001);
    float2 offsets[4] = { -BlurFarOffset * axis, -BlurNearOffset * axis, BlurNearOffset * axis, BlurFarOffset * axis };
    float weights[4] = { BlurFarWeight, BlurNearWeight, BlurNearWeight, BlurFarWeight };
    float sum = center.x * BlurCenterWeight;
    float weightSum = BlurCenterWeight;

    [unroll]
    for (int tap = 0; tap < 4; tap++)
    {
        float2 tapValue = ssSource.Sample(ssSourceSampler, coordinate + offsets[tap] * ScreenDimsInv.xy).xy;
        float weight = DepthSimilarity(tapValue.y - center.y, tolerance) * weights[tap];
        sum += tapValue.x * weight;
        weightSum += weight;
    }

    return float4(sum / weightSum, center.yyy);
}

float4 BlurHorizontalPixel(VertexOutput input) : SV_TARGET0
{
    return Blur(CoordinateOf(input), float2(1.0, 0.0));
}

float4 BlurVerticalPixel(VertexOutput input) : SV_TARGET0
{
    return Blur(CoordinateOf(input), float2(0.0, 1.0));
}

float4 UpsamplePixel(VertexOutput input) : SV_TARGET0
{
    float2 coordinate = CoordinateOf(input);
    float depth = ssDepthTex.Sample(ssDepthTexSampler, coordinate).x;
    float tolerance = 1.0 / (depth * 0.00095 + 0.00005);
    float2 offsets[4] = { float2(UpsampleOffset, UpsampleOffset), float2(UpsampleOffset, -UpsampleOffset), float2(-UpsampleOffset, UpsampleOffset), float2(-UpsampleOffset, -UpsampleOffset) };
    float sum = 0.0;
    float weightSum = 0.0;

    [unroll]
    for (int tap = 0; tap < 4; tap++)
    {
        float2 tapValue = ssOcclusion.Sample(ssOcclusionSampler, coordinate + offsets[tap] * ScreenDimsInv.xy).xy;
        float weight = DepthSimilarity(tapValue.y - depth, tolerance);
        sum += tapValue.x * weight;
        weightSum += weight;
    }

    float mean = sum / max(weightSum, 0.00001);
    float center = ssOcclusion.Sample(ssOcclusionSampler, coordinate).x;
    return float4(lerp(center, mean, min(weightSum, 1.0)).xxx, 1.0);
}
