#pragma program March vertex=MainVertex pixel=MainPixel

#define MAXIMUM_CASCADES 4
#define MARCH_STEPS 32

float4 ScreenDimsInv;
float4 ClipToWorld[4];
float4 WorldCameraPos;
float4 FogColorNearMinusFar;
float4 FogColorFar;
float4 FogColorHeight;
float4 FogDepthCue;
float4 FogParam0;
float4 PostLightShSunDir;
float4 WorldToShadowA;
float4 WorldToShadowB;
float4 WorldToShadowC;
float4 ShadowBias;
float4 ShadowMapConfiguration;
float4 ShadowMapCascadeSplitRadii;
float4 ShadowMapCascadeWorldToShadow[MAXIMUM_CASCADES * 4];
float4 ShadowMapCascadeBoundSphereCenters[MAXIMUM_CASCADES];
Texture2D ssDepth;
SamplerState ssDepthSampler;
Texture2D ssShadowNear;
SamplerState ssShadowNearSampler;
Texture2D ssShadow;
SamplerState ssShadowSampler;

static const float JitterOfPixel[4] = { 0.125, 0.625, 0.875, 0.375 };

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

float3 WorldPositionOf(float2 screenCoordinate, float depth)
{
    float4 clip = float4(screenCoordinate * 2.0 - 1.0, depth, 1.0);
    clip.y = -clip.y;
    float4 world = float4(dot(ClipToWorld[0], clip), dot(ClipToWorld[1], clip), dot(ClipToWorld[2], clip), dot(ClipToWorld[3], clip));
    return world.xyz / world.w;
}

float ExeFogAlpha(float3 worldPosition)
{
    float distanceToCamera = distance(worldPosition, WorldCameraPos.xyz);
    float2 fogs = float2(distanceToCamera, worldPosition.z) * FogParam0.xz + FogParam0.yw;
    fogs = 1.0 - saturate(exp2(-fogs));
    float distanceAlpha = min(fogs.x, FogColorFar.w + FogColorNearMinusFar.w * (1.0 - fogs.x));
    float heightAlpha = min(fogs.y, FogColorHeight.w) * min(distanceToCamera * FogDepthCue.x, 1.0);
    return heightAlpha * (1.0 - distanceAlpha) + distanceAlpha;
}

float DistanceFromSunRayToCascadeCenter(float3 worldPosition, float3 sunDirection, int cascade)
{
    float3 center = ShadowMapCascadeBoundSphereCenters[cascade].xyz;
    float3 alongSun = worldPosition - sunDirection * dot(sunDirection, worldPosition - center);
    return length(center - alongSun);
}

float CascadeBias(int cascade)
{
    return cascade == 0 ? ShadowBias.x : cascade == 1 ? ShadowBias.y : cascade == 2 ? ShadowBias.z : ShadowBias.w;
}

float ShadowNearSingleTap(float3 worldPosition, int cascade)
{
    float4 position = float4(worldPosition, 1.0);
    float2 shadowCoordinate = float2(dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 0]), dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 1]));
    float shadowDepth = dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 2]) - CascadeBias(cascade);
    float2 texelPosition = shadowCoordinate / ShadowMapConfiguration.zw - 0.5;
    float2 weight = frac(texelPosition);
    float2 gatherCoordinate = (floor(texelPosition) + 1.0) * ShadowMapConfiguration.zw;
    float4 lit = step(shadowDepth, ssShadowNear.GatherRed(ssShadowNearSampler, gatherCoordinate));
    return lerp(lerp(lit.w, lit.z, weight.x), lerp(lit.x, lit.y, weight.x), weight.y);
}

float ShadowFar(float3 worldPosition)
{
    float4 position = float4(worldPosition, 1.0);
    float2 shadowCoordinate = float2(dot(position, WorldToShadowA), dot(position, WorldToShadowB));
    if (any(shadowCoordinate != saturate(shadowCoordinate))) return 1.0;
    float shadowDepth = dot(position, WorldToShadowC);
    float2 moments = ssShadow.SampleLevel(ssShadowSampler, shadowCoordinate, 0).xy;
    float variance = min(max(moments.y - moments.x * moments.x, 0.0) + 0.000005, 1.0);
    float distanceToMean = shadowDepth - moments.x;
    float upperBound = saturate((variance / (distanceToMean * distanceToMean + variance) - 0.4) * 1.666667);
    return max(upperBound, shadowDepth < moments.x ? 1.0 : 0.0);
}

float SunVisibility(float3 worldPosition, float3 sunDirection)
{
    int cascadeCount = (int)ShadowMapConfiguration.y;
    int cascade = 0;
    [loop]
    while (cascade < cascadeCount && ShadowMapCascadeSplitRadii[cascade] < DistanceFromSunRayToCascadeCenter(worldPosition, sunDirection, cascade))
        cascade++;
    return cascade < cascadeCount ? ShadowNearSingleTap(worldPosition, cascade) : ShadowFar(worldPosition);
}

float4 MainPixel(VertexOutput input) : SV_TARGET0
{
    float2 screenCoordinate = input.Position.xy * ScreenDimsInv.xy;
    float depth = ssDepth.SampleLevel(ssDepthSampler, screenCoordinate, 0).x;
    float3 surface = WorldPositionOf(screenCoordinate, depth);
    float3 sunDirection = normalize(PostLightShSunDir.xyz);
    int2 pixel = int2(input.Position.xy) & 1;
    float jitter = JitterOfPixel[pixel.x + pixel.y * 2];
    float previousAlpha = 0.0;
    float litAlpha = 0.0;
    [loop]
    for (int step = 0; step < MARCH_STEPS; step++)
    {
        float along = (step + jitter) / MARCH_STEPS;
        float3 samplePosition = lerp(WorldCameraPos.xyz, surface, along * along);
        float endAlong = (step + 1.0) / MARCH_STEPS;
        float alpha = ExeFogAlpha(lerp(WorldCameraPos.xyz, surface, endAlong * endAlong));
        float alphaIncrease = max(alpha - previousAlpha, 0.0);
        previousAlpha = max(alpha, previousAlpha);
        litAlpha += alphaIncrease * SunVisibility(samplePosition, sunDirection);
    }
    float litRatio = previousAlpha > 0.0001 ? litAlpha / previousAlpha : 1.0;
    return float4(litRatio, 0.0, 0.0, 0.0);
}
